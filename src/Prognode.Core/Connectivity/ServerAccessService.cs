using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Prognode.Core.Connectivity;

public sealed record ServerIdentitySnapshot(
    Guid ServerId,
    string DisplayName,
    string ApiVersion,
    int ApiPort,
    int DiscoveryPort,
    DateTime CreatedAtUtc,
    bool PairingRequired,
    int? SecureApiPort = null,
    string? CertificateSha256 = null);

public sealed record PairedClientSnapshot(
    Guid ClientId,
    string Name,
    string Platform,
    string? DevicePublicKey,
    DateTime CreatedAtUtc,
    DateTime LastSeenAtUtc,
    bool RemoteEnabled,
    Guid? RemoteClientId,
    DateTimeOffset? RemoteRegisteredAtUtc,
    bool CanViewAlarms = true,
    bool CanAcknowledge = false,
    string AckAuthMode = "USER_SESSION",
    string? GrantedBy = null,
    DateTimeOffset? AccessUpdatedAtUtc = null,
    bool NotificationsEnabled = true);

public sealed record PairClientResult(
    Guid ClientId,
    string AccessToken,
    ServerIdentitySnapshot Server);

internal sealed class ServerAccessDocument
{
    public Guid ServerId { get; set; }
    public string DisplayName { get; set; } = "PROGNODE Server";
    public string ApiVersion { get; set; } = "v1";
    public int ApiPort { get; set; } = 5080;
    public int DiscoveryPort { get; set; } = 5081;
    public DateTime CreatedAtUtc { get; set; }
    public string PairingSecret { get; set; } = string.Empty;
    public List<PairedClientDocument> Clients { get; set; } = [];
    public bool? NotificationDeliveryEnabled { get; set; } // absent in old files => enabled
}

internal sealed class PairedClientDocument
{
    public Guid ClientId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Platform { get; set; } = "Unknown";
    public string? DevicePublicKey { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime LastSeenAtUtc { get; set; }
    public bool RemoteEnabled { get; set; }
    public Guid? RemoteClientId { get; set; }
    public DateTimeOffset? RemoteRegisteredAtUtc { get; set; }
    public bool? CanViewAlarms { get; set; }  // null on older backups => view allowed
    public bool CanAcknowledge { get; set; }   // default DENY
    public string? AckAuthMode { get; set; }   // legacy => USER_SESSION
    public string? GrantedBy { get; set; }
    public DateTimeOffset? AccessUpdatedAtUtc { get; set; }
    public bool? NotificationsEnabled { get; set; } // absent in old pairings => enabled
}

public sealed class ServerAccessService
{
    private readonly string _filePath;
    private readonly int? _secureApiPort;
    private readonly string? _certificateSha256;
    private readonly object _gate = new();
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private ServerAccessDocument _document;
    private readonly Dictionary<Guid, DateTime> _lastSeenPersistenceAttemptUtc = [];
    private readonly Dictionary<string, List<DateTimeOffset>> _pairFailures = new(StringComparer.Ordinal);
    private byte[]? _manualCodeHash;
    private DateTimeOffset _manualCodeExpires;
    private string? _manualCode;
    public const int ManualCodeTtlSeconds = 120;
    private const int MaxManualFailures = 5;
    private static readonly TimeSpan ManualFailureWindow = TimeSpan.FromMinutes(5);


    public ServerAccessService(
        string dataRoot,
        int apiPort,
        int discoveryPort,
        string? configuredName = null,
        int? secureApiPort = null, string? certificateSha256 = null)
    {
        _secureApiPort=secureApiPort;
        _certificateSha256=certificateSha256;
        Directory.CreateDirectory(dataRoot);
        _filePath = Path.Combine(dataRoot, "server-access.json");
        _document = LoadOrCreate(apiPort, discoveryPort, configuredName);
    }

    public ServerIdentitySnapshot Identity
    {
        get
        {
            lock (_gate)
                return ToSnapshot(_document) with { SecureApiPort=_secureApiPort, CertificateSha256=_certificateSha256 };
        }
    }

    // Only local authorized admin endpoints should call this method. A single active code
    // exists per Core. It is generated with CSPRNG, lives 120 s and is consumed atomically.
    public string GetPairingCode()
    {
        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            if (_manualCode is null || now >= _manualCodeExpires)
            {
                _manualCode = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
                _manualCodeHash = SHA256.HashData(Encoding.ASCII.GetBytes(_manualCode));
                _manualCodeExpires = now.AddSeconds(ManualCodeTtlSeconds);
            }
            return _manualCode;
        }
    }

    public int PairingCodeSecondsRemaining()
    {
        lock (_gate) return _manualCode is null ? 0 :
            (int)Math.Max(0, Math.Ceiling((_manualCodeExpires - DateTimeOffset.UtcNow).TotalSeconds));
    }

    public void CancelManualPairingCode()
    {
        lock (_gate) { _manualCode = null; _manualCodeHash = null; _manualCodeExpires = default; }
    }

    public PairClientResult Pair(
        string clientName,
        string pairingCode,
        string? platform = null,
        string? devicePublicKey = null,
        string? requesterIp = null)
    {
        if (string.IsNullOrWhiteSpace(clientName))
            throw new ArgumentException("Client name is required.", nameof(clientName));

        lock (_gate)
        {
            var now = DateTimeOffset.UtcNow;
            var requester = requesterIp ?? "unknown";
            foreach (var key in _pairFailures.Keys.ToArray())
            {
                _pairFailures[key].RemoveAll(t => t < now - ManualFailureWindow);
                if (_pairFailures[key].Count == 0) _pairFailures.Remove(key);
            }
            var keyForLimit = requester + ":" + _document.ServerId.ToString("D");
            if (_pairFailures.TryGetValue(keyForLimit, out var old) && old.Count >= MaxManualFailures)
                throw new ManualPairingException("PAIR_RATE_LIMIT", "Too many manual pairing attempts; try again later.", 429);
            if (!ValidatePairingCodeUnsafe(pairingCode))
            {
                if (!_pairFailures.TryGetValue(keyForLimit, out var failures))
                    _pairFailures[keyForLimit] = failures = [];
                failures.Add(now);
                AuditManualPairingUnsafe("REJECTED", requester, null);
                throw new ManualPairingException("PAIR_CODE_EXPIRED", "Incorrect, expired or already used pairing code.", 409);
            }
            // Do not consume the code if persistence fails. Successful pair consumes it before
            // returning the token and invalidates the code for parallel/replayed requests.
            var result = CreatePairedClientUnsafe(clientName, platform, devicePublicKey);
            _manualCode = null;
            _manualCodeHash = null;
            _manualCodeExpires = default;
            _pairFailures.Clear();
            AuditManualPairingUnsafe("PAIRED", requester, result.ClientId);
            return result;
        }
    }

    /// <summary>QR ticket already checked under the QrPairingService lock; do not expose as HTTP.</summary>
    internal PairClientResult PairWithVerifiedQrTicket(string clientName, string? platform, string? devicePublicKey)
    {
        if (string.IsNullOrWhiteSpace(clientName))
            throw new ArgumentException("Client name is required.", nameof(clientName));
        lock (_gate)
            return CreatePairedClientUnsafe(clientName, platform, devicePublicKey);
    }

    private PairClientResult CreatePairedClientUnsafe(string clientName, string? platform, string? devicePublicKey)
    {
        var rawToken = Base64Url(RandomNumberGenerator.GetBytes(32));
        var now = DateTime.UtcNow;
        var client = new PairedClientDocument
        {
            ClientId = Guid.NewGuid(),
            Name = clientName.Trim()[..Math.Min(clientName.Trim().Length, 128)],
            Platform = NormalizePlatform(platform),
            DevicePublicKey = NormalizeKey(devicePublicKey),
            TokenHash = HashToken(rawToken),
            CreatedAtUtc = now,
            LastSeenAtUtc = now
        };
        _document.Clients.Add(client);
        try { SaveUnsafe(); }
        catch { _document.Clients.Remove(client); throw; }
        return new PairClientResult(client.ClientId, rawToken,
            ToSnapshot(_document) with { SecureApiPort = _secureApiPort, CertificateSha256 = _certificateSha256 });
    }

    public bool ValidateToken(string? rawToken) =>
        TryValidateToken(rawToken, out _);

    public bool TryValidateToken(string? rawToken, out PairedClientSnapshot? pairedClient)
    {
        pairedClient = null;
        if (string.IsNullOrWhiteSpace(rawToken))
            return false;

        var hash = HashToken(rawToken);
        lock (_gate)
        {
            var hashBytes = Encoding.ASCII.GetBytes(hash);
            var client = _document.Clients.FirstOrDefault(x =>
            {
                var candidate = Encoding.ASCII.GetBytes(x.TokenHash ?? string.Empty);
                return candidate.Length == hashBytes.Length &&
                       CryptographicOperations.FixedTimeEquals(candidate, hashBytes);
            });

            if (client is null)
                return false;

            var now = DateTime.UtcNow;
            var lastAttempt = _lastSeenPersistenceAttemptUtc.TryGetValue(client.ClientId, out var attemptedAt)
                ? attemptedAt
                : client.LastSeenAtUtc;
            client.LastSeenAtUtc = now;
            pairedClient = ToSnapshot(client);

            // Last-seen is telemetry, not authorization state. Persist it at most once a
            // minute and never let a stale/read-only data directory reject a valid token.
            if (now - lastAttempt >= TimeSpan.FromMinutes(1))
            {
                _lastSeenPersistenceAttemptUtc[client.ClientId] = now;
                try { SaveUnsafe(); }
                catch (IOException ex)
                {
                    System.Diagnostics.Trace.TraceWarning(
                        "Unable to persist mobile client last-seen telemetry: {0}", ex.Message);
                }
                catch (UnauthorizedAccessException ex)
                {
                    System.Diagnostics.Trace.TraceWarning(
                        "Unable to persist mobile client last-seen telemetry: {0}", ex.Message);
                }
            }
            return true;
        }
    }

    public IReadOnlyList<PairedClientSnapshot> GetClients()
    {
        lock (_gate)
            return _document.Clients
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .Select(ToSnapshot)
                .ToArray();
    }

    public PairedClientSnapshot? GetClient(Guid clientId)
    {
        lock (_gate)
        {
            var client = _document.Clients.FirstOrDefault(x => x.ClientId == clientId);
            return client is null ? null : ToSnapshot(client);
        }
    }

    public bool NotificationDeliveryEnabled
    {
        get { lock (_gate) return _document.NotificationDeliveryEnabled != false; }
    }

    public bool DeliveryEnabledFor(Guid clientId)
    {
        lock (_gate)
        {
            var client=_document.Clients.FirstOrDefault(x=>x.ClientId==clientId);
            return client is not null && _document.NotificationDeliveryEnabled!=false &&
                client.NotificationsEnabled!=false;
        }
    }

    public void SetNotificationDelivery(bool enabled)
    {
        lock (_gate)
        {
            var previous=_document.NotificationDeliveryEnabled;
            _document.NotificationDeliveryEnabled=enabled;
            try { SaveUnsafe(); }
            catch { _document.NotificationDeliveryEnabled=previous; throw; }
        }
    }

    public PairedClientSnapshot? SetClientNotifications(Guid clientId,bool enabled)
    {
        lock (_gate)
        {
            var client=_document.Clients.FirstOrDefault(x=>x.ClientId==clientId);
            if(client is null)return null;
            var previous=client.NotificationsEnabled;
            client.NotificationsEnabled=enabled;
            try { SaveUnsafe(); return ToSnapshot(client); }
            catch { client.NotificationsEnabled=previous; throw; }
        }
    }

    public bool SetRemoteRegistration(
        Guid clientId,
        Guid remoteClientId,
        string devicePublicKey,
        string? platform = null)
    {
        lock (_gate)
        {
            var client = _document.Clients.FirstOrDefault(x => x.ClientId == clientId);
            if (client is null)
                return false;

            client.RemoteEnabled = true;
            client.RemoteClientId = remoteClientId;
            client.RemoteRegisteredAtUtc = DateTimeOffset.UtcNow;
            var normalizedKey = NormalizeKey(devicePublicKey);
            // A changed key invalidates any previous device-only authority.
            if (!string.Equals(client.DevicePublicKey, normalizedKey, StringComparison.Ordinal))
            {
                client.CanAcknowledge = false;
                client.AckAuthMode = "USER_SESSION";
                client.GrantedBy = null;
                client.AccessUpdatedAtUtc = DateTimeOffset.UtcNow;
            }
            client.DevicePublicKey = normalizedKey;
            client.Platform = NormalizePlatform(platform ?? client.Platform);
            SaveUnsafe();
            return true;
        }
    }

    /// <summary>Only call from the Core-host-local authenticated administrator endpoint.</summary>
    public PairedClientSnapshot? SetDeviceAccess(Guid clientId, string? displayName,
        bool canViewAlarms, bool canAcknowledge, string ackAuthMode, string administrator)
    {
        if (ackAuthMode is not ("DEVICE" or "USER_SESSION"))
            throw new ArgumentException("Unsupported ACK mode. PIN requires a separate Core-verified flow.");
        if (string.IsNullOrWhiteSpace(administrator))
            throw new ArgumentException("Authorizing administrator is required.");
        lock (_gate)
        {
            var target = _document.Clients.FirstOrDefault(x => x.ClientId == clientId);
            if (target is null) return null;
            if (canAcknowledge && !canViewAlarms)
                throw new ArgumentException("ACK requires alarm-view permission.");
            if (ackAuthMode == "DEVICE" && canAcknowledge &&
                string.IsNullOrWhiteSpace(target.DevicePublicKey))
                throw new ArgumentException("DEVICE ACK requires a QR-paired public key. Re-pair this phone.");
            var oldState = (target.Name,target.CanViewAlarms,target.CanAcknowledge,
                target.AckAuthMode,target.GrantedBy,target.AccessUpdatedAtUtc);
            try
            {
                if (!string.IsNullOrWhiteSpace(displayName))
                    target.Name = displayName.Trim()[..Math.Min(128, displayName.Trim().Length)];
                target.CanViewAlarms = canViewAlarms;
                target.CanAcknowledge = canAcknowledge;
                target.AckAuthMode = ackAuthMode;
                target.GrantedBy = administrator;
                target.AccessUpdatedAtUtc = DateTimeOffset.UtcNow;
                SaveUnsafe();
                return ToSnapshot(target);
            }
            catch
            {
                (target.Name,target.CanViewAlarms,target.CanAcknowledge,
                 target.AckAuthMode,target.GrantedBy,target.AccessUpdatedAtUtc) = oldState;
                throw;
            }
        }
    }

    public bool ClearRemoteRegistration(Guid clientId)
    {
        lock (_gate)
        {
            var client = _document.Clients.FirstOrDefault(x => x.ClientId == clientId);
            if (client is null)
                return false;

            client.RemoteEnabled = false;
            client.RemoteClientId = null;
            client.RemoteRegisteredAtUtc = null;
            SaveUnsafe();
            return true;
        }
    }

    public bool RevokeClient(Guid clientId)
    {
        lock (_gate)
        {
            var target = _document.Clients.FirstOrDefault(x => x.ClientId == clientId);
            if (target?.RemoteEnabled == true)
                throw new InvalidOperationException("Revoke Remote Access before removing this paired client so its seat can be released in PROGNODE Cloud.");

            var removed = _document.Clients.RemoveAll(x => x.ClientId == clientId) > 0;
            if (removed)
                SaveUnsafe();
            return removed;
        }
    }

    private ServerAccessDocument LoadOrCreate(int apiPort, int discoveryPort, string? configuredName)
    {
        if (File.Exists(_filePath))
        {
            try
            {
                var loaded = JsonSerializer.Deserialize<ServerAccessDocument>(File.ReadAllText(_filePath), _jsonOptions);
                if (loaded is not null && loaded.ServerId != Guid.Empty && !string.IsNullOrWhiteSpace(loaded.PairingSecret))
                {
                    loaded.Clients ??= [];
                    foreach (var client in loaded.Clients)
                    {
                        client.Platform = NormalizePlatform(client.Platform);
                        client.DevicePublicKey = NormalizeKey(client.DevicePublicKey);
                    }
                    loaded.ApiPort = apiPort;
                    loaded.DiscoveryPort = discoveryPort;
                    if (!string.IsNullOrWhiteSpace(configuredName))
                        loaded.DisplayName = configuredName.Trim();
                    File.WriteAllText(_filePath, JsonSerializer.Serialize(loaded, _jsonOptions));
                    return loaded;
                }
            }
            catch
            {
                // A damaged identity file is replaced with a new local identity.
            }
        }

        var serverId = Guid.NewGuid();
        var suffix = serverId.ToString("N")[..6].ToUpperInvariant();
        var created = new ServerAccessDocument
        {
            ServerId = serverId,
            DisplayName = !string.IsNullOrWhiteSpace(configuredName)
                ? configuredName.Trim()
                : $"PROGNODE-{suffix}",
            ApiVersion = "v1",
            ApiPort = apiPort,
            DiscoveryPort = discoveryPort,
            CreatedAtUtc = DateTime.UtcNow,
            PairingSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        };

        File.WriteAllText(_filePath, JsonSerializer.Serialize(created, _jsonOptions));
        return created;
    }

    // Never log OTP, ticket, bearer token or public-key material. The pairing operation
    // has already committed before audit; logging problems are non-authoritative and
    // must not leak an issued token in HTTP error details.
    private void AuditManualPairingUnsafe(string outcome, string requester, Guid? clientId)
    {
        try
        {
            var directory=Path.Combine(Path.GetDirectoryName(_filePath)!,"audit");
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory,"manual-pairing.jsonl"),
                JsonSerializer.Serialize(new {utc=DateTimeOffset.UtcNow,serverId=_document.ServerId,
                    outcome,requester,clientId})+Environment.NewLine);
        }
        catch(IOException) { /* Verify authorization solely from persisted pairing state. */ }
        catch(UnauthorizedAccessException) { /* Audit storage health is reported separately. */ }
    }

    private bool ValidatePairingCodeUnsafe(string? pairingCode)
    {
        if (_manualCodeHash is null || _manualCode is null ||
            DateTimeOffset.UtcNow >= _manualCodeExpires || pairingCode is null ||
            pairingCode.Length != 6 || pairingCode.Any(c => c is < '0' or > '9')) return false;
        var supplied = SHA256.HashData(Encoding.ASCII.GetBytes(pairingCode));
        return CryptographicOperations.FixedTimeEquals(supplied, _manualCodeHash);
    }

    private static string HashToken(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private static string Base64Url(byte[] value) =>
        Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string NormalizePlatform(string? value) =>
        string.IsNullOrWhiteSpace(value) ? "Unknown" : value.Trim()[..Math.Min(value.Trim().Length, 64)];

    private static string? NormalizeKey(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        var clean = value.Trim();
        return clean.Length <= 1024 ? clean : clean[..1024];
    }

    private static ServerIdentitySnapshot ToSnapshot(ServerAccessDocument document) =>
        new(document.ServerId, document.DisplayName, document.ApiVersion, document.ApiPort,
            document.DiscoveryPort, document.CreatedAtUtc, true);

    private static PairedClientSnapshot ToSnapshot(PairedClientDocument client) =>
        new(client.ClientId, client.Name, client.Platform, client.DevicePublicKey,
            client.CreatedAtUtc, client.LastSeenAtUtc, client.RemoteEnabled,
            client.RemoteClientId, client.RemoteRegisteredAtUtc,
            client.CanViewAlarms != false, client.CanAcknowledge,
            client.AckAuthMode is "DEVICE" ? "DEVICE" : "USER_SESSION",
            client.GrantedBy, client.AccessUpdatedAtUtc,
            client.NotificationsEnabled != false);

    private void SaveUnsafe()
    {
        // Avoid partial JSON on crash/power loss, especially while writing device grants.
        var temporary=_filePath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            File.WriteAllText(temporary,JsonSerializer.Serialize(_document,_jsonOptions));
            File.Move(temporary,_filePath,overwrite:true);
        }
        finally { try { if(File.Exists(temporary)) File.Delete(temporary); } catch {} }
    }
}
