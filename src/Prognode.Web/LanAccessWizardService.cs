using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Prognode.Core.Connectivity;

namespace Prognode.Web;

// The Core never changes Windows firewall rules. Only a human-approved, elevated
// helper launched at the physical Windows tray may apply these validated requests.
public sealed record LanAccessPrepareRequest(int InterfaceIndex, string SelectedHost,
    string ScopeType, string[]? DeviceIps, bool ConfirmPublic, string Action = "ENABLE");
public sealed record LanInterface(int InterfaceIndex, string Name, string Profile,
    string[] Ipv4, string[] Ipv6, string Cidr, bool Eligible, string? Note);
public sealed record LanAccessPreview(string RequestId, DateTimeOffset ExpiresAtUtc,
    string Action, int InterfaceIndex, string InterfaceName, string Profile,
    string SelectedHost, string ScopeType, string[] RemoteAddresses, int Port,
    string ConfigPath, bool RequiresPublicConsent, string RuleName, string? Warning);
public sealed record LanAccessProbe(string Url, DateTimeOffset ExpiresAtUtc);

public sealed class LanAccessWizardService(ServerAccessService serverAccess, string configPath, X509Certificate2? configuredTlsCertificate)
{
    private readonly object _gate = new();
    private LanAccessPreview? _pending;
    private string? _ownerSession;
    private string? _probeToken;
    private DateTimeOffset _probeUntil;
    private DateTimeOffset? _phoneProbeAt;
    private string? _phoneIp;
    private const int Port = 5443;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private static readonly string StatePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "PROGNODE", "lan-access-state.json");

    public IReadOnlyList<LanInterface> Interfaces()
    {
        var profiles = ReadProfiles();
        var result = new List<LanInterface>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            IPInterfaceProperties props;
            try { props = nic.GetIPProperties(); }
            catch (NetworkInformationException) { continue; }
            var ipv4 = props.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork &&
                !IPAddress.IsLoopback(a.Address) && !a.Address.ToString().StartsWith("169.254.", StringComparison.Ordinal))
                .ToArray();
            int index;
            try { index = props.GetIPv4Properties()?.Index ?? -1; }
            catch (NetworkInformationException) { continue; }
            var profile = profiles.GetValueOrDefault(index, "Unknown");
            var isVpn = nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel ||
                (nic.Name + " " + nic.Description).Contains("tailscale", StringComparison.OrdinalIgnoreCase) ||
                (nic.Name + " " + nic.Description).Contains("vpn", StringComparison.OrdinalIgnoreCase);
            var cidr = ipv4.Length == 1 ? NetworkCidr(ipv4[0].Address, ipv4[0].IPv4Mask) : "";
            var hasWildcardAlias = nic.Name.IndexOfAny(['*', '?', '[', ']']) >= 0;
            var eligible = OperatingSystem.IsWindows() && !isVpn && !hasWildcardAlias &&
                index > 0 && ipv4.Length == 1 &&
                profile is "Public" or "Private" && cidr.Length > 0;
            result.Add(new LanInterface(index, nic.Name, profile,
                ipv4.Select(a => a.Address.ToString()).ToArray(),
                props.UnicastAddresses.Where(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6)
                    .Select(a => a.Address.ToString()).ToArray(), cidr, eligible,
                isVpn ? "VPN/Tailscale cannot be selected for plant LAN pairing." :
                ipv4.Length != 1 ? "A single active LAN IPv4 address is required for this wizard." :
                profile is not ("Public" or "Private") ? "Windows network profile could not be verified." : null));
        }
        return result.OrderBy(x => x.Name).ToArray();
    }

    public LanAccessPreview Prepare(LanAccessPrepareRequest input, string session)
    {
        if (!OperatingSystem.IsWindows()) throw new LanAccessError("WINDOWS_ONLY", "LAN firewall setup requires Windows.");
        var nic = Interfaces().SingleOrDefault(n => n.InterfaceIndex == input.InterfaceIndex && n.Eligible)
                  ?? throw new LanAccessError("INVALID_INTERFACE", "Select one active, non-VPN LAN adapter.");
        var action = (input.Action ?? "ENABLE").Trim().ToUpperInvariant();
        if (action is not ("ENABLE" or "REPAIR" or "DISABLE"))
            throw new LanAccessError("INVALID_ACTION", "Only enable, repair and disable are supported.");
        if (!nic.Ipv4.Contains(input.SelectedHost, StringComparer.Ordinal))
            throw new LanAccessError("INVALID_HOST", "The selected IP does not belong to this interface.");
        if (nic.Profile == "Public" && action != "DISABLE" && !input.ConfirmPublic)
            throw new LanAccessError("PUBLIC_CONFIRMATION_REQUIRED", "Explicitly approve opening the selected Public LAN interface.");
        var saved = ReadSaved();
        if (action == "DISABLE" && (saved is null || saved.InterfaceIndex != nic.InterfaceIndex ||
             saved.SelectedHost != input.SelectedHost))
            throw new LanAccessError("NOT_MANAGED", "Only an existing PROGNODE-managed LAN rule can be disabled.");
        var scope = (input.ScopeType ?? "").Trim().ToUpperInvariant();
        var remote = new List<string>();
        if (action == "DISABLE") { scope = saved!.ScopeType; remote.AddRange(saved.RemoteAddresses); }
        else if (scope == "SUBNET") remote.Add(nic.Cidr);
        else if (scope == "DEVICES")
        {
            if (input.DeviceIps is null || input.DeviceIps.Length is < 1 or > 8)
                throw new LanAccessError("DEVICE_IPS_REQUIRED", "Specify 1–8 approved device IPv4 addresses.");
            foreach (var text in input.DeviceIps)
            {
                if (!IPAddress.TryParse(text, out var ip) || ip.AddressFamily != AddressFamily.InterNetwork ||
                    !IsInside(ip, nic.Cidr))
                    throw new LanAccessError("OUTSIDE_SUBNET", "Approved device IPs must belong to the selected LAN subnet.");
                remote.Add(ip.ToString());
            }
            remote = remote.Distinct(StringComparer.Ordinal).ToList();
        }
        else throw new LanAccessError("INVALID_SCOPE", "Choose approved device IPs or the selected LAN subnet.");
        if (action == "REPAIR" && saved is not null && saved.InterfaceIndex != nic.InterfaceIndex)
            throw new LanAccessError("INTERFACE_CHANGED", "Use Enable to explicitly approve a new interface and scope.");
        var preview = new LanAccessPreview(Convert.ToHexString(RandomNumberGenerator.GetBytes(18)),
            DateTimeOffset.UtcNow.AddMinutes(3), action, nic.InterfaceIndex, nic.Name, nic.Profile,
            input.SelectedHost, scope, remote.ToArray(), Port, configPath,
            nic.Profile == "Public" && action != "DISABLE", $"PROGNODE-MOBILE-LAN-{nic.InterfaceIndex}",
            scope == "SUBNET" && action != "DISABLE" ?
                "Every device within the approved subnet can reach the HTTPS port until app authentication." : null);
        lock (_gate) { _pending = preview; _ownerSession = session; }
        return preview;
    }

    public LanAccessPreview? AgentPending()
    {
        lock (_gate)
            return _pending is not null && _pending.ExpiresAtUtc > DateTimeOffset.UtcNow ? _pending : null;
    }

    public LanAccessPreview Claim(string requestId, Func<string, bool> sessionValid)
    {
        lock (_gate)
        {
            var current = AgentPending();
            if (current is null || current.RequestId != requestId ||
                _ownerSession is null || !sessionValid(_ownerSession))
                throw new LanAccessError("EXPIRED_REQUEST", "LAN request expired or administrator signed out.");
            _pending = null; _ownerSession = null;
            return current;
        }
    }

    public Task<LanTlsHealthResult> CheckHttpsAsync(CancellationToken ct = default) =>
        LanTlsHealth.CheckAsync(configuredTlsCertificate, serverAccess.Identity, ct);

    public async Task<object> StatusAsync(CancellationToken ct = default)
    {
        var tls = await CheckHttpsAsync(ct).ConfigureAwait(false);
        var saved = ReadSaved();
        var iface = saved is null ? null : Interfaces().FirstOrDefault(x =>
            x.InterfaceIndex == saved.InterfaceIndex && x.Ipv4.Contains(saved.SelectedHost));
        var rule = saved is null ? "NOT_CONFIGURED" :
            iface is null ? "INTERFACE_MISSING" :
            iface.Profile != saved.Profile ? "PROFILE_CHANGED" : FirewallHealth(saved);
        var clients = serverAccess.GetClients()
            .Where(x => x.Platform is "ANDROID" or "IOS").ToArray();
        return new { supported = OperatingSystem.IsWindows(), interfaces = Interfaces(),
            httpsReady = tls.Ready,
            tlsDiagnosticCode = tls.DiagnosticCode, tlsDiagnosticMessage = tls.DiagnosticMessage,
            privateKeyUsable = tls.PrivateKeyUsable, actualTlsHandshake = tls.HandshakeSucceeded,
            httpsPort = serverAccess.Identity.SecureApiPort,
            certificateSha256 = serverAccess.Identity.CertificateSha256,
            localHttpsListener = IsLocalPortOpen(serverAccess.Identity.SecureApiPort),
            selected = saved, firewallRuleHealth = rule,
            // Last paired client contact is not proof that the *current* phone can connect.
            lastClientConnectionUtc = clients.Length == 0 ? (DateTime?)null : clients.Max(x => x.LastSeenAtUtc),
            phoneProbeAtUtc = _phoneProbeAt, phoneProbeIp = _phoneIp,
            phoneVerified = tls.Ready && rule == "HEALTHY" && _phoneProbeAt.HasValue && saved is not null &&
                _phoneProbeAt.Value >= saved.ChangedAtUtc,
            pendingRequest = AgentPending() is not null };
    }

    public async Task<LanAccessProbe> CreateProbeAsync(int interfaceIndex, CancellationToken ct = default)
    {
        var saved = ReadSaved() ?? throw new LanAccessError("LAN_NOT_CONFIGURED", "Enable LAN access first.");
        if (saved.InterfaceIndex != interfaceIndex)
            throw new LanAccessError("INVALID_INTERFACE", "Select the configured LAN interface.");
        var tls = await CheckHttpsAsync(ct).ConfigureAwait(false);
        if (!tls.Ready) throw new LanAccessError(tls.DiagnosticCode,
            tls.DiagnosticMessage ?? "Verified localhost TLS handshake is required before testing the phone.");
        lock (_gate)
        {
            _probeToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(24));
            _probeUntil = DateTimeOffset.UtcNow.AddMinutes(5);
            _phoneProbeAt = null; _phoneIp = null;
            return new LanAccessProbe($"https://{saved.SelectedHost}:{Port}/api/mobile-access/phone-probe/{_probeToken}", _probeUntil);
        }
    }

    public bool RecordProbe(string token, IPAddress? remote, IPAddress? localIp, bool isHttps, int localPort)
    {
        var saved = ReadSaved();
        var target = localIp?.IsIPv4MappedToIPv6 == true ? localIp.MapToIPv4() : localIp;
        if (saved is null || target is null || target.ToString() != saved.SelectedHost || remote is null ||
            FirewallHealth(saved) != "HEALTHY") return false;
        var source = remote.IsIPv4MappedToIPv6 ? remote.MapToIPv4() : remote;
        if (source.AddressFamily != AddressFamily.InterNetwork ||
            !(saved.ScopeType == "DEVICES" ? saved.RemoteAddresses.Contains(source.ToString(), StringComparer.Ordinal) :
              saved.ScopeType == "SUBNET" && saved.RemoteAddresses.Length == 1 &&
              IsInside(source, saved.RemoteAddresses[0]))) return false;
        lock (_gate)
        {
            if (!isHttps || localPort != Port || remote is null || IPAddress.IsLoopback(remote) ||
                _probeToken is null || _probeUntil <= DateTimeOffset.UtcNow ||
                token.Length != _probeToken.Length ||
                !CryptographicOperations.FixedTimeEquals(System.Text.Encoding.ASCII.GetBytes(token),
                    System.Text.Encoding.ASCII.GetBytes(_probeToken))) return false;
            _phoneProbeAt = DateTimeOffset.UtcNow; _phoneIp = source.ToString();
            _probeToken = null;
            return true;
        }
    }

    private sealed record SavedState(int InterfaceIndex, string InterfaceName, string Profile,
        string SelectedHost, string ScopeType, string[] RemoteAddresses,
        DateTimeOffset ChangedAtUtc, string RuleName);

    private static SavedState? ReadSaved()
    {
        try
        {
            if (!File.Exists(StatePath)) return null;
            return JsonSerializer.Deserialize<SavedState>(File.ReadAllText(StatePath), Json);
        }
        catch { return null; }
    }

    private static bool IsLocalPortOpen(int? port)
    {
        if (port is null) return false;
        try { using var client = new System.Net.Sockets.TcpClient();
            return client.ConnectAsync(IPAddress.Loopback, port.Value).Wait(TimeSpan.FromMilliseconds(600)) && client.Connected; }
        catch { return false; }
    }

    private static Dictionary<int, string> ReadProfiles()
    {
        var profiles = new Dictionary<int, string>();
        if (!OperatingSystem.IsWindows()) return profiles;
        var data = Powershell("Get-NetConnectionProfile | Select-Object InterfaceIndex,NetworkCategory | ConvertTo-Json -Compress");
        if (data is null) return profiles;
        try
        {
            using var json = JsonDocument.Parse(data);
            var rows = json.RootElement.ValueKind == JsonValueKind.Array ? json.RootElement.EnumerateArray().ToArray() :
                new[] { json.RootElement };
            foreach (var row in rows)
            {
                if (row.TryGetProperty("InterfaceIndex", out var id) && id.TryGetInt32(out var index) &&
                    row.TryGetProperty("NetworkCategory", out var profile))
                {
                    var name = profile.ValueKind == JsonValueKind.String ? profile.GetString() : profile.ToString();
                    // Get-NetConnectionProfile returns either strings or integer enum values.
                    profiles[index] = name switch { "0" => "Public", "1" => "Private", "2" => "DomainAuthenticated", _ => name ?? "Unknown" };
                }
            }
        }
        catch (JsonException) { }
        return profiles;
    }

    private static string? Powershell(string command)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                ArgumentList = { "-NoProfile", "-NonInteractive", "-Command", command }
            });
            if (p is null || !p.WaitForExit(3000) || p.ExitCode != 0) { try { p?.Kill(); } catch { } return null; }
            return p.StandardOutput.ReadToEnd().Trim();
        }
        catch { return null; }
    }

    private static string FirewallHealth(SavedState saved)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(saved.RuleName, @"^PROGNODE-MOBILE-LAN-\d+$"))
            return "UNVERIFIED";
        var rule = Powershell($"$r=Get-NetFirewallRule -Name '{saved.RuleName}' -ErrorAction SilentlyContinue; " +
            "if(-not $r){'MISSING'}else{ $p=$r|Get-NetFirewallPortFilter; " +
            "$a=$r|Get-NetFirewallAddressFilter; $i=$r|Get-NetFirewallInterfaceFilter; " +
            "[pscustomobject]@{ Enabled=$r.Enabled.ToString(); Group=$r.Group; Direction=$r.Direction.ToString(); Action=$r.Action.ToString(); EdgeTraversalPolicy=$r.EdgeTraversalPolicy.ToString(); Profile=$r.Profile.ToString(); " +
            "LocalPort=$p.LocalPort.ToString(); Protocol=$p.Protocol.ToString(); " +
            "LocalAddress=($a.LocalAddress -join ','); RemoteAddress=($a.RemoteAddress -join ','); " +
            "InterfaceAlias=($i.InterfaceAlias -join ',') } | ConvertTo-Json -Compress }");
        if (rule is null) return "UNVERIFIED";
        if (rule == "MISSING") return "MISSING";
        try
        {
            using var parsed = JsonDocument.Parse(rule);
            var e = parsed.RootElement;
            var remote = e.GetProperty("RemoteAddress").GetString() ?? "";
            return e.GetProperty("Enabled").GetString() == "True" &&
                e.GetProperty("Group").GetString() == "PROGNODE LAN Access" &&
                e.GetProperty("Direction").GetString() == "Inbound" &&
                e.GetProperty("Action").GetString() == "Allow" &&
                e.GetProperty("EdgeTraversalPolicy").GetString() == "Block" &&
                e.GetProperty("Profile").GetString() == saved.Profile &&
                e.GetProperty("LocalPort").GetString() == Port.ToString() &&
                e.GetProperty("Protocol").ToString() is "TCP" or "6" &&
                e.GetProperty("LocalAddress").GetString() == saved.SelectedHost &&
                e.GetProperty("InterfaceAlias").GetString() == saved.InterfaceName &&
                saved.RemoteAddresses.OrderBy(x => x).SequenceEqual(remote.Split(',', StringSplitOptions.RemoveEmptyEntries).OrderBy(x => x))
                ? "HEALTHY" : "DRIFTED";
        }
        catch { return "UNVERIFIED"; }
    }

    private static string NetworkCidr(IPAddress ip, IPAddress mask)
    {
        var address = ip.GetAddressBytes(); var bytes = mask.GetAddressBytes();
        if (address.Length != 4 || bytes.Length != 4) return "";
        uint m = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
        var prefix = 0; var zero = false;
        for (int b = 31; b >= 0; b--) { if ((m & (1u << b)) != 0) { if (zero) return ""; prefix++; } else zero = true; }
        if (prefix < 16 || prefix > 30) return ""; // avoid overly broad or point-to-point scopes
        for (int i = 0; i < 4; i++) address[i] &= bytes[i];
        return $"{new IPAddress(address)}/{prefix}";
    }
    private static bool IsInside(IPAddress ip, string cidr)
    {
        var parts = cidr.Split('/');
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var net) || !int.TryParse(parts[1], out var prefix)) return false;
        var a = ip.GetAddressBytes(); var b = net.GetAddressBytes();
        uint x = ((uint)a[0] << 24) | ((uint)a[1] << 16) | ((uint)a[2] << 8) | a[3];
        uint y = ((uint)b[0] << 24) | ((uint)b[1] << 16) | ((uint)b[2] << 8) | b[3];
        uint mask = uint.MaxValue << (32 - prefix);
        return (x & mask) == (y & mask);
    }
}

public sealed class LanAccessError(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
