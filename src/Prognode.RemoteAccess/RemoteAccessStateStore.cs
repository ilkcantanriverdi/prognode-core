using System.Text.Json;

namespace Prognode.RemoteAccess;

internal sealed class RemoteAccessCacheDocument
{
    public bool ServerBound { get; set; }
    public string? ServerAccessToken { get; set; }
    public string BindingStatus { get; set; } = "UNBOUND";
    public string SubscriptionStatus { get; set; } = "UNKNOWN";
    public bool UnlimitedClients { get; set; }
    public int? MaxClients { get; set; }
    public int UsedClients { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public DateTimeOffset? LastSyncedAtUtc { get; set; }
    public string? LastError { get; set; }
    public List<RemoteAccessCloudClientDocument> Clients { get; set; } = [];
}

internal sealed class RemoteAccessCloudClientDocument
{
    public Guid RemoteClientId { get; set; }
    public Guid? LocalClientId { get; set; }
    public string DeviceName { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string? UserId { get; set; }
    public string? UserDisplayName { get; set; }
    public string? DevicePublicKey { get; set; }
    public string Status { get; set; } = "ACTIVE";
    public DateTimeOffset? RegisteredAtUtc { get; set; }
    public DateTimeOffset? LastSeenAtUtc { get; set; }
}

public sealed class RemoteAccessStateStore
{
    private readonly string _path;
    private readonly object _gate = new();
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public RemoteAccessStateStore(string dataRoot)
    {
        var directory = Path.Combine(dataRoot, "remote-access");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "state.json");
    }

    internal RemoteAccessCacheDocument Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_path))
                return new RemoteAccessCacheDocument();

            try
            {
                return JsonSerializer.Deserialize<RemoteAccessCacheDocument>(File.ReadAllText(_path), _json)
                    ?? new RemoteAccessCacheDocument();
            }
            catch
            {
                return new RemoteAccessCacheDocument
                {
                    LastError = "Remote Access cache could not be read. Cloud sync will rebuild it."
                };
            }
        }
    }

    internal void Save(RemoteAccessCacheDocument document)
    {
        lock (_gate)
            File.WriteAllText(_path, JsonSerializer.Serialize(document, _json));
    }
}
