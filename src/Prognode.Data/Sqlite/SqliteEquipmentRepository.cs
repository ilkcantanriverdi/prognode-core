using Microsoft.Data.Sqlite;
using Prognode.Contracts.Equipment;
using Prognode.Trends;

namespace Prognode.Data.Sqlite;

public sealed class SqliteEquipmentRepository(SqliteDatabaseOptions options) : IEquipmentRepository
{
    public async Task<IReadOnlyList<EquipmentDefinition>> GetAllAsync(CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        return await ReadAsync(connection, null, ct);
    }

    public async Task<EquipmentDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        return (await ReadAsync(connection, id, ct)).FirstOrDefault();
    }

    public Task AddAsync(EquipmentDefinition equipment, CancellationToken ct = default) => SaveAsync(equipment, false, ct);
    public Task UpdateAsync(EquipmentDefinition equipment, CancellationToken ct = default) => SaveAsync(equipment, true, ct);

    public async Task DeleteAsync(Guid id, CancellationToken ct = default)
    {
        await using var connection = await OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM equipment WHERE id=$id;";
        command.Parameters.AddWithValue("$id", id.ToString());
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task<IReadOnlyList<EquipmentDefinition>> ReadAsync(SqliteConnection connection, Guid? id, CancellationToken ct)
    {
        var items = new Dictionary<Guid, List<EquipmentItem>>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT equipment_id,tag_id,view FROM equipment_tags" +
                (id is null ? "" : " WHERE equipment_id=$id") + " ORDER BY sort_order ASC;";
            if (id is not null) command.Parameters.AddWithValue("$id", id.Value.ToString());
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var key = Guid.Parse(reader.GetString(0));
                if (!items.TryGetValue(key, out var list)) items[key] = list = [];
                list.Add(new EquipmentItem(Guid.Parse(reader.GetString(1)), reader.GetString(2)));
            }
        }
        var result = new List<EquipmentDefinition>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT id,name,device_id,created_at,updated_at FROM equipment" +
                (id is null ? "" : " WHERE id=$id") + " ORDER BY created_at ASC;";
            if (id is not null) command.Parameters.AddWithValue("$id", id.Value.ToString());
            await using var reader = await command.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var key = Guid.Parse(reader.GetString(0));
                result.Add(new EquipmentDefinition(key, reader.GetString(1),
                    reader.IsDBNull(2) ? null : Guid.Parse(reader.GetString(2)),
                    items.TryGetValue(key, out var list) ? list : [],
                    DateTimeOffset.Parse(reader.GetString(3)), DateTimeOffset.Parse(reader.GetString(4))));
            }
        }
        return result;
    }

    private async Task SaveAsync(EquipmentDefinition equipment, bool update, CancellationToken ct)
    {
        await using var connection = await OpenAsync(ct);
        await using var tx = (SqliteTransaction)await connection.BeginTransactionAsync(ct);
        await using (var command = connection.CreateCommand())
        {
            command.Transaction = tx;
            command.CommandText = update
                ? "UPDATE equipment SET name=$name,device_id=$deviceId,updated_at=$updatedAt WHERE id=$id;"
                : "INSERT INTO equipment(id,name,device_id,created_at,updated_at) VALUES($id,$name,$deviceId,$createdAt,$updatedAt);";
            command.Parameters.AddWithValue("$id", equipment.Id.ToString());
            command.Parameters.AddWithValue("$name", equipment.Name);
            command.Parameters.AddWithValue("$deviceId", (object?)equipment.DeviceId?.ToString() ?? DBNull.Value);
            command.Parameters.AddWithValue("$createdAt", equipment.CreatedAt.ToString("O"));
            command.Parameters.AddWithValue("$updatedAt", equipment.UpdatedAt.ToString("O"));
            await command.ExecuteNonQueryAsync(ct);
        }
        if (update)
        {
            await using var delete = connection.CreateCommand();
            delete.Transaction = tx;
            delete.CommandText = "DELETE FROM equipment_tags WHERE equipment_id=$id;";
            delete.Parameters.AddWithValue("$id", equipment.Id.ToString());
            await delete.ExecuteNonQueryAsync(ct);
        }
        for (var i = 0; i < equipment.Items.Count; i++)
        {
            await using var insert = connection.CreateCommand();
            insert.Transaction = tx;
            insert.CommandText = "INSERT INTO equipment_tags(equipment_id,tag_id,sort_order,view) VALUES($equipmentId,$tagId,$sortOrder,$view);";
            insert.Parameters.AddWithValue("$equipmentId", equipment.Id.ToString());
            insert.Parameters.AddWithValue("$tagId", equipment.Items[i].TagId.ToString());
            insert.Parameters.AddWithValue("$sortOrder", i);
            insert.Parameters.AddWithValue("$view", equipment.Items[i].View);
            await insert.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection(options.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys=ON;";
        await command.ExecuteNonQueryAsync(ct);
        return connection;
    }
}
