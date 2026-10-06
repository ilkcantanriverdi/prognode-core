using System.Text.Json;
using Microsoft.Data.Sqlite;
using Prognode.Contracts.Notifications;

namespace Prognode.RemoteAccess;

public sealed class RemoteNotificationOutboxItem
{
    public NotificationEvent Event { get; set; } = new(0,"Information",string.Empty,string.Empty,DateTimeOffset.UtcNow);
    public int Attempts { get; set; }
    public DateTimeOffset NextAttemptAtUtc { get; set; }
}

internal sealed class LegacyRemoteOutboxDocument
{
    public List<RemoteNotificationOutboxItem> Items { get; set; } = [];
}

/// <summary>
/// Reads the very same SQLite outbox written atomically by NotificationEventStore.
/// Relay acceptance is tracked separately from actual device receipt.
/// </summary>
public sealed class RemoteNotificationOutboxStore
{
    private readonly string _connectionString;
    private readonly string _legacyPath;
    private readonly object _gate = new();

    public RemoteNotificationOutboxStore(string databasePath, string dataRoot)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource=databasePath, Mode=SqliteOpenMode.ReadWriteCreate,
            Cache=SqliteCacheMode.Shared, Pooling=true, DefaultTimeout=10
        }.ToString();
        _legacyPath = Path.Combine(dataRoot,"remote-access","notification-outbox.json");
        MigrateOldJson();
    }

    public IReadOnlyList<RemoteNotificationOutboxItem> Due(int max=25)
    {
        lock (_gate)
        {
            using var c = Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = """
              SELECT n.id,n.severity,n.title,n.message,n.created_at,n.alarm_key,
                     n.requires_ack,n.repeat_sequence,n.occurrence_id,n.event_type,
                     n.source_name,n.active_at,n.cleared_at,o.attempts,o.next_attempt_at
              FROM notification_outbox o JOIN notification_events n ON n.id=o.event_id
              WHERE o.state='QUEUED' AND o.next_attempt_at <= $now
              ORDER BY n.id LIMIT $max;
              """;
            cmd.Parameters.AddWithValue("$now",DateTimeOffset.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("$max",Math.Clamp(max,1,100));
            var list = new List<RemoteNotificationOutboxItem>();
            using var r = cmd.ExecuteReader();
            while (r.Read())
                list.Add(new RemoteNotificationOutboxItem
                {
                    Event = new NotificationEvent(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetString(3),
                        DateTimeOffset.Parse(r.GetString(4)),r.IsDBNull(5)?null:r.GetString(5),
                        r.GetInt32(6)!=0,r.GetInt32(7),r.IsDBNull(8)?null:Guid.Parse(r.GetString(8)),
                        r.GetString(9),r.IsDBNull(10)?null:r.GetString(10),
                        r.IsDBNull(11)?null:DateTimeOffset.Parse(r.GetString(11)),
                        r.IsDBNull(12)?null:DateTimeOffset.Parse(r.GetString(12))),
                    Attempts=r.GetInt32(13),NextAttemptAtUtc=DateTimeOffset.Parse(r.GetString(14))
                });
            return list;
        }
    }

    public void MarkDelivered(long eventId)
    {
        lock (_gate)
        {
            using var c = Open(); using var cmd=c.CreateCommand();
            cmd.CommandText = "UPDATE notification_outbox SET state='RELAY_ACCEPTED',relay_accepted_at=$now WHERE event_id=$id AND state='QUEUED';";
            cmd.Parameters.AddWithValue("$now",DateTimeOffset.UtcNow.ToString("O"));
            cmd.Parameters.AddWithValue("$id",eventId);
            cmd.ExecuteNonQuery();
        }
    }

    public int SuppressPending()
    {
        lock (_gate)
        {
            using var c=Open();using var cmd=c.CreateCommand();
            cmd.CommandText="UPDATE notification_outbox SET state='SUPPRESSED',last_error='DELIVERY_DISABLED' WHERE state='QUEUED';";
            return cmd.ExecuteNonQuery();
        }
    }

    public void MarkFailed(long eventId, string? reason=null)
    {
        lock (_gate)
        {
            using var c=Open();using var cmd=c.CreateCommand();
            cmd.CommandText = "SELECT attempts FROM notification_outbox WHERE event_id=$id AND state='QUEUED';";
            cmd.Parameters.AddWithValue("$id",eventId);
            if (cmd.ExecuteScalar() is not long previous) return;
            var attempts=(int)previous+1;
            var seconds=Math.Min(3600,5*(1<<Math.Min(attempts,9)));
            cmd.CommandText = """
              UPDATE notification_outbox SET attempts=$attempts,next_attempt_at=$next,last_error=$error
              WHERE event_id=$id AND state='QUEUED';
              """;
            cmd.Parameters.AddWithValue("$attempts",attempts);
            cmd.Parameters.AddWithValue("$next",DateTimeOffset.UtcNow.AddSeconds(seconds).ToString("O"));
            cmd.Parameters.AddWithValue("$error",(object?)reason ?? DBNull.Value);
            cmd.ExecuteNonQuery();
        }
    }

    private SqliteConnection Open()
    {
        var c=new SqliteConnection(_connectionString);c.Open();
        using var cmd=c.CreateCommand();cmd.CommandText="PRAGMA foreign_keys=ON; PRAGMA busy_timeout=10000;";
        cmd.ExecuteNonQuery();return c;
    }
    private void MigrateOldJson()
    {
        if (!File.Exists(_legacyPath)) return;
        try
        {
            var old=JsonSerializer.Deserialize<LegacyRemoteOutboxDocument>(File.ReadAllText(_legacyPath),
                new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (old is null) return;
            lock(_gate)
            {
                using var c=Open();using var tx=c.BeginTransaction();
                foreach(var item in old.Items.Where(x=>x.Event.Id>0))
                {
                    // Allocate NEW journal IDs for old JSON events. Legacy numeric IDs can
                    // collide with real SQLite IDs; never attach a legacy outbox row to an
                    // unrelated notification event after a collision.
                    using var cmd=c.CreateCommand();cmd.Transaction=tx;
                    cmd.CommandText = """
                      INSERT INTO notification_events
                      (severity,title,message,created_at,alarm_key,requires_ack,repeat_sequence,
                       occurrence_id,event_type,source_name,active_at,cleared_at)
                      VALUES($severity,$title,$message,$created,$key,$ack,$seq,$occ,$type,$source,$active,$cleared);
                      INSERT INTO notification_outbox(event_id,state,attempts,next_attempt_at)
                      VALUES(last_insert_rowid(),'QUEUED',$attempts,$next);
                      """;
                    var e=item.Event;
                    cmd.Parameters.AddWithValue("$severity",e.Severity);
                    cmd.Parameters.AddWithValue("$title",e.Title);
                    cmd.Parameters.AddWithValue("$message",e.Message);
                    cmd.Parameters.AddWithValue("$created",e.Timestamp.ToString("O"));
                    cmd.Parameters.AddWithValue("$key",(object?)e.AlarmKey ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("$ack",e.RequiresAcknowledgement?1:0);
                    cmd.Parameters.AddWithValue("$seq",e.RepeatSequence);
                    cmd.Parameters.AddWithValue("$occ",e.OccurrenceId.HasValue?e.OccurrenceId.Value.ToString("D"):DBNull.Value);
                    cmd.Parameters.AddWithValue("$type",e.EventType);
                    cmd.Parameters.AddWithValue("$source",(object?)e.SourceName??DBNull.Value);
                    cmd.Parameters.AddWithValue("$active",e.ActiveAtUtc.HasValue?e.ActiveAtUtc.Value.ToString("O"):DBNull.Value);
                    cmd.Parameters.AddWithValue("$cleared",e.ClearedAtUtc.HasValue?e.ClearedAtUtc.Value.ToString("O"):DBNull.Value);
                    cmd.Parameters.AddWithValue("$attempts",item.Attempts);
                    cmd.Parameters.AddWithValue("$next",item.NextAttemptAtUtc.ToString("O"));
                    cmd.ExecuteNonQuery();
                }
                tx.Commit();
            }
            File.Move(_legacyPath,_legacyPath+".migrated",overwrite:true);
        }
        catch(Exception ex) { Console.Error.WriteLine("PROGNODE legacy outbox migration needs attention: "+ex.Message); }
    }
}
