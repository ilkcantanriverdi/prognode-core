using Microsoft.Data.Sqlite;
using Prognode.Contracts.Notifications;
using Prognode.Contracts.Alarms;

namespace Prognode.Notifications;

/// <summary>
/// SQLite-backed event journal. Notification row + eligible remote outbox row are
/// committed atomically; all mobile cursors survive a Core restart.
/// </summary>
public sealed class NotificationEventStore
{
    private readonly string _connectionString;
    private readonly object _gate = new();
    private Func<bool>? _remoteEligible;
    private long _fallbackId = -1;
    public event Action<NotificationEvent>? Published;
    public string? LastStorageError { get; private set; }
    public bool StorageHealthy => LastStorageError is null;

    public NotificationEventStore(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath, Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared, Pooling = true, DefaultTimeout = 10
        }.ToString();
    }

    // Set by Host before hosted services start. No remote licence means never queue
    // an event for later transmission when a licence is purchased in the future.
    public void SetRemoteEligibility(Func<bool> predicate)
    {
        lock (_gate) _remoteEligible = predicate;
    }

    public NotificationEvent Publish(
        string severity, string title, string message,
        string? alarmKey = null, bool requiresAcknowledgement = false,
        int repeatSequence = 0, Guid? occurrenceId = null,
        string eventType = "INFO", string? sourceName = null,
        DateTimeOffset? activeAtUtc = null, DateTimeOffset? clearedAtUtc = null,
        AlarmEventRecord? alarmEvent = null)
    {
        NotificationEvent item;
        var now = DateTimeOffset.UtcNow;
        lock (_gate)
        {
            try
            {
                using var connection = Open();
                using var transaction = connection.BeginTransaction();
                if(alarmEvent is not null)
                {
                    // One transaction across alarm event, notification and relay outbox.
                    using var alarmInsert=connection.CreateCommand();
                    alarmInsert.Transaction=transaction;
                    alarmInsert.CommandText = """
                      INSERT INTO alarm_events
                      (alarm_key,definition_id,tag_id,device_id,is_system,
                       source_name,alarm_text,priority,event_type,event_time,batch_id,occurrence_id,acknowledged_by)
                      VALUES($key,$def,$tag,$device,$system,$source,$text,$priority,$type,$at,$batch,$occ,$ackBy);
                      """;
                    alarmInsert.Parameters.AddWithValue("$key",alarmEvent.AlarmKey);
                    alarmInsert.Parameters.AddWithValue("$def",alarmEvent.DefinitionId.HasValue?alarmEvent.DefinitionId.Value.ToString("D"):DBNull.Value);
                    alarmInsert.Parameters.AddWithValue("$tag",alarmEvent.TagId.HasValue?alarmEvent.TagId.Value.ToString("D"):DBNull.Value);
                    alarmInsert.Parameters.AddWithValue("$device",alarmEvent.DeviceId.HasValue?alarmEvent.DeviceId.Value.ToString("D"):DBNull.Value);
                    alarmInsert.Parameters.AddWithValue("$system",alarmEvent.IsSystem?1:0);
                    alarmInsert.Parameters.AddWithValue("$source",alarmEvent.SourceName);
                    alarmInsert.Parameters.AddWithValue("$text",alarmEvent.Text);
                    alarmInsert.Parameters.AddWithValue("$priority",alarmEvent.Priority.ToString());
                    alarmInsert.Parameters.AddWithValue("$type",alarmEvent.EventType.ToString());
                    alarmInsert.Parameters.AddWithValue("$at",alarmEvent.Timestamp.ToString("O"));
                    alarmInsert.Parameters.AddWithValue("$batch",alarmEvent.BatchId.HasValue?alarmEvent.BatchId.Value.ToString("D"):DBNull.Value);
                    alarmInsert.Parameters.AddWithValue("$occ",alarmEvent.OccurrenceId==Guid.Empty?DBNull.Value:alarmEvent.OccurrenceId.ToString("D"));
                    alarmInsert.Parameters.AddWithValue("$ackBy",(object?)alarmEvent.AcknowledgedBy??DBNull.Value);
                    alarmInsert.ExecuteNonQuery();
                }
                using var insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                  INSERT INTO notification_events
                    (severity,title,message,created_at,alarm_key,requires_ack,
                     repeat_sequence,occurrence_id,event_type,source_name,active_at,cleared_at)
                  VALUES ($severity,$title,$message,$created,$key,$ack,$seq,$occ,$type,$source,$active,$cleared)
                  RETURNING id;
                  """;
                insert.Parameters.AddWithValue("$severity", severity);
                insert.Parameters.AddWithValue("$title", title);
                insert.Parameters.AddWithValue("$message", message);
                insert.Parameters.AddWithValue("$created", now.ToString("O"));
                insert.Parameters.AddWithValue("$key", (object?)alarmKey ?? DBNull.Value);
                insert.Parameters.AddWithValue("$ack", requiresAcknowledgement ? 1 : 0);
                insert.Parameters.AddWithValue("$seq", repeatSequence);
                insert.Parameters.AddWithValue("$occ", occurrenceId.HasValue ? occurrenceId.Value.ToString("D") : DBNull.Value);
                insert.Parameters.AddWithValue("$type", eventType);
                insert.Parameters.AddWithValue("$source", (object?)sourceName ?? DBNull.Value);
                insert.Parameters.AddWithValue("$active", activeAtUtc.HasValue ? activeAtUtc.Value.ToString("O") : DBNull.Value);
                insert.Parameters.AddWithValue("$cleared", clearedAtUtc.HasValue ? clearedAtUtc.Value.ToString("O") : DBNull.Value);
                var id = (long)(insert.ExecuteScalar() ?? throw new InvalidOperationException("Notification INSERT did not return an ID."));
                item = new NotificationEvent(id,severity,title,message,now,alarmKey,
                    requiresAcknowledgement,repeatSequence,occurrenceId,eventType,sourceName,activeAtUtc,clearedAtUtc);

                var shouldQueue = false;
                try { shouldQueue = _remoteEligible?.Invoke() == true; }
                catch { /* Cloud entitlement must not interrupt the local alarm engine. */ }
                if (shouldQueue)
                {
                    using var outbox = connection.CreateCommand();
                    outbox.Transaction = transaction;
                    outbox.CommandText = """
                      INSERT INTO notification_outbox(event_id,state,attempts,next_attempt_at)
                      VALUES($id,'QUEUED',0,$now);
                      """;
                    outbox.Parameters.AddWithValue("$id", id);
                    outbox.Parameters.AddWithValue("$now", now.ToString("O"));
                    outbox.ExecuteNonQuery();
                }
                transaction.Commit();
                LastStorageError = null;
            }
            catch (Exception ex) when (ex is SqliteException or IOException)
            {
                // Never invent a persisted success or block process alarms when storage
                // fails. Ephemeral negative IDs are NOT offered as replayable events.
                LastStorageError = ex.Message;
                if(alarmEvent is not null) throw; // Never silently lose a required alarm event.
                Console.Error.WriteLine("PROGNODE NOTIFICATION STORAGE ERROR: " + ex.Message);
                item = new NotificationEvent(_fallbackId--, severity,title,message,now,
                    alarmKey,requiresAcknowledgement,repeatSequence,occurrenceId,
                    eventType,sourceName,activeAtUtc,clearedAtUtc);
            }
        }
        try { Published?.Invoke(item); }
        catch { /* Windows Agent observers cannot break the alarm engine. */ }
        return item;
    }

    public IReadOnlyList<NotificationEvent> GetAfter(long afterId, int limit = 1000)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT " + Columns + " FROM notification_events WHERE id > $after ORDER BY id LIMIT $limit;";
        cmd.Parameters.AddWithValue("$after", Math.Max(0,afterId));
        cmd.Parameters.AddWithValue("$limit",Math.Clamp(limit,1,1000));
        return Read(cmd);
    }

    public IReadOnlyList<NotificationEvent> GetRecent(int count = 20)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT " + Columns + " FROM notification_events ORDER BY id DESC LIMIT $limit;";
        cmd.Parameters.AddWithValue("$limit",Math.Clamp(count,1,200));
        return Read(cmd);
    }

    public bool KnowsOccurrence(Guid occurrenceId)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT EXISTS(SELECT 1 FROM notification_events WHERE occurrence_id=$id);";
        cmd.Parameters.AddWithValue("$id", occurrenceId.ToString("D"));
        return Convert.ToInt32(cmd.ExecuteScalar()) != 0;
    }

    public (int Sequence, DateTimeOffset? LastAtUtc) LastReminderCycle(Guid occurrenceId)
    {
        using var c=Open();
        using var cmd=c.CreateCommand();
        cmd.CommandText="""
            SELECT repeat_sequence,created_at FROM notification_events
            WHERE occurrence_id=$id AND event_type IN ('ACTIVE','REMINDER')
            ORDER BY id DESC LIMIT 1;
            """;
        cmd.Parameters.AddWithValue("$id",occurrenceId.ToString("D"));
        using var reader=cmd.ExecuteReader();
        if(!reader.Read()) return (0,null);
        var seq=reader.GetInt32(0);
        var timestamp=DateTimeOffset.TryParse(reader.GetString(1),out var parsed)
            ? parsed : (DateTimeOffset?)null;
        return (seq,timestamp);
    }

    public bool WasAcknowledgedWithoutNewActivation(Guid occurrenceId)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            SELECT EXISTS(
                SELECT 1 FROM notification_events oldAck
                WHERE oldAck.occurrence_id=$id AND oldAck.event_type='ACK'
                  AND NOT EXISTS (
                    SELECT 1 FROM notification_events newActive
                    WHERE newActive.alarm_key=oldAck.alarm_key
                      AND newActive.event_type='ACTIVE'
                      AND newActive.id>oldAck.id
                  )
            );
            """;
        cmd.Parameters.AddWithValue("$id", occurrenceId.ToString("D"));
        return Convert.ToInt32(cmd.ExecuteScalar()) != 0;
    }

    public bool TryRecordReceipt(long eventId, Guid clientId, string state)
    {
        if (state is not ("DEVICE_RECEIVED" or "USER_OPENED")) return false;
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
          INSERT INTO notification_receipts(event_id,client_id,state,received_at)
          SELECT id,$client,$state,$now FROM notification_events WHERE id=$id
          ON CONFLICT(event_id,client_id) DO UPDATE SET
          state=CASE WHEN notification_receipts.state='USER_OPENED' THEN 'USER_OPENED' ELSE excluded.state END,
          received_at=excluded.received_at;
          """;
        cmd.Parameters.AddWithValue("$id",eventId);
        cmd.Parameters.AddWithValue("$client",clientId.ToString("D"));
        cmd.Parameters.AddWithValue("$state",state);
        cmd.Parameters.AddWithValue("$now",DateTimeOffset.UtcNow.ToString("O"));
        return cmd.ExecuteNonQuery() > 0;
    }

    public object GetHealth()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM notification_outbox WHERE state='QUEUED';";
        var pending = Convert.ToInt64(cmd.ExecuteScalar());
        cmd.CommandText = "SELECT COUNT(*) FROM notification_events;";
        var total = Convert.ToInt64(cmd.ExecuteScalar());
        return new { storageHealthy=StorageHealthy, lastStorageError=LastStorageError,
            persistedEvents=total, queuedRemote=pending };
    }

    public void PruneOld(int retentionDays = 90)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-Math.Clamp(retentionDays,30,3650));
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
          DELETE FROM notification_events WHERE created_at < $cutoff
          AND id NOT IN (SELECT event_id FROM notification_outbox WHERE state='QUEUED');
          """;
        cmd.Parameters.AddWithValue("$cutoff",cutoff.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(_connectionString);
        c.Open();
        using var pragma = c.CreateCommand();
        pragma.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=10000;";
        pragma.ExecuteNonQuery();
        return c;
    }
    private const string Columns = "id,severity,title,message,created_at,alarm_key,requires_ack,repeat_sequence,occurrence_id,event_type,source_name,active_at,cleared_at";
    private static IReadOnlyList<NotificationEvent> Read(SqliteCommand cmd)
    {
        using var r = cmd.ExecuteReader();
        var events = new List<NotificationEvent>();
        while (r.Read())
            events.Add(new NotificationEvent(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetString(3),
                DateTimeOffset.Parse(r.GetString(4)),r.IsDBNull(5)?null:r.GetString(5),r.GetInt32(6)!=0,
                r.GetInt32(7),r.IsDBNull(8)?null:Guid.Parse(r.GetString(8)),r.GetString(9),
                r.IsDBNull(10)?null:r.GetString(10),r.IsDBNull(11)?null:DateTimeOffset.Parse(r.GetString(11)),
                r.IsDBNull(12)?null:DateTimeOffset.Parse(r.GetString(12))));
        return events;
    }
}
