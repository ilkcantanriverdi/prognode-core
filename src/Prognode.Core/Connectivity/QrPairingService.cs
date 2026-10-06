using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Prognode.Core.Connectivity;

public sealed record QrNetworkOption(string Address, string InterfaceName);
public sealed record QrPairingCreation(
    int SchemaVersion, string PairingMethod, string QrPayload,
    DateTimeOffset ExpiresAtUtc, int ExpiresInSeconds, string SelectedHost, string DisplayName);
public sealed record QrPairingStatus(string Status, DateTimeOffset? ExpiresAtUtc, Guid? PairedClientId);
public sealed class QrPairingProblem(string code, string message, int httpStatus = 400) : Exception(message)
{
    public string Code { get; } = code;
    public int HttpStatus { get; } = httpStatus;
}

/// <summary>
/// In-memory QR tickets: no secrets at rest. A ticket can create exactly one existing
/// ServerAccessService client and is consumed only after persistence succeeds.
/// All ticket transitions and pairing calls are under the same monitor.
/// </summary>
public sealed class QrPairingService(ServerAccessService serverAccess)
{
    private TimeProvider _clock = TimeProvider.System;
    private Func<IReadOnlyList<QrNetworkOption>>? _networkOptions;
    public QrPairingService(ServerAccessService serverAccess, TimeProvider clock) : this(serverAccess)
        => _clock = clock;
    // Explicit injection seam for deterministic offline contract tests; production uses OS adapters.
    public QrPairingService(ServerAccessService serverAccess, TimeProvider clock,
        Func<IReadOnlyList<QrNetworkOption>> networkOptions) : this(serverAccess, clock)
        => _networkOptions = networkOptions;
    private sealed class TicketState
    {
        public required byte[] TicketHash { get; init; }
        public required DateTimeOffset ExpiresAtUtc { get; init; }
        public required string OwnerSession { get; init; }
        public string Status { get; set; } = "PENDING";
        public Guid? PairedClientId { get; set; }
    }

    private readonly object _gate = new();
    private TicketState? _ticket;
    private readonly List<(byte[] Hash, string Status, DateTimeOffset Until)> _past = [];
    private readonly Dictionary<string, List<DateTimeOffset>> _failures = new(StringComparer.Ordinal);
    private const int MaxFailures = 8;
    private static readonly TimeSpan FailureWindow = TimeSpan.FromMinutes(5);
    public const int TicketTtlSeconds = 120;

    public IReadOnlyList<QrNetworkOption> GetNetworkOptions()
    {
        if (_networkOptions is not null) return _networkOptions();
        var result = new List<QrNetworkOption>();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up ||
                nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel ||
                (nic.Name + " " + nic.Description).Contains("tailscale", StringComparison.OrdinalIgnoreCase) ||
                (nic.Name + " " + nic.Description).Contains("vpn", StringComparison.OrdinalIgnoreCase))
                continue;
            try
            {
                foreach (var address in nic.GetIPProperties().UnicastAddresses)
                {
                    var ip = address.Address;
                    if (ip.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(ip) ||
                        ip.Equals(IPAddress.Any) || ip.GetAddressBytes() is [169, 254, _, _])
                        continue;
                    result.Add(new QrNetworkOption(ip.ToString(), nic.Name));
                }
            }
            catch (NetworkInformationException) { /* An adapter may disappear during enumeration. */ }
        }
        return result.DistinctBy(x => x.Address).OrderBy(x => x.InterfaceName).ThenBy(x => x.Address).ToArray();
    }

    public QrPairingCreation Create(string ownerSession, string? selectedHost)
    {
        var identity = serverAccess.Identity;
        if (identity.SecureApiPort is null || identity.CertificateSha256?.Length != 64)
            throw new QrPairingProblem("HTTPS_NOT_READY", "Configure PROGNODE LAN HTTPS and its certificate first.", 409);

        var choices = GetNetworkOptions();
        if (choices.Count == 0)
            throw new QrPairingProblem("NO_LAN_ADDRESS", "No active LAN IPv4 address is available.", 409);
        var host = selectedHost?.Trim();
        if (string.IsNullOrEmpty(host) && choices.Count == 1) host = choices[0].Address;
        if (string.IsNullOrEmpty(host))
            throw new QrPairingProblem("SELECT_NETWORK_INTERFACE", "Choose the LAN adapter accessible by the phone.", 409);
        if (!choices.Any(x => x.Address == host))
            throw new QrPairingProblem("INVALID_LAN_ADDRESS", "Selected IP is not a live local network interface.");

        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            PruneUnsafe(now);
            if (_ticket is not null)
                RetireUnsafe(_ticket.Status == "PAIRED" ? "ALREADY_USED" :
                    _ticket.Status == "PENDING" ? "REVOKED" : _ticket.Status, now);
            var rawTicket = Base64Url(RandomNumberGenerator.GetBytes(32));
            var expires = now.AddSeconds(TicketTtlSeconds);
            _ticket = new TicketState
            {
                TicketHash = SHA256.HashData(Encoding.ASCII.GetBytes(rawTicket)),
                OwnerSession = ownerSession,
                ExpiresAtUtc = expires
            };
            var payload = JsonSerializer.Serialize(new
            {
                v = 1,
                type = "PROGNODE_LAN_PAIRING",
                serverId = identity.ServerId,
                displayName = identity.DisplayName,
                lanHosts = new[] { host },
                httpsPort = identity.SecureApiPort,
                certSha256 = identity.CertificateSha256,
                pairingTicket = rawTicket,
                expiresAtUtc = expires
            });
            return new QrPairingCreation(1, "QR_TICKET_V1", Base64Url(Encoding.UTF8.GetBytes(payload)),
                expires, TicketTtlSeconds, host, identity.DisplayName);
        }
    }

    public QrPairingStatus Status(string ownerSession)
    {
        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            if (_ticket is null || !string.Equals(_ticket.OwnerSession, ownerSession, StringComparison.Ordinal))
                return new QrPairingStatus("NONE", null, null);
            if (_ticket.Status == "PENDING" && now >= _ticket.ExpiresAtUtc) _ticket.Status = "EXPIRED";
            return new QrPairingStatus(_ticket.Status, _ticket.ExpiresAtUtc, _ticket.PairedClientId);
        }
    }

    public void Cancel(string ownerSession)
    {
        lock (_gate)
        {
            if (_ticket?.OwnerSession == ownerSession && _ticket.Status == "PENDING")
                RetireUnsafe("REVOKED", _clock.GetUtcNow());
        }
    }

    public PairClientResult Pair(Guid serverId, string pairingTicket, string clientName,
        string? platform, string? publicKey, IPAddress? requesterIp, Func<string, bool> ownerSessionStillValid)
    {
        var ip = requesterIp?.ToString() ?? "unknown";
        lock (_gate)
        {
            var now = _clock.GetUtcNow();
            PruneUnsafe(now);
            var attempts = _failures.GetValueOrDefault(ip);
            if (attempts is not null && attempts.Count >= MaxFailures)
                throw new QrPairingProblem("RATE_LIMITED", "Too many pairing failures. Try again later.", 429);
            try
            {
                if (serverId != serverAccess.Identity.ServerId)
                    throw new QrPairingProblem("SERVER_MISMATCH", "Scanned QR belongs to another PROGNODE Core.");
                if (pairingTicket is null || pairingTicket.Length != 43 ||
                    pairingTicket.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not ('-' or '_')))
                    throw new QrPairingProblem("INVALID_TICKET", "Invalid QR pairing ticket.");
                var candidate = SHA256.HashData(Encoding.ASCII.GetBytes(pairingTicket));
                if (_ticket is not null && CryptographicOperations.FixedTimeEquals(candidate, _ticket.TicketHash))
                {
                    if (_ticket.Status != "PENDING")
                        throw new QrPairingProblem(_ticket.Status == "PAIRED" ? "ALREADY_USED" : _ticket.Status,
                            "This QR code has already been used or revoked.", 409);
                    if (now >= _ticket.ExpiresAtUtc)
                    {
                        _ticket.Status = "EXPIRED";
                        throw new QrPairingProblem("EXPIRED", "The QR code has expired.", 410);
                    }
                    if (!ownerSessionStillValid(_ticket.OwnerSession))
                    {
                        RetireUnsafe("REVOKED", now);
                        throw new QrPairingProblem("REVOKED", "Administrator session is no longer valid.", 410);
                    }
                    // This call persists the existing client/token BEFORE making the QR ticket unusable.
                    var client = serverAccess.PairWithVerifiedQrTicket(clientName, platform, publicKey);
                    _ticket.Status = "PAIRED";
                    _ticket.PairedClientId = client.ClientId;
                    return client;
                }
                var previous = _past.FirstOrDefault(x => CryptographicOperations.FixedTimeEquals(candidate, x.Hash));
                if (previous.Hash is not null)
                    throw new QrPairingProblem(previous.Status, "This QR code is no longer valid.", 410);
                throw new QrPairingProblem("INVALID_TICKET", "Invalid QR pairing ticket.");
            }
            catch (QrPairingProblem)
            {
                RecordFailedUnsafe(ip, now);
                throw;
            }
            catch (ArgumentException)
            {
                RecordFailedUnsafe(ip, now);
                throw;
            }
        }
    }

    private void RecordFailedUnsafe(string ip, DateTimeOffset now)
    {
        if (!_failures.TryGetValue(ip, out var list)) _failures[ip] = list = [];
        list.Add(now);
    }
    private void RetireUnsafe(string status, DateTimeOffset now)
    {
        if (_ticket is null) return;
        _ticket.Status = status;
        _past.Add((_ticket.TicketHash, status, now.AddMinutes(10)));
        // Retain completed ticket only until next QR, so the UI can show a successful pairing.
    }
    private void PruneUnsafe(DateTimeOffset now)
    {
        _past.RemoveAll(x => x.Until <= now);
        foreach (var key in _failures.Keys.ToArray())
        {
            _failures[key].RemoveAll(t => t < now - FailureWindow);
            if (_failures[key].Count == 0) _failures.Remove(key);
        }
    }
    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
