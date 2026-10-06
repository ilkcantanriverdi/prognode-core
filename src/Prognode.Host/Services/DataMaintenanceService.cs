using Microsoft.Data.Sqlite;
using Prognode.Alarm;
using Prognode.Core.Tags;
using Prognode.Data.Sqlite;

namespace Prognode.Host.Services;

public sealed class DataMaintenanceService(SqliteDatabaseOptions options,
    AlarmRuntimeStore alarmRuntime, TagService tags)
{
    private static readonly HashSet<string> Allowed =
    ["devices", "tags", "alarms", "alarmHistory", "historian", "trends", "batchHistory", "notifications"];

    public async Task<IReadOnlyDictionary<string, int>> DeleteAsync(
        IReadOnlyCollection<string> requested, CancellationToken ct = default)
    {
        var selected = requested.Where(Allowed.Contains).ToHashSet(StringComparer.Ordinal);
        if (selected.Count == 0 || selected.Count != requested.Count)
            throw new ArgumentException("Select at least one supported data category.");

        await using var db = new SqliteConnection(options.ConnectionString);
        await db.OpenAsync(ct);
        await using (var pragma = db.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys=ON;";
            await pragma.ExecuteNonQueryAsync(ct);
        }
        await using var tx = await db.BeginTransactionAsync(ct);
        var deleted = new Dictionary<string, int>(StringComparer.Ordinal);
        var tagIdsToForget = new HashSet<Guid>();
        var alarmIdsToForget = new HashSet<Guid>();

        if (selected.Contains("devices") || selected.Contains("tags"))
        {
            var filter = selected.Contains("devices") && !selected.Contains("tags")
                ? "WHERE device_id IN (SELECT id FROM devices)" : string.Empty;
            await using var tagQuery = db.CreateCommand();
            tagQuery.Transaction = (SqliteTransaction)tx;
            tagQuery.CommandText = $"SELECT id FROM tags {filter};";
            await using var reader = await tagQuery.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                if (Guid.TryParse(reader.GetString(0), out var id)) tagIdsToForget.Add(id);
            await reader.DisposeAsync();
        }

        if (selected.Contains("alarms"))
        {
            await using var alarmQuery = db.CreateCommand();
            alarmQuery.Transaction = (SqliteTransaction)tx;
            alarmQuery.CommandText = "SELECT id FROM alarm_definitions;";
            await using var reader = await alarmQuery.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                if (Guid.TryParse(reader.GetString(0), out var id)) alarmIdsToForget.Add(id);
        }
        else if (tagIdsToForget.Count > 0)
        {
            await using var alarmQuery = db.CreateCommand();
            alarmQuery.Transaction = (SqliteTransaction)tx;
            alarmQuery.CommandText = "SELECT id FROM alarm_definitions WHERE tag_id IN (SELECT id FROM tags WHERE device_id IN (SELECT id FROM devices))";
            if (selected.Contains("tags")) alarmQuery.CommandText = "SELECT id FROM alarm_definitions WHERE tag_id IN (SELECT id FROM tags)";
            await using var reader = await alarmQuery.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                if (Guid.TryParse(reader.GetString(0), out var id)) alarmIdsToForget.Add(id);
        }

        async Task<int> Run(string key, string sql)
        {
            await using var command = db.CreateCommand();
            command.Transaction = (SqliteTransaction)tx;
            command.CommandText = sql;
            var count = await command.ExecuteNonQueryAsync(ct);
            deleted[key] = deleted.GetValueOrDefault(key) + count;
            return count;
        }

        if (selected.Contains("notifications"))
            await Run("notifications", "DELETE FROM notification_events;");
        if (selected.Contains("alarmHistory"))
            await Run("alarmHistory", "DELETE FROM alarm_events;");
        if (selected.Contains("batchHistory"))
        {
            await Run("batchHistory", "DELETE FROM batch_alarm_occurrences;");
            await Run("batchHistory", "DELETE FROM batch_runs;");
        }
        if (selected.Contains("trends"))
            await Run("trends", "DELETE FROM trend_definitions;");
        if (selected.Contains("historian"))
        {
            await Run("historian", "DELETE FROM historian_samples;");
            await Run("historian", "DELETE FROM historian_recording_configs;");
        }
        if (selected.Contains("alarms"))
            await Run("alarms", "DELETE FROM alarm_definitions;");
        if (tagIdsToForget.Count > 0)
        {
            if (!selected.Contains("alarms") && alarmIdsToForget.Count > 0)
            {
                await using var removeRules = db.CreateCommand();
                removeRules.Transaction = (SqliteTransaction)tx;
                removeRules.CommandText = selected.Contains("devices") && !selected.Contains("tags")
                    ? "DELETE FROM alarm_definitions WHERE tag_id IN (SELECT id FROM tags WHERE device_id IN (SELECT id FROM devices));"
                    : "DELETE FROM alarm_definitions WHERE tag_id IN (SELECT id FROM tags);";
                await removeRules.ExecuteNonQueryAsync(ct);
            }
            if (selected.Contains("tags"))
                await Run("tags", "DELETE FROM tags;");
            else if (selected.Contains("devices"))
                await Run("tags", "DELETE FROM tags WHERE device_id IN (SELECT id FROM devices);");
        }
        if (selected.Contains("devices"))
            await Run("devices", "DELETE FROM devices;");

        await tx.CommitAsync(ct);
        foreach (var id in tagIdsToForget) tags.ForgetCurrentValue(id);
        foreach (var id in alarmIdsToForget) alarmRuntime.Remove(AlarmService.KeyFor(id));
        return deleted;
    }
}

public sealed record DataResetRequest(string[]? Categories, string? Password);
