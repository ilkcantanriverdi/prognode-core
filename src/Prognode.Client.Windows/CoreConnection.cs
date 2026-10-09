using System.Net;
using System.Net.Http.Json;
using System.Net.Security;
using System.Net.Sockets;
using System.Numerics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Prognode.Contracts.Alarms;
using Prognode.Contracts.Notifications;

namespace Prognode.Client.Windows;

public sealed record DiscoveredCore(Guid ServerId, string DisplayName, IPAddress Address, int SecurePort)
{
    public override string ToString() => $"{DisplayName}   ·   {Address}";
}

public sealed record CoreCandidate(Guid ServerId, string DisplayName, string Host, int Port, string CertificateSha256, string CheckCode);

public sealed record DeviceAccess(bool CanViewAlarms, bool CanAcknowledge, string AckAuthMode, bool RequiresUserLogin)
{
    public bool DeviceAck => CanViewAlarms && CanAcknowledge && AckAuthMode == "DEVICE" && !RequiresUserLogin;
}

public sealed record NotificationPage(IReadOnlyList<NotificationEvent> Items, bool HasMore, long NextCursor);

/// <summary>
/// Talks to PROGNODE Core over LAN HTTPS. Every request is pinned to the certificate the operator
/// confirmed with the check code shown on the Core screen; redirects and proxies are never used.
/// </summary>
public sealed class CoreConnection : IDisposable
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private const int DiscoveryPort = 5081;
    private readonly HttpClient _http;

    public CoreConnection(string host, int port, string pinnedSha256, string? token = null)
    {
        var pin = pinnedSha256.Replace(":", "").Trim().ToUpperInvariant();
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(8),
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, cert, _, _) =>
                    cert is not null && Convert.ToHexString(SHA256.HashData(cert.GetRawCertData())) == pin,
            },
        };
        _http = new HttpClient(handler) { BaseAddress = new Uri($"https://{host}:{port}/"), Timeout = TimeSpan.FromSeconds(12) };
        if (!string.IsNullOrEmpty(token))
            _http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
    }

    /// <summary>UDP broadcast discovery; replies are only hints until the certificate is checked.</summary>
    public static async Task<IReadOnlyList<DiscoveredCore>> DiscoverAsync(CancellationToken ct = default)
    {
        var found = new List<DiscoveredCore>();
        using var udp = new UdpClient(AddressFamily.InterNetwork) { EnableBroadcast = true };
        var request = Encoding.UTF8.GetBytes("PROGNODE_DISCOVER_V1");
        await udp.SendAsync(request, request.Length, new IPEndPoint(IPAddress.Broadcast, DiscoveryPort));
        using var window = CancellationTokenSource.CreateLinkedTokenSource(ct);
        window.CancelAfter(TimeSpan.FromSeconds(2.5));
        while (!window.IsCancellationRequested)
        {
            try
            {
                var result = await udp.ReceiveAsync(window.Token);
                using var doc = JsonDocument.Parse(result.Buffer);
                var root = doc.RootElement;
                // Core answers with PascalCase identity fields ("ServerId"); match names case-insensitively.
                if (Field(root, "type")?.GetString() != "PROGNODE_SERVER_V1") continue;
                if (Field(root, "serverId") is not { ValueKind: JsonValueKind.String } idElement || !idElement.TryGetGuid(out var id)) continue;
                var port = Field(root, "secureApiPort") is { ValueKind: JsonValueKind.Number } sp ? sp.GetInt32() : 5443;
                if (found.Any(x => x.ServerId == id)) continue;
                var name = Field(root, "displayName") is { ValueKind: JsonValueKind.String } dn ? dn.GetString() : null;
                found.Add(new DiscoveredCore(id, string.IsNullOrWhiteSpace(name) ? "PROGNODE Core" : name, result.RemoteEndPoint.Address, port));
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or FormatException) { }
        }
        return found;
    }

    private static JsonElement? Field(JsonElement root, string name)
    {
        if (root.ValueKind != JsonValueKind.Object) return null;
        foreach (var property in root.EnumerateObject())
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) return property.Value;
        return null;
    }

    /// <summary>
    /// TLS handshake only (nothing is sent), then a pinned identity request. Returns the check code
    /// the operator compares with the PROGNODE Core screen (same algorithm as the mobile app).
    /// </summary>
    public static async Task<CoreCandidate> InspectAsync(string host, int port, Guid? expectedServerId, CancellationToken ct = default)
    {
        byte[] der;
        using (var tcp = new TcpClient())
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            await tcp.ConnectAsync(host, port, timeout.Token);
            X509Certificate? captured = null;
            await using var ssl = new SslStream(tcp.GetStream(), false, (_, cert, _, _) => { captured = cert; return true; });
            await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = host }, timeout.Token);
            der = captured?.GetRawCertData() ?? throw new InvalidOperationException("PROGNODE Core did not present a certificate.");
        }
        var pin = Convert.ToHexString(SHA256.HashData(der));
        using var pinned = new CoreConnection(host, port, pin);
        var identity = await pinned._http.GetFromJsonAsync<JsonElement>("api/server/identity", ct);
        var serverId = identity.GetProperty("serverId").GetGuid();
        if (expectedServerId is { } expected && expected != serverId)
            throw new InvalidOperationException("The discovered Core and the Core answering on HTTPS are not the same.");
        var name = identity.TryGetProperty("displayName", out var dn) ? dn.GetString() ?? "PROGNODE Core" : "PROGNODE Core";
        return new CoreCandidate(serverId, name, host, port, pin, CheckCode(serverId, der));
    }

    /// <summary>SAS V1: SHA-256("PROGNODE-MANUAL-PAIR-V1\0" + serverId + "\0" + SHA-256(cert)) → 60 bits → 12 Crockford digits.</summary>
    public static string CheckCode(Guid serverId, byte[] certificateDer)
    {
        const string alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
        var material = new List<byte>();
        material.AddRange(Encoding.UTF8.GetBytes("PROGNODE-MANUAL-PAIR-V1\0"));
        material.AddRange(Encoding.UTF8.GetBytes(serverId.ToString("D").ToLowerInvariant()));
        material.Add(0);
        material.AddRange(SHA256.HashData(certificateDer));
        var hash = SHA256.HashData(material.ToArray());
        BigInteger value = 0;
        for (var i = 0; i < 8; i++) value = (value << 8) | hash[i];
        value >>= 4;
        var code = new StringBuilder();
        for (var shift = 55; shift >= 0; shift -= 5) code.Append(alphabet[(int)((value >> shift) & 31)]);
        var raw = code.ToString();
        return $"{raw[..4]}-{raw[4..8]}-{raw[8..12]}";
    }

    /// <param name="devicePublicKey">This PC's Ed25519 identity; Core requires it before an administrator can allow ACK.</param>
    public async Task<(Guid ClientId, string Token)> PairAsync(Guid serverId, string clientName, string pairingCode, string devicePublicKey, CancellationToken ct = default)
    {
        using var response = await _http.PostAsJsonAsync("api/client/pair", new
        {
            serverId,
            clientName,
            pairingCode = pairingCode.Trim(),
            platform = "Windows",
            devicePublicKey,
        }, ct);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(ct);
        if (!response.IsSuccessStatusCode)
        {
            var message = body.ValueKind == JsonValueKind.Object && body.TryGetProperty("message", out var m) ? m.GetString() : null;
            throw new InvalidOperationException(message ?? $"Pairing was refused (HTTP {(int)response.StatusCode}). Check the code and that it has not expired.");
        }
        return (body.GetProperty("clientId").GetGuid(), body.GetProperty("accessToken").GetString() ?? throw new InvalidOperationException("Core returned no access token."));
    }

    public async Task<DeviceAccess?> GetAccessAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync("api/mobile/v2/device/access", ct);
        if (response.StatusCode is HttpStatusCode.NotFound) return null;
        EnsureAuthorized(response);
        return await response.Content.ReadFromJsonAsync<DeviceAccess>(Json, ct);
    }

    public async Task<IReadOnlyList<AlarmRuntimeSnapshot>> GetActiveAlarmsAsync(CancellationToken ct = default)
    {
        using var response = await _http.GetAsync("api/mobile/v2/alarms/active", ct);
        EnsureAuthorized(response);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json, ct);
        return body.GetProperty("items").Deserialize<List<AlarmRuntimeSnapshot>>(Json) ?? [];
    }

    public async Task<NotificationPage> GetNotificationsAsync(long after, CancellationToken ct = default)
    {
        using var response = await _http.GetAsync($"api/mobile/v2/notifications?cursor={after}&limit=100", ct);
        EnsureAuthorized(response);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(Json, ct);
        var items = body.GetProperty("items").Deserialize<List<NotificationEvent>>(Json) ?? [];
        var next = long.TryParse(body.GetProperty("nextCursor").GetString(), out var n) ? n : after;
        return new NotificationPage(items, body.TryGetProperty("hasMore", out var more) && more.GetBoolean(), next);
    }

    public async Task AcknowledgeAsync(Guid occurrenceId, CancellationToken ct = default)
    {
        using var response = await _http.PostAsync($"api/mobile/v2/alarms/{occurrenceId:D}/ack", new StringContent("{}", Encoding.UTF8, "application/json"), ct);
        if (response.StatusCode == HttpStatusCode.Conflict)
            throw new InvalidOperationException("The alarm restarted or was already handled. Refresh and try again.");
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            throw new InvalidOperationException("This PC is not allowed to acknowledge alarms. A Core administrator can allow it in Settings › Mobile Access › Paired devices.");
        response.EnsureSuccessStatusCode();
    }

    private static void EnsureAuthorized(HttpResponseMessage response)
    {
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            throw new UnauthorizedAccessException("This PC is no longer paired with PROGNODE Core.");
        response.EnsureSuccessStatusCode();
    }

    public void Dispose() => _http.Dispose();
}
