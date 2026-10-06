using System.Text.Json;

namespace Prognode.RemoteAccess;

public sealed record RemoteAckAuditRecord(
    string CommandId,
    string AlarmKey,
    string UserId,
    string? UserDisplayName,
    Guid? RemoteClientId,
    bool Success,
    string ResultCode,
    DateTimeOffset ProcessedAtUtc);

public sealed class RemoteAckAuditStore
{
    private readonly string _path;
    private readonly object _gate = new();
    private readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public RemoteAckAuditStore(string dataRoot)
    {
        var directory = Path.Combine(dataRoot, "remote-access");
        Directory.CreateDirectory(directory);
        _path = Path.Combine(directory, "ack-audit.jsonl");
    }

    public RemoteAckAuditRecord? Find(string commandId)
    {
        if (string.IsNullOrWhiteSpace(commandId))
            return null;

        lock (_gate)
            return ReadRecentUnsafe(500)
                .LastOrDefault(x => string.Equals(x.CommandId, commandId, StringComparison.Ordinal));
    }

    public void Record(RemoteAckAuditRecord record)
    {
        lock (_gate)
            File.AppendAllText(_path, JsonSerializer.Serialize(record, _json) + Environment.NewLine);
    }

    public IReadOnlyList<RemoteAckAuditRecord> GetRecent(int count = 100)
    {
        lock (_gate)
            return ReadRecentUnsafe(Math.Clamp(count, 1, 500))
                .OrderByDescending(x => x.ProcessedAtUtc)
                .ToArray();
    }

    private IReadOnlyList<RemoteAckAuditRecord> ReadRecentUnsafe(int count)
    {
        if (!File.Exists(_path))
            return [];

        var lines = File.ReadLines(_path).TakeLast(count * 2);
        var result = new List<RemoteAckAuditRecord>();
        foreach (var line in lines)
        {
            try
            {
                var record = JsonSerializer.Deserialize<RemoteAckAuditRecord>(line, _json);
                if (record is not null)
                    result.Add(record);
            }
            catch (JsonException) { }
        }
        return result.TakeLast(count).ToArray();
    }
}
