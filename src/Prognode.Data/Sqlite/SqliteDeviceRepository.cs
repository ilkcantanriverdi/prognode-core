using Microsoft.Data.Sqlite;
using Prognode.Contracts.Devices;
using Prognode.Core.Devices;

namespace Prognode.Data.Sqlite;

public sealed class SqliteDeviceRepository(
    SqliteDatabaseOptions options) : IDeviceRepository
{
    public async Task<IReadOnlyList<DeviceDefinition>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        const string sql = """
        SELECT
            id, name, protocol, status, host, port, unit_id,
            poll_interval_ms, created_at, updated_at
        FROM devices
        ORDER BY created_at DESC;
        """;

        var result = new List<DeviceDefinition>();

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
            result.Add(ReadDevice(reader));

        return result;
    }

    public async Task<DeviceDefinition?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
        SELECT
            id, name, protocol, status, host, port, unit_id,
            poll_interval_ms, created_at, updated_at
        FROM devices
        WHERE id = $id
        LIMIT 1;
        """;

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", id.ToString());

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return ReadDevice(reader);
    }

    public async Task AddAsync(
        DeviceDefinition device,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
        INSERT INTO devices (
            id, name, protocol, status, host, port, unit_id,
            poll_interval_ms, created_at, updated_at
        )
        VALUES (
            $id, $name, $protocol, $status, $host, $port, $unitId,
            $pollIntervalMs, $createdAt, $updatedAt
        );
        """;

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", device.Id.ToString());
        command.Parameters.AddWithValue("$name", device.Name);
        command.Parameters.AddWithValue("$protocol", device.Protocol);
        command.Parameters.AddWithValue("$status", device.Status);
        command.Parameters.AddWithValue("$host", (object?)device.Host ?? DBNull.Value);
        command.Parameters.AddWithValue("$port", (object?)device.Port ?? DBNull.Value);
        command.Parameters.AddWithValue("$unitId", (object?)device.UnitId ?? DBNull.Value);
        command.Parameters.AddWithValue("$pollIntervalMs", device.PollIntervalMs);
        command.Parameters.AddWithValue("$createdAt", device.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", device.UpdatedAt.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }


    public async Task UpdateAsync(
        DeviceDefinition device,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
        UPDATE devices
        SET
            name = $name,
            protocol = $protocol,
            status = $status,
            host = $host,
            port = $port,
            unit_id = $unitId,
            poll_interval_ms = $pollIntervalMs,
            updated_at = $updatedAt
        WHERE id = $id;
        """;

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = sql;
        command.Parameters.AddWithValue("$id", device.Id.ToString());
        command.Parameters.AddWithValue("$name", device.Name);
        command.Parameters.AddWithValue("$protocol", device.Protocol);
        command.Parameters.AddWithValue("$status", device.Status);
        command.Parameters.AddWithValue("$host", (object?)device.Host ?? DBNull.Value);
        command.Parameters.AddWithValue("$port", (object?)device.Port ?? DBNull.Value);
        command.Parameters.AddWithValue("$unitId", (object?)device.UnitId ?? DBNull.Value);
        command.Parameters.AddWithValue("$pollIntervalMs", device.PollIntervalMs);
        command.Parameters.AddWithValue("$updatedAt", device.UpdatedAt.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = "DELETE FROM devices WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> CountAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM devices;";

        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(value);
    }

    private async Task<SqliteConnection> OpenAsync(
        CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(options.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=ON;";
        await command.ExecuteNonQueryAsync(cancellationToken);

        return connection;
    }

    private static DeviceDefinition ReadDevice(SqliteDataReader reader)
    {
        return new DeviceDefinition(
            Id: Guid.Parse(reader.GetString(0)),
            Name: reader.GetString(1),
            Protocol: reader.GetString(2),
            Status: reader.GetString(3),
            Host: reader.IsDBNull(4) ? null : reader.GetString(4),
            Port: reader.IsDBNull(5) ? null : reader.GetInt32(5),
            UnitId: reader.IsDBNull(6) ? null : reader.GetInt32(6),
            PollIntervalMs: reader.GetInt32(7),
            CreatedAt: DateTimeOffset.Parse(reader.GetString(8)),
            UpdatedAt: DateTimeOffset.Parse(reader.GetString(9))
        );
    }
}
