using Microsoft.Data.Sqlite;
using Prognode.Contracts.Batches;
using Prognode.Core.Batches;

namespace Prognode.Data.Sqlite;

public sealed class SqliteBatchRepository(SqliteDatabaseOptions options) : IBatchRepository
{
    private const string SelectColumns = "id,batch_no,recipe_name,started_at,ended_at,state,operator_name,note";

    public async Task<BatchRun?> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM batch_runs WHERE state='Running' ORDER BY started_at DESC LIMIT 1;";
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async Task<BatchRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM batch_runs WHERE id=$id LIMIT 1;";
        command.Parameters.AddWithValue("$id", id.ToString());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async Task<IReadOnlyList<BatchRun>> GetRecentAsync(int limit, CancellationToken cancellationToken = default)
    {
        var result = new List<BatchRun>();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {SelectColumns} FROM batch_runs ORDER BY started_at DESC LIMIT $limit;";
        command.Parameters.AddWithValue("$limit", Math.Clamp(limit, 1, 200));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            result.Add(Read(reader));
        return result;
    }

    public async Task AddAsync(BatchRun batch, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
        INSERT INTO batch_runs(id,batch_no,recipe_name,started_at,ended_at,state,operator_name,note)
        VALUES($id,$batchNo,$recipeName,$startedAt,$endedAt,$state,$operator,$note);
        """;
        Bind(command, batch);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateAsync(BatchRun batch, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
        UPDATE batch_runs
        SET batch_no=$batchNo, recipe_name=$recipeName, started_at=$startedAt,
            ended_at=$endedAt, state=$state, operator_name=$operator, note=$note
        WHERE id=$id;
        """;
        Bind(command, batch);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static void Bind(SqliteCommand command, BatchRun batch)
    {
        command.Parameters.AddWithValue("$id", batch.Id.ToString());
        command.Parameters.AddWithValue("$batchNo", batch.BatchNo);
        command.Parameters.AddWithValue("$recipeName", (object?)batch.RecipeName ?? DBNull.Value);
        command.Parameters.AddWithValue("$startedAt", batch.StartedAt.ToString("O"));
        command.Parameters.AddWithValue("$endedAt", (object?)batch.EndedAt?.ToString("O") ?? DBNull.Value);
        command.Parameters.AddWithValue("$state", batch.State.ToString());
        command.Parameters.AddWithValue("$operator", (object?)batch.Operator ?? DBNull.Value);
        command.Parameters.AddWithValue("$note", (object?)batch.Note ?? DBNull.Value);
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

    private static BatchRun Read(SqliteDataReader reader)
    {
        if (!Enum.TryParse<BatchRunState>(reader.GetString(5), true, out var state))
            state = BatchRunState.Aborted;

        return new BatchRun(
            Guid.Parse(reader.GetString(0)),
            reader.GetString(1),
            reader.IsDBNull(2) ? null : reader.GetString(2),
            DateTimeOffset.Parse(reader.GetString(3)),
            reader.IsDBNull(4) ? null : DateTimeOffset.Parse(reader.GetString(4)),
            state,
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7));
    }
}
