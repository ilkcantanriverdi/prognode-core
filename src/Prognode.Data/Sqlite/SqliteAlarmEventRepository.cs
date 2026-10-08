using Microsoft.Data.Sqlite;
using Prognode.Alarm;
using Prognode.Contracts.Alarms;

namespace Prognode.Data.Sqlite;

public sealed class SqliteAlarmEventRepository(
    SqliteDatabaseOptions options) : IAlarmEventRepository
{
    private const string SelectColumns = """
        id, alarm_key, definition_id, tag_id, device_id, is_system,
        source_name, alarm_text, priority, event_type, event_time, batch_id, occurrence_id,
        acknowledged_by
        """;
    private const string OccurrenceFilter = """
        ($priority='' OR a.priority=$priority COLLATE NOCASE) AND
        ($search='' OR instr(lower(a.source_name),$search)>0 OR
          instr(lower(a.alarm_text),$search)>0 OR
          instr(lower(a.alarm_key),$search)>0 OR
          instr(lower(a.priority),$search)>0 OR
          EXISTS (SELECT 1 FROM devices d WHERE d.id=a.device_id
            AND instr(lower(d.name),$search)>0))
        """;

    public async Task AddAsync(
        AlarmEventRecord item,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = """
        INSERT INTO alarm_events (
            alarm_key, definition_id, tag_id, device_id, is_system,
            source_name, alarm_text, priority, event_type, event_time, batch_id, occurrence_id,
            acknowledged_by)
        VALUES (
            $alarmKey, $definitionId, $tagId, $deviceId, $isSystem,
            $sourceName, $text, $priority, $eventType, $eventTime, $batchId, $occurrenceId,
            $acknowledgedBy);
        """;

        command.Parameters.AddWithValue("$alarmKey", item.AlarmKey);
        command.Parameters.AddWithValue("$definitionId", (object?)item.DefinitionId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$tagId", (object?)item.TagId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$deviceId", (object?)item.DeviceId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$isSystem", item.IsSystem ? 1 : 0);
        command.Parameters.AddWithValue("$sourceName", item.SourceName);
        command.Parameters.AddWithValue("$text", item.Text);
        command.Parameters.AddWithValue("$priority", item.Priority.ToString());
        command.Parameters.AddWithValue("$eventType", item.EventType.ToString());
        command.Parameters.AddWithValue("$eventTime", item.Timestamp.ToString("O"));
        command.Parameters.AddWithValue("$batchId", (object?)item.BatchId?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$occurrenceId", item.OccurrenceId == Guid.Empty ? DBNull.Value : item.OccurrenceId.ToString("D"));
        command.Parameters.AddWithValue("$acknowledgedBy", (object?)item.AcknowledgedBy ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task LinkOccurrenceToBatchAsync(
        string alarmKey,
        DateTimeOffset activeAt,
        Guid batchId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
        INSERT OR IGNORE INTO batch_alarm_occurrences(batch_id,alarm_key,active_at)
        VALUES($batchId,$alarmKey,$activeAt);
        """;
        command.Parameters.AddWithValue("$batchId", batchId.ToString());
        command.Parameters.AddWithValue("$alarmKey", alarmKey);
        command.Parameters.AddWithValue("$activeAt", activeAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AlarmEventRecord>> GetRecentAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM alarm_events ORDER BY id DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 6000));
        return await ReadEventsAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<AlarmOccurrenceRecord>> GetRecentOccurrencesAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 500);
        var eventLimit = Math.Clamp(limit * 12, 200, 6000);
        var recentEvents = await GetRecentAsync(eventLimit, cancellationToken);
        return GroupOccurrences(recentEvents, limit);
    }

    public async Task<long> CountOccurrencesAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM alarm_events WHERE event_type = 'Active';";
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<IReadOnlyList<AlarmOccurrenceRecord>> GetOccurrencePageAsync(
        int offset, int limit, string? search = null, string? priority = null,
        CancellationToken cancellationToken = default)
    {
        offset = Math.Max(0, offset);
        limit = Math.Clamp(limit, 1, 100);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
        WITH page AS (
          SELECT a.id, a.alarm_key, a.occurrence_id FROM alarm_events a
          WHERE a.event_type='Active' AND {OccurrenceFilter}
          ORDER BY id DESC LIMIT $limit OFFSET $offset
        )
        SELECT {PrefixColumns("e")}
        FROM alarm_events e
        JOIN page p ON e.id=p.id OR
          (p.occurrence_id IS NOT NULL AND p.occurrence_id<>''
            AND e.occurrence_id=p.occurrence_id AND e.id>p.id) OR
          ((p.occurrence_id IS NULL OR p.occurrence_id='') AND e.alarm_key=p.alarm_key
            AND e.id>p.id AND e.id<COALESCE(
              (SELECT MIN(n.id) FROM alarm_events n WHERE n.alarm_key=p.alarm_key
                AND n.event_type='Active' AND n.id>p.id), 9223372036854775807))
        ORDER BY e.id DESC;
        """;
        command.Parameters.AddWithValue("$limit", limit);
        command.Parameters.AddWithValue("$offset", offset);
        command.Parameters.AddWithValue("$search", (search ?? "").Trim().ToLowerInvariant());
        command.Parameters.AddWithValue("$priority", (priority ?? "").Trim());
        var pageEvents = await ReadEventsAsync(command, cancellationToken);
        return GroupOccurrences(pageEvents, limit);
    }

    public async Task<long> CountMatchingOccurrencesAsync(string? search, string? priority,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM alarm_events a WHERE a.event_type='Active' AND {OccurrenceFilter};";
        command.Parameters.AddWithValue("$search", (search ?? "").Trim().ToLowerInvariant());
        command.Parameters.AddWithValue("$priority", (priority ?? "").Trim());
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    public async Task<int> DeleteOccurrencesAsync(IReadOnlyCollection<Guid> occurrenceIds,
        CancellationToken cancellationToken = default)
    {
        var ids = occurrenceIds.Where(x => x != Guid.Empty).Distinct().Take(100).ToArray();
        if (ids.Length == 0) return 0;
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = "DELETE FROM alarm_events WHERE occurrence_id IN (" +
            string.Join(",", ids.Select((_, i) => "$id" + i)) + ");";
        for (var i = 0; i < ids.Length; i++)
            command.Parameters.AddWithValue("$id" + i, ids[i].ToString("D"));
        var deleted = await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return deleted;
    }

    public async Task<IReadOnlyList<AlarmOccurrenceRecord>> GetOccurrencesByBatchAsync(
        Guid batchId,
        int limit,
        CancellationToken cancellationToken = default)
    {
        limit = Math.Clamp(limit, 1, 500);
        var eventLimit = Math.Clamp(limit * 12, 200, 6000);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        // The mapping table makes Batch membership many-to-many. A long-running alarm can
        // overlap sequential Lots without losing either relationship. Each linked window
        // starts at one persisted Active event and ends immediately before the next Active
        // event for the same alarm key.
        command.CommandText = $"""
        SELECT DISTINCT {PrefixColumns("e")}
        FROM batch_alarm_occurrences l
        JOIN alarm_events e ON e.alarm_key=l.alarm_key
        WHERE l.batch_id=$batchId
          AND e.event_time >= l.active_at
          AND e.event_time < COALESCE(
              (SELECT MIN(n.event_time)
               FROM alarm_events n
               WHERE n.alarm_key=l.alarm_key
                 AND n.event_type='Active'
                 AND n.event_time > l.active_at),
              '9999-12-31T23:59:59.9999999+00:00')
        ORDER BY e.id DESC
        LIMIT $limit;
        """;
        command.Parameters.AddWithValue("$batchId", batchId.ToString());
        command.Parameters.AddWithValue("$limit", eventLimit);
        var events = await ReadEventsAsync(command, cancellationToken);
        return GroupOccurrences(events, limit, batchId);
    }

    private static string PrefixColumns(string alias) =>
        string.Join(", ", SelectColumns
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => $"{alias}.{x}"));

    private static IReadOnlyList<AlarmOccurrenceRecord> GroupOccurrences(
        IReadOnlyList<AlarmEventRecord> events,
        int limit,
        Guid? forcedBatchId = null)
    {
        var pending = new Dictionary<string, PendingOccurrence>(StringComparer.OrdinalIgnoreCase);
        var result = new List<AlarmOccurrenceRecord>(limit);

        foreach (var item in events)
        {
            var groupKey = item.OccurrenceId == Guid.Empty ? item.AlarmKey : item.OccurrenceId.ToString("D");
            switch (item.EventType)
            {
                case AlarmEventType.Cleared:
                    if (!pending.TryGetValue(groupKey, out var cleared))
                        cleared = PendingOccurrence.From(item);
                    pending[groupKey] = cleared with { ClearedAt = item.Timestamp };
                    break;

                case AlarmEventType.Acknowledged:
                    if (!pending.TryGetValue(groupKey, out var acknowledged))
                    {
                        acknowledged = PendingOccurrence.From(item);
                        pending[groupKey] = acknowledged;
                    }
                    pending[groupKey] = acknowledged with
                    {
                        AcknowledgedAt = acknowledged.AcknowledgedAt ?? item.Timestamp,
                        AcknowledgedBy = acknowledged.AcknowledgedBy ?? item.AcknowledgedBy
                    };
                    break;

                case AlarmEventType.Active:
                    pending.TryGetValue(groupKey, out var newer);
                    result.Add(new AlarmOccurrenceRecord(
                        AlarmKey: item.AlarmKey,
                        DefinitionId: item.DefinitionId,
                        TagId: item.TagId,
                        DeviceId: item.DeviceId,
                        IsSystem: item.IsSystem,
                        SourceName: item.SourceName,
                        Text: item.Text,
                        Priority: item.Priority,
                        ActiveAt: item.Timestamp,
                        AcknowledgedAt: newer?.AcknowledgedAt,
                        ClearedAt: newer?.ClearedAt,
                        State: newer?.ClearedAt is not null
                            ? "Cleared"
                            : newer?.AcknowledgedAt is not null
                                ? "Acknowledged"
                                : "Active",
                        BatchId: forcedBatchId ?? item.BatchId,
                        OccurrenceId: item.OccurrenceId,
                        AcknowledgedBy: newer?.AcknowledgedBy));

                    pending.Remove(groupKey);
                    if (result.Count >= limit)
                        return result;
                    break;
            }
        }

        return result;
    }

    private static async Task<IReadOnlyList<AlarmEventRecord>> ReadEventsAsync(
        SqliteCommand command,
        CancellationToken cancellationToken)
    {
        var result = new List<AlarmEventRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(ReadEvent(reader));
        return result;
    }

    private static AlarmEventRecord ReadEvent(SqliteDataReader reader)
    {
        Enum.TryParse<AlarmPriority>(reader.GetString(8), true, out var priority);
        Enum.TryParse<AlarmEventType>(reader.GetString(9), true, out var eventType);

        return new AlarmEventRecord(
            Id: reader.GetInt64(0),
            AlarmKey: reader.GetString(1),
            DefinitionId: reader.IsDBNull(2) ? null : Guid.Parse(reader.GetString(2)),
            TagId: reader.IsDBNull(3) ? null : Guid.Parse(reader.GetString(3)),
            DeviceId: reader.IsDBNull(4) ? null : Guid.Parse(reader.GetString(4)),
            IsSystem: reader.GetInt32(5) != 0,
            SourceName: reader.GetString(6),
            Text: reader.GetString(7),
            Priority: priority,
            EventType: eventType,
            Timestamp: DateTimeOffset.Parse(reader.GetString(10)),
            BatchId: reader.IsDBNull(11) ? null : Guid.Parse(reader.GetString(11)),
            OccurrenceId: reader.IsDBNull(12) ? Guid.Empty : Guid.Parse(reader.GetString(12)),
            AcknowledgedBy: reader.IsDBNull(13) ? null : reader.GetString(13));
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=ON;";
        await command.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    private sealed record PendingOccurrence(
        DateTimeOffset? AcknowledgedAt,
        DateTimeOffset? ClearedAt,
        string? AcknowledgedBy = null)
    {
        public static PendingOccurrence From(AlarmEventRecord _) => new(null, null);
    }
}
