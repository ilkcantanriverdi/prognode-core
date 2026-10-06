using Microsoft.Data.Sqlite;
using Prognode.Contracts.Historian;
using Prognode.Historian;

namespace Prognode.Data.Sqlite;

public sealed class SqliteHistorianConfigurationRepository(
    SqliteDatabaseOptions options) : IHistorianConfigurationRepository
{
    public async Task<IReadOnlyList<HistorianRecordingConfiguration>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        var result = new List<HistorianRecordingConfiguration>();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
        SELECT id, tag_id, sample_interval_seconds, retention_days,
               enabled, created_at, updated_at
        FROM historian_recording_configs
        ORDER BY created_at ASC;
        """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(Read(reader));
        return result;
    }

    public async Task<HistorianRecordingConfiguration?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
        SELECT id, tag_id, sample_interval_seconds, retention_days,
               enabled, created_at, updated_at
        FROM historian_recording_configs
        WHERE id = $id LIMIT 1;
        """;
        command.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async Task<HistorianRecordingConfiguration?> GetByTagIdAsync(
        Guid tagId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
        SELECT id, tag_id, sample_interval_seconds, retention_days,
               enabled, created_at, updated_at
        FROM historian_recording_configs
        WHERE tag_id = $tagId LIMIT 1;
        """;
        command.Parameters.AddWithValue("$tagId", tagId.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public Task AddAsync(HistorianRecordingConfiguration item,
        CancellationToken cancellationToken = default) =>
        SaveAsync(item, false, cancellationToken);

    public Task UpdateAsync(HistorianRecordingConfiguration item,
        CancellationToken cancellationToken = default) =>
        SaveAsync(item, true, cancellationToken);

    public async Task DeleteAsync(Guid id,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM historian_recording_configs WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private async Task SaveAsync(HistorianRecordingConfiguration item, bool update,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = update
            ? """
              UPDATE historian_recording_configs
              SET tag_id=$tagId, sample_interval_seconds=$interval,
                  retention_days=$retention, enabled=$enabled,
                  updated_at=$updatedAt
              WHERE id=$id;
              """
            : """
              INSERT INTO historian_recording_configs(
                  id, tag_id, sample_interval_seconds, retention_days,
                  enabled, created_at, updated_at)
              VALUES($id,$tagId,$interval,$retention,$enabled,$createdAt,$updatedAt);
              """;

        command.Parameters.AddWithValue("$id", item.Id.ToString());
        command.Parameters.AddWithValue("$tagId", item.TagId.ToString());
        command.Parameters.AddWithValue("$interval", item.SampleIntervalSeconds);
        command.Parameters.AddWithValue("$retention", item.RetentionDays);
        command.Parameters.AddWithValue("$enabled", item.Enabled ? 1 : 0);
        command.Parameters.AddWithValue("$createdAt", item.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", item.UpdatedAt.ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
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

    private static HistorianRecordingConfiguration Read(SqliteDataReader r) =>
        new(
            Guid.Parse(r.GetString(0)),
            Guid.Parse(r.GetString(1)),
            r.GetInt32(2),
            r.GetInt32(3),
            r.GetInt32(4) != 0,
            DateTimeOffset.Parse(r.GetString(5)),
            DateTimeOffset.Parse(r.GetString(6)));
}
