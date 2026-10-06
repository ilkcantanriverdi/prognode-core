using Microsoft.Data.Sqlite;
using Prognode.Alarm;
using Prognode.Contracts.Alarms;

namespace Prognode.Data.Sqlite;

public sealed class SqliteAlarmDefinitionRepository(
    SqliteDatabaseOptions options) : IAlarmDefinitionRepository
{
    private const string SelectColumns = """
        id, tag_id, alarm_text, priority, bit_index, trigger_value,
        condition, threshold, deadband,
        delay_on_ms, delay_off_ms, notify_on_active, notify_on_cleared,
        notification_mode, repeat_interval_seconds, continue_after_clear_until_ack,
        enabled, created_at, updated_at, requires_acknowledgement
        """;

    public async Task<IReadOnlyList<AlarmDefinition>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        var result = new List<AlarmDefinition>();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM alarm_definitions ORDER BY created_at ASC;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(Read(reader));
        return result;
    }

    public async Task<AlarmDefinition?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM alarm_definitions WHERE id=$id LIMIT 1;";
        command.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public Task AddAsync(
        AlarmDefinition definition,
        CancellationToken cancellationToken = default) =>
        SaveAsync(definition, update: false, cancellationToken);

    public Task UpdateAsync(
        AlarmDefinition definition,
        CancellationToken cancellationToken = default) =>
        SaveAsync(definition, update: true, cancellationToken);

    public async Task DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM alarm_definitions WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM alarm_definitions;";
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private async Task SaveAsync(
        AlarmDefinition definition,
        bool update,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = update
            ? """
              UPDATE alarm_definitions
              SET tag_id=$tagId, alarm_text=$text, priority=$priority,
                  bit_index=$bitIndex, trigger_value=$triggerValue,
                  condition=$condition, threshold=$threshold, deadband=$deadband,
                  delay_on_ms=$delayOnMs, delay_off_ms=$delayOffMs,
                  notify_on_active=$notifyOnActive, notify_on_cleared=$notifyOnCleared,
                  notification_mode=$notificationMode,
                  repeat_interval_seconds=$repeatIntervalSeconds,
                  continue_after_clear_until_ack=$continueAfterClearUntilAck,
                  requires_acknowledgement=$requiresAcknowledgement,
                  enabled=$enabled, updated_at=$updatedAt
              WHERE id=$id;
              """
            : """
              INSERT INTO alarm_definitions (
                  id,tag_id,alarm_text,priority,bit_index,trigger_value,
                  condition,threshold,deadband,
                  delay_on_ms,delay_off_ms,notify_on_active,notify_on_cleared,
                  notification_mode,repeat_interval_seconds,continue_after_clear_until_ack,
                  enabled,created_at,updated_at,requires_acknowledgement)
              VALUES (
                  $id,$tagId,$text,$priority,$bitIndex,$triggerValue,
                  $condition,$threshold,$deadband,
                  $delayOnMs,$delayOffMs,$notifyOnActive,$notifyOnCleared,
                  $notificationMode,$repeatIntervalSeconds,$continueAfterClearUntilAck,
                  $enabled,$createdAt,$updatedAt,$requiresAcknowledgement);
              """;

        Bind(command, definition);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void Bind(SqliteCommand command, AlarmDefinition x)
    {
        command.Parameters.AddWithValue("$id", x.Id.ToString());
        command.Parameters.AddWithValue("$tagId", x.TagId.ToString());
        command.Parameters.AddWithValue("$text", x.Text);
        command.Parameters.AddWithValue("$priority", x.Priority.ToString());
        command.Parameters.AddWithValue("$bitIndex", (object?)x.BitIndex ?? DBNull.Value);
        command.Parameters.AddWithValue("$triggerValue", x.TriggerValue ? 1 : 0);
        command.Parameters.AddWithValue("$condition", x.Condition.ToString());
        command.Parameters.AddWithValue("$threshold", (object?)x.Threshold ?? DBNull.Value);
        command.Parameters.AddWithValue("$deadband", x.Deadband);
        command.Parameters.AddWithValue("$delayOnMs", x.DelayOnMs);
        command.Parameters.AddWithValue("$delayOffMs", x.DelayOffMs);
        command.Parameters.AddWithValue("$notifyOnActive", x.NotifyOnActive ? 1 : 0);
        command.Parameters.AddWithValue("$notifyOnCleared", x.NotifyOnCleared ? 1 : 0);
        command.Parameters.AddWithValue("$notificationMode", x.NotificationMode.ToString());
        command.Parameters.AddWithValue("$repeatIntervalSeconds", x.RepeatIntervalSeconds);
        command.Parameters.AddWithValue("$continueAfterClearUntilAck", x.ContinueAfterClearUntilAcknowledged ? 1 : 0);
        command.Parameters.AddWithValue("$requiresAcknowledgement", x.RequiresAcknowledgement ? 1 : 0);
        command.Parameters.AddWithValue("$enabled", x.Enabled ? 1 : 0);
        command.Parameters.AddWithValue("$createdAt", x.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", x.UpdatedAt.ToString("O"));
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

    private static AlarmDefinition Read(SqliteDataReader r)
    {
        Enum.TryParse<AlarmPriority>(r.GetString(3), true, out var priority);
        var condition = Enum.TryParse<AlarmCondition>(r.GetString(6), true, out var parsedCondition)
            ? parsedCondition
            : AlarmCondition.DigitalEquals;
        var notificationMode = Enum.TryParse<AlarmNotificationMode>(r.GetString(13), true, out var parsedMode)
            ? parsedMode
            : AlarmNotificationMode.NotifyOnce;

        return new AlarmDefinition(
            Id: Guid.Parse(r.GetString(0)),
            TagId: Guid.Parse(r.GetString(1)),
            Text: r.GetString(2),
            Priority: priority,
            BitIndex: r.IsDBNull(4) ? null : r.GetInt32(4),
            TriggerValue: r.GetInt32(5) != 0,
            Condition: condition,
            Threshold: r.IsDBNull(7) ? null : r.GetDouble(7),
            Deadband: r.GetDouble(8),
            DelayOnMs: r.GetInt32(9),
            DelayOffMs: r.GetInt32(10),
            NotifyOnActive: r.GetInt32(11) != 0,
            NotifyOnCleared: r.GetInt32(12) != 0,
            NotificationMode: notificationMode,
            RepeatIntervalSeconds: r.GetInt32(14),
            ContinueAfterClearUntilAcknowledged: r.GetInt32(15) != 0,
            Enabled: r.GetInt32(16) != 0,
            CreatedAt: DateTimeOffset.Parse(r.GetString(17)),
            UpdatedAt: DateTimeOffset.Parse(r.GetString(18)),
            RequiresAcknowledgement: r.GetInt32(19) != 0);
    }
}
