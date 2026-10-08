using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Prognode.Contracts.Alarms;
using Prognode.Contracts.Tags;

namespace Prognode.Alarm;

public sealed class AlarmRuntimeStore
{
    private readonly ConcurrentDictionary<string, AlarmRuntimeSnapshot> _active = new();
    private readonly object _occurrenceGate = new();
    private readonly ConcurrentDictionary<string, AlarmRuntimeSnapshot> _pendingAcknowledgement = new();
    private readonly string _connectionString;
    private long _definitionVersion;

    /// <summary>Bumped whenever alarm definitions change, so cached engine definitions are refreshed.</summary>
    public long DefinitionVersion => Interlocked.Read(ref _definitionVersion);

    public void MarkDefinitionsChanged() => Interlocked.Increment(ref _definitionVersion);

    public AlarmRuntimeStore(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder {
            DataSource=databasePath,Mode=SqliteOpenMode.ReadWriteCreate,
            Pooling=true,Cache=SqliteCacheMode.Shared,DefaultTimeout=10
        }.ToString();
        using var db = new SqliteConnection(_connectionString);
        db.Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS alarm_runtime_snapshots (
                alarm_key TEXT PRIMARY KEY, lifecycle TEXT NOT NULL, snapshot_json TEXT NOT NULL
            );
            """;
        cmd.ExecuteNonQuery();
        cmd.CommandText="SELECT lifecycle,snapshot_json FROM alarm_runtime_snapshots;";
        using var reader=cmd.ExecuteReader();
        while(reader.Read())
        {
            try
            {
                var value=JsonSerializer.Deserialize<AlarmRuntimeSnapshot>(reader.GetString(1));
                if(value is null || value.OccurrenceId == Guid.Empty) continue;
                if(reader.GetString(0)=="ACTIVE") _active[value.AlarmKey]=value;
                else if(reader.GetString(0)=="PENDING") _pendingAcknowledgement[value.AlarmKey]=value;
            }
            catch (JsonException) { /* Damaged row is not trusted as an ACK target. */ }
        }
    }

    private void Persist(AlarmRuntimeSnapshot value,string lifecycle)
    {
        using var db=new SqliteConnection(_connectionString);
        db.Open();
        using var cmd=db.CreateCommand();
        cmd.CommandText="""
            INSERT INTO alarm_runtime_snapshots(alarm_key,lifecycle,snapshot_json)
            VALUES($key,$cycle,$json)
            ON CONFLICT(alarm_key) DO UPDATE SET lifecycle=excluded.lifecycle,
              snapshot_json=excluded.snapshot_json;
            """;
        cmd.Parameters.AddWithValue("$key",value.AlarmKey);
        cmd.Parameters.AddWithValue("$cycle",lifecycle);
        cmd.Parameters.AddWithValue("$json",JsonSerializer.Serialize(value));
        cmd.ExecuteNonQuery();
    }

    private void DeletePersisted(string key)
    {
        using var db=new SqliteConnection(_connectionString);
        db.Open();
        using var cmd=db.CreateCommand();
        cmd.CommandText="DELETE FROM alarm_runtime_snapshots WHERE alarm_key=$key";
        cmd.Parameters.AddWithValue("$key",key);
        cmd.ExecuteNonQuery();
    }

    public void SetActive(AlarmRuntimeSnapshot snapshot)
    {
        lock (_occurrenceGate)
        {
            _pendingAcknowledgement.TryRemove(snapshot.AlarmKey, out _);
            _active[snapshot.AlarmKey] = snapshot;
            Persist(snapshot,"ACTIVE");
        }
    }

    public AlarmRuntimeSnapshot? Get(string alarmKey) =>
        _active.TryGetValue(alarmKey, out var value)
            ? value
            : null;

    public AlarmRuntimeSnapshot? GetPendingAcknowledgement(string alarmKey) =>
        _pendingAcknowledgement.TryGetValue(alarmKey, out var value)
            ? value
            : null;

    /// <summary>
    /// Applies an edited definition to its runtime occurrence. Returns the cleared occurrence that
    /// was still awaiting ACK when acknowledgement is switched off, so the caller can record that
    /// the requirement was waived instead of the occurrence silently disappearing.
    /// </summary>
    public AlarmRuntimeSnapshot? ApplyDefinitionUpdate(AlarmDefinition definition, TagDefinition tag)
    {
        var alarmKey = AlarmService.KeyFor(definition.Id);
        MarkDefinitionsChanged();
        lock (_occurrenceGate)
        {
            if (_active.TryGetValue(alarmKey, out var active))
            {
                var updated = WithDefinition(active, definition, tag);
                _active[alarmKey] = updated;
                Persist(updated, "ACTIVE");
                return null;
            }

            if (!_pendingAcknowledgement.TryGetValue(alarmKey, out var pending))
                return null;

            if (!definition.RequiresAcknowledgement)
            {
                _pendingAcknowledgement.TryRemove(alarmKey, out _);
                DeletePersisted(alarmKey);
                return pending;
            }

            var updatedPending = WithDefinition(pending, definition, tag);
            _pendingAcknowledgement[alarmKey] = updatedPending;
            Persist(updatedPending, "PENDING");
            return null;
        }
    }

    /// <summary>
    /// Brings the ACK requirement of an active occurrence in line with its definition. Only that
    /// field changes, under the occurrence lock, so a concurrent ACK is never overwritten.
    /// </summary>
    public void SyncRequiresAcknowledgement(string alarmKey, bool requiresAcknowledgement)
    {
        lock (_occurrenceGate)
        {
            if (!_active.TryGetValue(alarmKey, out var current) ||
                current.RequiresAcknowledgement == requiresAcknowledgement)
                return;
            var updated = current with { RequiresAcknowledgement = requiresAcknowledgement };
            _active[alarmKey] = updated;
            Persist(updated, "ACTIVE");
        }
    }

    private static AlarmRuntimeSnapshot WithDefinition(
        AlarmRuntimeSnapshot snapshot,
        AlarmDefinition definition,
        TagDefinition tag) => snapshot with
    {
        DefinitionId = definition.Id,
        TagId = tag.Id,
        DeviceId = tag.DeviceId,
        SourceName = tag.Name,
        Text = definition.Text,
        Priority = definition.Priority,
        RequiresAcknowledgement = definition.RequiresAcknowledgement
    };

    public IReadOnlyList<AlarmRuntimeSnapshot> GetAllPendingAcknowledgement() =>
        _pendingAcknowledgement.Values.ToArray();

    public IReadOnlyList<AlarmRuntimeSnapshot> GetAllActive() =>
        _active.Values
            .OrderByDescending(x => x.Priority)
            .ThenBy(x => x.ActiveSince)
            .ToArray();

    public AlarmRuntimeSnapshot? Acknowledge(string alarmKey, string? acknowledgedBy = null)
    {
        lock (_occurrenceGate) return AcknowledgeLegacyCore(alarmKey, acknowledgedBy);
    }

    private AlarmRuntimeSnapshot? AcknowledgeLegacyCore(string alarmKey, string? acknowledgedBy)
    {
        while (_active.TryGetValue(alarmKey, out var current))
        {
            if (current.RequiresAcknowledgement == false)
                return null;
            if (current.State == AlarmRuntimeState.Acknowledged)
                return current;

            var now = DateTimeOffset.UtcNow;
            var updated = current with
            {
                State = AlarmRuntimeState.Acknowledged,
                LastChangedAt = now,
                AcknowledgedAt = now,
                AcknowledgedBy = acknowledgedBy
            };

            if (_active.TryUpdate(alarmKey, updated, current))
            {
                Persist(updated,"ACTIVE");
                return updated;
            }
        }

        if (_pendingAcknowledgement.TryRemove(alarmKey, out var pending))
        {
            DeletePersisted(alarmKey);
            var now = DateTimeOffset.UtcNow;
            return pending with
            {
                State = AlarmRuntimeState.Acknowledged,
                LastChangedAt = now,
                AcknowledgedAt = now,
                AcknowledgedBy = acknowledgedBy
            };
        }

        return null;
    }

    public AlarmRuntimeSnapshot? Clear(string alarmKey, bool keepPendingAcknowledgement)
    {
        lock (_occurrenceGate) return ClearCore(alarmKey,keepPendingAcknowledgement);
    }

    private AlarmRuntimeSnapshot? ClearCore(string alarmKey, bool keepPendingAcknowledgement)
    {
        if (!_active.TryRemove(alarmKey, out var value))
            return null;

        if (keepPendingAcknowledgement && value.State != AlarmRuntimeState.Acknowledged)
            _pendingAcknowledgement[alarmKey] = value;
        else
            _pendingAcknowledgement.TryRemove(alarmKey, out _);
        if(keepPendingAcknowledgement && value.State!=AlarmRuntimeState.Acknowledged)
            Persist(value,"PENDING");
        else DeletePersisted(alarmKey);
        return value;
    }

    public void RemovePendingAcknowledgement(string alarmKey)
    { lock (_occurrenceGate) { _pendingAcknowledgement.TryRemove(alarmKey, out _);
        DeletePersisted(alarmKey); } }

    public AlarmRuntimeSnapshot? Remove(string alarmKey)
    {
        lock (_occurrenceGate) return RemoveCore(alarmKey);
    }

    private AlarmRuntimeSnapshot? RemoveCore(string alarmKey)
    {
        _pendingAcknowledgement.TryRemove(alarmKey, out _);
        DeletePersisted(alarmKey);
        return _active.TryRemove(alarmKey, out var value)
            ? value
            : null;
    }
    /// <summary>Atomic, occurrence-specific ACK. NEVER resolves a newer cycle by alarmKey alone.</summary>
    public AlarmRuntimeSnapshot? AcknowledgeOccurrence(Guid occurrenceId, out bool alreadyAcknowledged) =>
        AcknowledgeOccurrence(occurrenceId, null, out alreadyAcknowledged);

    public AlarmRuntimeSnapshot? AcknowledgeOccurrence(Guid occurrenceId, string? acknowledgedBy, out bool alreadyAcknowledged)
    {
        alreadyAcknowledged = false;
        if (occurrenceId == Guid.Empty) return null;
        lock (_occurrenceGate)
        {
            foreach (var pair in _active)
            {
                if (pair.Value.OccurrenceId != occurrenceId) continue;
                var current=pair.Value;
                if (current.RequiresAcknowledgement == false)
                    return null;
                if (current.State == AlarmRuntimeState.Acknowledged)
                {
                    alreadyAcknowledged = true;
                    return current;
                }
                var now=DateTimeOffset.UtcNow;
                var updated=current with
                { State=AlarmRuntimeState.Acknowledged,LastChangedAt=now,
                  AcknowledgedAt=now,AcknowledgedBy=acknowledgedBy };
                if (_active.TryUpdate(pair.Key,updated,current))
                {
                    Persist(updated,"ACTIVE");
                    return updated;
                }
                return null;
            }
            foreach (var pair in _pendingAcknowledgement)
            {
                if (pair.Value.OccurrenceId != occurrenceId) continue;
                if (_pendingAcknowledgement.TryRemove(pair.Key,out var pending))
                {
                    DeletePersisted(pair.Key);
                    var now=DateTimeOffset.UtcNow;
                    return pending with { State=AlarmRuntimeState.Acknowledged,
                        LastChangedAt=now, AcknowledgedAt=now, AcknowledgedBy=acknowledgedBy };
                }
            }
            return null;
        }
    }

    public bool IsKnownOccurrence(Guid occurrenceId) =>
        _active.Values.Any(x=>x.OccurrenceId==occurrenceId) ||
        _pendingAcknowledgement.Values.Any(x=>x.OccurrenceId==occurrenceId);

}
