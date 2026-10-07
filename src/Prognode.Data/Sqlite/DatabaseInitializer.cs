using Microsoft.Data.Sqlite;

namespace Prognode.Data.Sqlite;

public sealed class DatabaseInitializer(SqliteDatabaseOptions options)
{
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        var dir = Path.GetDirectoryName(options.DatabasePath);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);

        await using var connection = new SqliteConnection(options.ConnectionString);
        await connection.OpenAsync(ct);
        await Exec(connection, "PRAGMA journal_mode=WAL;", ct);
        await Exec(connection, "PRAGMA synchronous=NORMAL;", ct);
        await Exec(connection, "PRAGMA foreign_keys=ON;", ct);

        await Exec(connection, """
        CREATE TABLE IF NOT EXISTS schema_version(version INTEGER NOT NULL);
        INSERT INTO schema_version(version) SELECT 9 WHERE NOT EXISTS(SELECT 1 FROM schema_version);

        CREATE TABLE IF NOT EXISTS devices(
          id TEXT PRIMARY KEY,name TEXT NOT NULL,protocol TEXT NOT NULL,status TEXT NOT NULL,
          host TEXT NULL,port INTEGER NULL,unit_id INTEGER NULL,poll_interval_ms INTEGER NOT NULL,
          created_at TEXT NOT NULL,updated_at TEXT NOT NULL);
        CREATE INDEX IF NOT EXISTS ix_devices_protocol ON devices(protocol);
        CREATE INDEX IF NOT EXISTS ix_devices_name ON devices(name);

        CREATE TABLE IF NOT EXISTS tags(
          id TEXT PRIMARY KEY,device_id TEXT NOT NULL,name TEXT NOT NULL,address TEXT NOT NULL,
          data_type TEXT NOT NULL,bit_index INTEGER NULL,byte_order TEXT NOT NULL DEFAULT 'ABCD',
          unit TEXT NOT NULL,scale REAL NOT NULL DEFAULT 1,offset REAL NOT NULL DEFAULT 0,
          decimal_places INTEGER NOT NULL DEFAULT 0,enabled INTEGER NOT NULL,created_at TEXT NOT NULL,
          updated_at TEXT NOT NULL,FOREIGN KEY(device_id) REFERENCES devices(id) ON DELETE CASCADE);
        CREATE INDEX IF NOT EXISTS ix_tags_device_id ON tags(device_id);
        CREATE INDEX IF NOT EXISTS ix_tags_name ON tags(name);

        CREATE TABLE IF NOT EXISTS batch_runs(
          id TEXT PRIMARY KEY,batch_no TEXT NOT NULL,recipe_name TEXT NULL,
          started_at TEXT NOT NULL,ended_at TEXT NULL,state TEXT NOT NULL,
          operator_name TEXT NULL,note TEXT NULL);
        CREATE INDEX IF NOT EXISTS ix_batch_runs_started_at ON batch_runs(started_at);
        CREATE INDEX IF NOT EXISTS ix_batch_runs_batch_no ON batch_runs(batch_no);
        CREATE UNIQUE INDEX IF NOT EXISTS ux_batch_runs_single_running ON batch_runs(state) WHERE state='Running';

        CREATE TABLE IF NOT EXISTS alarm_definitions(
          id TEXT PRIMARY KEY,tag_id TEXT NOT NULL,alarm_text TEXT NOT NULL,priority TEXT NOT NULL,
          bit_index INTEGER NULL,trigger_value INTEGER NOT NULL,
          condition TEXT NOT NULL DEFAULT 'DigitalEquals',threshold REAL NULL,deadband REAL NOT NULL DEFAULT 0,
          delay_on_ms INTEGER NOT NULL DEFAULT 0,delay_off_ms INTEGER NOT NULL DEFAULT 0,
          notification_enabled INTEGER NOT NULL DEFAULT 1,
          notify_on_active INTEGER NOT NULL DEFAULT 1,notify_on_cleared INTEGER NOT NULL DEFAULT 0,
          notification_mode TEXT NOT NULL DEFAULT 'NotifyOnce',repeat_interval_seconds INTEGER NOT NULL DEFAULT 60,
          continue_after_clear_until_ack INTEGER NOT NULL DEFAULT 0,requires_acknowledgement INTEGER NOT NULL DEFAULT 1,
          enabled INTEGER NOT NULL DEFAULT 1,
          created_at TEXT NOT NULL,updated_at TEXT NOT NULL,
          FOREIGN KEY(tag_id) REFERENCES tags(id) ON DELETE CASCADE);
        CREATE INDEX IF NOT EXISTS ix_alarm_definitions_tag_id ON alarm_definitions(tag_id);

        CREATE TABLE IF NOT EXISTS alarm_events(
          id INTEGER PRIMARY KEY AUTOINCREMENT,alarm_key TEXT NOT NULL,definition_id TEXT NULL,
          tag_id TEXT NULL,device_id TEXT NULL,is_system INTEGER NOT NULL,source_name TEXT NOT NULL,
          alarm_text TEXT NOT NULL,priority TEXT NOT NULL,event_type TEXT NOT NULL,event_time TEXT NOT NULL,
          batch_id TEXT NULL,occurrence_id TEXT NULL);
        CREATE INDEX IF NOT EXISTS ix_alarm_events_time ON alarm_events(event_time);
        CREATE INDEX IF NOT EXISTS ix_alarm_events_key ON alarm_events(alarm_key);

        CREATE TABLE IF NOT EXISTS batch_alarm_occurrences(
          batch_id TEXT NOT NULL,alarm_key TEXT NOT NULL,active_at TEXT NOT NULL,
          PRIMARY KEY(batch_id,alarm_key,active_at),
          FOREIGN KEY(batch_id) REFERENCES batch_runs(id) ON DELETE CASCADE);
        CREATE INDEX IF NOT EXISTS ix_batch_alarm_occurrences_alarm
          ON batch_alarm_occurrences(alarm_key,active_at);

        CREATE TABLE IF NOT EXISTS trend_definitions(
          id TEXT PRIMARY KEY,name TEXT NOT NULL,default_window_minutes INTEGER NOT NULL,
          sample_interval_seconds INTEGER NOT NULL DEFAULT 30,retention_days INTEGER NOT NULL DEFAULT 365,
          created_at TEXT NOT NULL,updated_at TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS trend_tags(
          trend_id TEXT NOT NULL,tag_id TEXT NOT NULL,sort_order INTEGER NOT NULL,
          color TEXT NOT NULL DEFAULT '#3ed7e8',PRIMARY KEY(trend_id,tag_id),
          FOREIGN KEY(trend_id) REFERENCES trend_definitions(id) ON DELETE CASCADE,
          FOREIGN KEY(tag_id) REFERENCES tags(id) ON DELETE CASCADE);

        CREATE TABLE IF NOT EXISTS historian_recording_configs(
          id TEXT PRIMARY KEY,tag_id TEXT NOT NULL UNIQUE,sample_interval_seconds INTEGER NOT NULL,
          retention_days INTEGER NOT NULL,enabled INTEGER NOT NULL DEFAULT 1,
          created_at TEXT NOT NULL,updated_at TEXT NOT NULL,
          FOREIGN KEY(tag_id) REFERENCES tags(id) ON DELETE CASCADE);

        CREATE TABLE IF NOT EXISTS historian_samples(
          tag_id TEXT NOT NULL,timestamp_unix_ms INTEGER NOT NULL,value REAL NULL,quality TEXT NOT NULL,
          batch_id TEXT NULL,
          PRIMARY KEY(tag_id,timestamp_unix_ms),FOREIGN KEY(tag_id) REFERENCES tags(id) ON DELETE CASCADE
        ) WITHOUT ROWID;
        CREATE INDEX IF NOT EXISTS ix_historian_samples_time ON historian_samples(timestamp_unix_ms);
        """, ct);

        // RC6.1 -> RC6.3 additive migration. Existing BOOL/WORD definitions remain DigitalEquals.
        await EnsureColumn(connection, "alarm_definitions", "notify_on_active", "INTEGER NOT NULL DEFAULT 1", ct);
        await EnsureColumn(connection, "alarm_definitions", "notify_on_cleared", "INTEGER NOT NULL DEFAULT 0", ct);
        await EnsureColumn(connection, "alarm_definitions", "notification_mode", "TEXT NOT NULL DEFAULT 'NotifyOnce'", ct);
        await EnsureColumn(connection, "alarm_definitions", "repeat_interval_seconds", "INTEGER NOT NULL DEFAULT 60", ct);
        await EnsureColumn(connection, "alarm_definitions", "continue_after_clear_until_ack", "INTEGER NOT NULL DEFAULT 0", ct);
        var hadAckRequirement = await ColumnExists(connection, "alarm_definitions", "requires_acknowledgement", ct);
        await EnsureColumn(connection, "alarm_definitions", "requires_acknowledgement", "INTEGER NOT NULL DEFAULT 1", ct);
        if (!hadAckRequirement)
            await Exec(connection, "UPDATE alarm_definitions SET requires_acknowledgement=CASE WHEN notification_mode='RepeatUntilAcknowledged' THEN 1 ELSE 0 END;", ct);
        await EnsureColumn(connection, "alarm_definitions", "condition", "TEXT NOT NULL DEFAULT 'DigitalEquals'", ct);
        await EnsureColumn(connection, "alarm_definitions", "threshold", "REAL NULL", ct);
        await EnsureColumn(connection, "alarm_definitions", "deadband", "REAL NOT NULL DEFAULT 0", ct);
        await EnsureColumn(connection, "alarm_events", "batch_id", "TEXT NULL", ct);
        await EnsureColumn(connection, "alarm_events", "occurrence_id", "TEXT NULL", ct);
        await EnsureColumn(connection, "alarm_events", "acknowledged_by", "TEXT NULL", ct);
        await EnsureColumn(connection, "trend_tags", "color", "TEXT NOT NULL DEFAULT '#3ed7e8'", ct);

        await MigrateHistorianSamplesNullable(connection, ct);
        await EnsureColumn(connection, "historian_samples", "batch_id", "TEXT NULL", ct);

        await Exec(connection, "CREATE INDEX IF NOT EXISTS ix_alarm_events_batch ON alarm_events(batch_id);", ct);
        await Exec(connection, "CREATE INDEX IF NOT EXISTS ix_alarm_events_active_id ON alarm_events(id DESC) WHERE event_type='Active';", ct);
        await Exec(connection, "CREATE INDEX IF NOT EXISTS ix_alarm_events_occurrence ON alarm_events(occurrence_id,id);", ct);
        await Exec(connection, "CREATE INDEX IF NOT EXISTS ix_historian_samples_batch ON historian_samples(batch_id);", ct);

        // RC6.4.4: Historian enrollment is manual only; saved Trends never create recordings.
        // Keep existing configurations/samples intact; customers can stop legacy entries in Historian.
        await MigrateLegacySignals(connection, ct);
        // RC6.4.6: notification event + remote outbox share the SAME SQLite transaction.
        await Exec(connection, """
          CREATE TABLE IF NOT EXISTS notification_events(
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            severity TEXT NOT NULL,title TEXT NOT NULL,message TEXT NOT NULL,
            created_at TEXT NOT NULL,alarm_key TEXT NULL,requires_ack INTEGER NOT NULL,
            repeat_sequence INTEGER NOT NULL,occurrence_id TEXT NULL,
            event_type TEXT NOT NULL DEFAULT 'INFO',source_name TEXT NULL,
            active_at TEXT NULL,cleared_at TEXT NULL);
          CREATE INDEX IF NOT EXISTS ix_notification_occurrence ON notification_events(occurrence_id,id);
          CREATE TABLE IF NOT EXISTS notification_outbox(
            event_id INTEGER PRIMARY KEY REFERENCES notification_events(id) ON DELETE CASCADE,
            state TEXT NOT NULL DEFAULT 'QUEUED', attempts INTEGER NOT NULL DEFAULT 0,
            next_attempt_at TEXT NOT NULL,relay_accepted_at TEXT NULL, last_error TEXT NULL);
          CREATE INDEX IF NOT EXISTS ix_notification_outbox_due ON notification_outbox(state,next_attempt_at);
          CREATE TABLE IF NOT EXISTS notification_receipts(
            event_id INTEGER NOT NULL REFERENCES notification_events(id) ON DELETE CASCADE,
            client_id TEXT NOT NULL, state TEXT NOT NULL, received_at TEXT NOT NULL,
            PRIMARY KEY(event_id,client_id));
          CREATE TABLE IF NOT EXISTS mobile_push_tokens(
            client_id TEXT PRIMARY KEY, platform TEXT NOT NULL,
            token_cipher TEXT NOT NULL, updated_at TEXT NOT NULL, enabled INTEGER NOT NULL DEFAULT 1);
        """, ct);
        await Exec(connection, "UPDATE schema_version SET version=9 WHERE version<9;", ct);
    }

    private static async Task MigrateHistorianSamplesNullable(SqliteConnection c, CancellationToken ct)
    {
        if (!await TableExists(c, "historian_samples", ct)) return;

        await using var cmd = c.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(historian_samples);";
        var valueNotNull = false;
        var hasBatchId = false;

        await using (var r = await cmd.ExecuteReaderAsync(ct))
        {
            while (await r.ReadAsync(ct))
            {
                var name = r.GetString(1);
                if (string.Equals(name, "value", StringComparison.OrdinalIgnoreCase))
                    valueNotNull = r.GetInt32(3) != 0;
                if (string.Equals(name, "batch_id", StringComparison.OrdinalIgnoreCase))
                    hasBatchId = true;
            }
        }

        if (!valueNotNull) return;

        var batchSelect = hasBatchId ? "batch_id" : "NULL";
        await Exec(c, "PRAGMA foreign_keys=OFF;", ct);
        await Exec(c, "DROP TABLE IF EXISTS historian_samples_v8;", ct);
        await Exec(c, $"""
        CREATE TABLE historian_samples_v8(
          tag_id TEXT NOT NULL,timestamp_unix_ms INTEGER NOT NULL,value REAL NULL,quality TEXT NOT NULL,
          batch_id TEXT NULL,
          PRIMARY KEY(tag_id,timestamp_unix_ms),FOREIGN KEY(tag_id) REFERENCES tags(id) ON DELETE CASCADE
        ) WITHOUT ROWID;
        INSERT INTO historian_samples_v8(tag_id,timestamp_unix_ms,value,quality,batch_id)
          SELECT tag_id,timestamp_unix_ms,value,quality,{batchSelect} FROM historian_samples;
        DROP TABLE historian_samples;
        ALTER TABLE historian_samples_v8 RENAME TO historian_samples;
        CREATE INDEX IF NOT EXISTS ix_historian_samples_time ON historian_samples(timestamp_unix_ms);
        """, ct);
        await Exec(c, "PRAGMA foreign_keys=ON;", ct);
    }

    private static async Task MigrateLegacySignals(SqliteConnection c, CancellationToken ct)
    {
        if (!await TableExists(c, "signals", ct)) return;
        var hasBit = await ColumnExists(c, "signals", "bit_index", ct);
        var hasOrder = await ColumnExists(c, "signals", "byte_order", ct);
        var hasDecimals = await ColumnExists(c, "signals", "decimal_places", ct);
        var bit = hasBit ? "bit_index" : "NULL";
        var order = hasOrder ? "byte_order" : "'ABCD'";
        var decimals = hasDecimals ? "decimal_places" : "0";

        await Exec(c, $"""
        INSERT OR IGNORE INTO tags(id,device_id,name,address,data_type,bit_index,byte_order,unit,scale,offset,decimal_places,enabled,created_at,updated_at)
        SELECT id,device_id,name,address,data_type,{bit},{order},unit,
          CASE WHEN data_type IN ('UInt16','Int16','UInt32','Int32') AND ABS(scale-0.1)<0.0000001 THEN 1.0
               WHEN data_type IN ('UInt16','Int16','UInt32','Int32') AND ABS(scale-0.01)<0.0000001 THEN 1.0
               WHEN data_type IN ('UInt16','Int16','UInt32','Int32') AND ABS(scale-0.001)<0.0000001 THEN 1.0 ELSE scale END,
          offset,
          CASE WHEN data_type IN ('UInt16','Int16','UInt32','Int32') AND ABS(scale-0.1)<0.0000001 THEN 1
               WHEN data_type IN ('UInt16','Int16','UInt32','Int32') AND ABS(scale-0.01)<0.0000001 THEN 2
               WHEN data_type IN ('UInt16','Int16','UInt32','Int32') AND ABS(scale-0.001)<0.0000001 THEN 3 ELSE {decimals} END,
          enabled,created_at,updated_at FROM signals;
        """, ct);
    }

    private static async Task EnsureColumn(
        SqliteConnection c,
        string table,
        string column,
        string definition,
        CancellationToken ct)
    {
        if (await ColumnExists(c, table, column, ct)) return;
        await Exec(c, $"ALTER TABLE {table} ADD COLUMN {column} {definition};", ct);
    }

    private static async Task<bool> TableExists(SqliteConnection c, string table, CancellationToken ct)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$name;";
        cmd.Parameters.AddWithValue("$name", table);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(ct)) > 0;
    }

    private static async Task<bool> ColumnExists(
        SqliteConnection c,
        string table,
        string column,
        CancellationToken ct)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({table});";
        await using var r = await cmd.ExecuteReaderAsync(ct);
        while (await r.ReadAsync(ct))
        {
            if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static async Task Exec(SqliteConnection c, string sql, CancellationToken ct)
    {
        await using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        await cmd.ExecuteNonQueryAsync(ct);
    }
}
