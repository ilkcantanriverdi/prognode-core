using Microsoft.Data.Sqlite;
using System.Globalization;

namespace Prognode.Web;

public sealed record MobileAckAuditEntry(Guid ServerId, Guid OccurrenceId, Guid? DeviceId, string Principal,
    string Result, DateTimeOffset TimestampUtc, string? ActorDisplayName = null);

/// <summary>Immutable local attribution. Never treat transport acceptance as acknowledgement.</summary>
public sealed class MobileAckAuditStore
{
    private readonly string _connectionString;
    public MobileAckAuditStore(string databasePath)
    {
        _connectionString=new SqliteConnectionStringBuilder { DataSource=databasePath,
            Mode=SqliteOpenMode.ReadWriteCreate,Pooling=true,DefaultTimeout=10 }.ToString();
        using var db=new SqliteConnection(_connectionString); db.Open();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            CREATE TABLE IF NOT EXISTS mobile_ack_audit (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                server_id TEXT NOT NULL, occurrence_id TEXT NOT NULL, device_id TEXT,
                principal TEXT NOT NULL, result TEXT NOT NULL, timestamp_utc TEXT NOT NULL,
                actor_display_name TEXT
            );
            CREATE INDEX IF NOT EXISTS idx_mobile_ack_audit_occurrence
                ON mobile_ack_audit(server_id, occurrence_id, result, id);
            """;
        cmd.ExecuteNonQuery();

        // Existing Core databases predate display-name attribution.
        using var columns=db.CreateCommand();
        columns.CommandText="PRAGMA table_info(mobile_ack_audit);";
        using var reader=columns.ExecuteReader();
        var hasActorDisplayName=false;
        while(reader.Read())
            if(string.Equals(reader.GetString(1),"actor_display_name",StringComparison.OrdinalIgnoreCase))
                hasActorDisplayName=true;
        reader.Close();
        if(!hasActorDisplayName)
        {
            using var alter=db.CreateCommand();
            alter.CommandText="ALTER TABLE mobile_ack_audit ADD COLUMN actor_display_name TEXT;";
            alter.ExecuteNonQuery();
        }
    }
    public void Record(Guid serverId,Guid occurrenceId,Guid? deviceId,string principal,string result,
        string? actorDisplayName = null)
    {
        using var db=new SqliteConnection(_connectionString); db.Open();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            INSERT INTO mobile_ack_audit(server_id,occurrence_id,device_id,principal,result,timestamp_utc,actor_display_name)
            VALUES($server,$occurrence,$device,$principal,$result,$now,$actor);
            """;
        cmd.Parameters.AddWithValue("$server",serverId.ToString("D"));
        cmd.Parameters.AddWithValue("$occurrence",occurrenceId.ToString("D"));
        cmd.Parameters.AddWithValue("$device",deviceId?.ToString("D") ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("$principal",principal);
        cmd.Parameters.AddWithValue("$result",result);
        cmd.Parameters.AddWithValue("$now",DateTimeOffset.UtcNow.ToString("O"));
        cmd.Parameters.AddWithValue("$actor",string.IsNullOrWhiteSpace(actorDisplayName)
            ? (object)DBNull.Value : actorDisplayName.Trim());
        cmd.ExecuteNonQuery();
    }
    public IReadOnlyDictionary<Guid, MobileAckAuditEntry> GetAcknowledgements(Guid serverId, IEnumerable<Guid> occurrenceIds)
    {
        var ids=occurrenceIds.Where(x=>x!=Guid.Empty).Distinct().Take(500).ToArray();
        if(ids.Length==0) return new Dictionary<Guid,MobileAckAuditEntry>();
        using var db=new SqliteConnection(_connectionString); db.Open();
        using var cmd=db.CreateCommand();
        var names=ids.Select((_,i)=>$"$occ{i}").ToArray();
        cmd.CommandText=$"""
            SELECT server_id,occurrence_id,device_id,principal,result,timestamp_utc,actor_display_name
            FROM mobile_ack_audit
            WHERE server_id=$server AND occurrence_id IN ({string.Join(",",names)})
                AND UPPER(result)='ACKNOWLEDGED'
            ORDER BY id ASC;
            """;
        cmd.Parameters.AddWithValue("$server",serverId.ToString("D"));
        for(var i=0;i<ids.Length;i++) cmd.Parameters.AddWithValue(names[i],ids[i].ToString("D"));
        using var r=cmd.ExecuteReader();
        var items=new Dictionary<Guid,MobileAckAuditEntry>();
        while(r.Read())
        {
            var occurrenceId=Guid.Parse(r.GetString(1));
            if(items.ContainsKey(occurrenceId)) continue;
            items[occurrenceId]=new MobileAckAuditEntry(Guid.Parse(r.GetString(0)),occurrenceId,
                r.IsDBNull(2)?null:Guid.Parse(r.GetString(2)),r.GetString(3),r.GetString(4),
                DateTimeOffset.Parse(r.GetString(5),CultureInfo.InvariantCulture),
                r.IsDBNull(6)?null:r.GetString(6));
        }
        return items;
    }

    public object GetRecent(int limit=100)
    {
        using var db=new SqliteConnection(_connectionString); db.Open();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            SELECT server_id,occurrence_id,device_id,principal,result,timestamp_utc,actor_display_name
            FROM mobile_ack_audit ORDER BY id DESC LIMIT $limit;
            """;
        cmd.Parameters.AddWithValue("$limit",Math.Clamp(limit,1,500));
        using var r=cmd.ExecuteReader();
        var items=new List<object>();
        while(r.Read())items.Add(new {serverId=r.GetString(0),occurrenceId=r.GetString(1),
            deviceId=r.IsDBNull(2)?null:r.GetString(2),principal=r.GetString(3),
            result=r.GetString(4),timestampUtc=r.GetString(5),
            actorDisplayName=r.IsDBNull(6)?null:r.GetString(6)});
        return new {items};
    }
}
