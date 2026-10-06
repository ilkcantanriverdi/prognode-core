using Microsoft.Data.Sqlite;
using Prognode.Contracts.Tags;
using Prognode.Core.Tags;

namespace Prognode.Data.Sqlite;

public sealed class SqliteTagRepository(
    SqliteDatabaseOptions options) : ITagRepository
{
    private const string SelectColumns = """
        id, device_id, name, address, data_type, bit_index,
        byte_order, unit, scale, offset, decimal_places,
        enabled, created_at, updated_at
        """;

    public async Task<IReadOnlyList<TagDefinition>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        var result = new List<TagDefinition>();

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = $"""
        SELECT {SelectColumns}
        FROM tags
        ORDER BY created_at ASC;
        """;

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
            result.Add(ReadTag(reader));

        return result;
    }

    public async Task<IReadOnlyList<TagDefinition>> GetByDeviceIdAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default)
    {
        var result = new List<TagDefinition>();

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = $"""
        SELECT {SelectColumns}
        FROM tags
        WHERE device_id = $deviceId
        ORDER BY created_at ASC;
        """;

        command.Parameters.AddWithValue(
            "$deviceId",
            deviceId.ToString());

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
            result.Add(ReadTag(reader));

        return result;
    }

    public async Task<TagDefinition?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = $"""
        SELECT {SelectColumns}
        FROM tags
        WHERE id = $id
        LIMIT 1;
        """;

        command.Parameters.AddWithValue("$id", id.ToString());

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? ReadTag(reader)
            : null;
    }

    public async Task AddAsync(
        TagDefinition tag,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
        INSERT INTO tags (
            id, device_id, name, address, data_type, bit_index,
            byte_order, unit, scale, offset, decimal_places,
            enabled, created_at, updated_at
        )
        VALUES (
            $id, $deviceId, $name, $address, $dataType, $bitIndex,
            $byteOrder, $unit, $scale, $offset, $decimalPlaces,
            $enabled, $createdAt, $updatedAt
        );
        """;

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        Bind(command, tag);
        command.CommandText = sql;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpdateAsync(
        TagDefinition tag,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
        UPDATE tags
        SET
            device_id = $deviceId,
            name = $name,
            address = $address,
            data_type = $dataType,
            bit_index = $bitIndex,
            byte_order = $byteOrder,
            unit = $unit,
            scale = $scale,
            offset = $offset,
            decimal_places = $decimalPlaces,
            enabled = $enabled,
            updated_at = $updatedAt
        WHERE id = $id;
        """;

        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        Bind(command, tag);
        command.CommandText = sql;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = "DELETE FROM tags WHERE id = $id;";
        command.Parameters.AddWithValue("$id", id.ToString());

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> CountAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = "SELECT COUNT(*) FROM tags;";

        var value = await command.ExecuteScalarAsync(cancellationToken);
        return Convert.ToInt32(value);
    }

    private static void Bind(
        SqliteCommand command,
        TagDefinition tag)
    {
        command.Parameters.AddWithValue("$id", tag.Id.ToString());
        command.Parameters.AddWithValue("$deviceId", tag.DeviceId.ToString());
        command.Parameters.AddWithValue("$name", tag.Name);
        command.Parameters.AddWithValue("$address", tag.Address);
        command.Parameters.AddWithValue("$dataType", tag.DataType.ToString());
        command.Parameters.AddWithValue(
            "$bitIndex",
            (object?)tag.BitIndex ?? DBNull.Value);
        command.Parameters.AddWithValue("$byteOrder", tag.ByteOrder.ToString());
        command.Parameters.AddWithValue("$unit", tag.Unit);
        command.Parameters.AddWithValue("$scale", tag.Scale);
        command.Parameters.AddWithValue("$offset", tag.Offset);
        command.Parameters.AddWithValue("$decimalPlaces", tag.DecimalPlaces);
        command.Parameters.AddWithValue("$enabled", tag.Enabled ? 1 : 0);
        command.Parameters.AddWithValue("$createdAt", tag.CreatedAt.ToString("O"));
        command.Parameters.AddWithValue("$updatedAt", tag.UpdatedAt.ToString("O"));
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

    private static TagDefinition ReadTag(
        SqliteDataReader reader)
    {
        if (!Enum.TryParse<TagDataType>(
            reader.GetString(4),
            ignoreCase: true,
            out var dataType))
        {
            dataType = TagDataType.UInt16;
        }

        if (!Enum.TryParse<ModbusByteOrder>(
            reader.GetString(6),
            ignoreCase: true,
            out var byteOrder))
        {
            byteOrder = ModbusByteOrder.ABCD;
        }

        return new TagDefinition(
            Id: Guid.Parse(reader.GetString(0)),
            DeviceId: Guid.Parse(reader.GetString(1)),
            Name: reader.GetString(2),
            Address: reader.GetString(3),
            DataType: dataType,
            BitIndex: reader.IsDBNull(5) ? null : reader.GetInt32(5),
            ByteOrder: byteOrder,
            Unit: reader.GetString(7),
            Scale: reader.GetDouble(8),
            Offset: reader.GetDouble(9),
            DecimalPlaces: reader.GetInt32(10),
            Enabled: reader.GetInt32(11) != 0,
            CreatedAt: DateTimeOffset.Parse(reader.GetString(12)),
            UpdatedAt: DateTimeOffset.Parse(reader.GetString(13))
        );
    }
}
