using Microsoft.Data.Sqlite;
using Prognode.Contracts.Trends;
using Prognode.Trends;

namespace Prognode.Data.Sqlite;

public sealed class SqliteTrendRepository(SqliteDatabaseOptions options) : ITrendRepository
{
    public async Task<IReadOnlyList<TrendDefinition>> GetAllAsync(CancellationToken ct=default)
    {
        var result=new List<TrendDefinition>();
        await using var connection=await OpenAsync(ct);
        await using var command=connection.CreateCommand();
        command.CommandText="SELECT id,name,default_window_minutes,created_at,updated_at FROM trend_definitions ORDER BY created_at ASC;";
        var rows=new List<(Guid Id,string Name,int Window,DateTimeOffset Created,DateTimeOffset Updated)>();
        await using var reader=await command.ExecuteReaderAsync(ct);
        while(await reader.ReadAsync(ct)) rows.Add((Guid.Parse(reader.GetString(0)),reader.GetString(1),reader.GetInt32(2),DateTimeOffset.Parse(reader.GetString(3)),DateTimeOffset.Parse(reader.GetString(4))));
        await reader.DisposeAsync();
        foreach(var row in rows)
        {
            var series=await GetSeriesAsync(connection,row.Id,ct);
            result.Add(new TrendDefinition(row.Id,row.Name,series.Select(x=>x.TagId).ToArray(),series.Select(x=>x.Color).ToArray(),row.Window,row.Created,row.Updated));
        }
        return result;
    }

    public async Task<TrendDefinition?> GetByIdAsync(Guid id,CancellationToken ct=default)
    {
        await using var connection=await OpenAsync(ct);
        await using var command=connection.CreateCommand();
        command.CommandText="SELECT id,name,default_window_minutes,created_at,updated_at FROM trend_definitions WHERE id=$id LIMIT 1;";
        command.Parameters.AddWithValue("$id",id.ToString());
        await using var reader=await command.ExecuteReaderAsync(ct);
        if(!await reader.ReadAsync(ct)) return null;
        var row=(Id:Guid.Parse(reader.GetString(0)),Name:reader.GetString(1),Window:reader.GetInt32(2),Created:DateTimeOffset.Parse(reader.GetString(3)),Updated:DateTimeOffset.Parse(reader.GetString(4)));
        await reader.DisposeAsync();
        var series=await GetSeriesAsync(connection,row.Id,ct);
        return new TrendDefinition(row.Id,row.Name,series.Select(x=>x.TagId).ToArray(),series.Select(x=>x.Color).ToArray(),row.Window,row.Created,row.Updated);
    }

    public Task AddAsync(TrendDefinition trend,CancellationToken ct=default)=>SaveAsync(trend,false,ct);
    public Task UpdateAsync(TrendDefinition trend,CancellationToken ct=default)=>SaveAsync(trend,true,ct);

    public async Task DeleteAsync(Guid id,CancellationToken ct=default)
    {
        await using var connection=await OpenAsync(ct);
        await using var command=connection.CreateCommand();
        command.CommandText="DELETE FROM trend_definitions WHERE id=$id;";
        command.Parameters.AddWithValue("$id",id.ToString());
        await command.ExecuteNonQueryAsync(ct);
    }

    private async Task SaveAsync(TrendDefinition trend,bool update,CancellationToken ct)
    {
        await using var connection=await OpenAsync(ct);
        await using var tx=await connection.BeginTransactionAsync(ct);
        await using(var command=connection.CreateCommand())
        {
            command.Transaction=(SqliteTransaction)tx;
            command.CommandText=update
                ? "UPDATE trend_definitions SET name=$name,default_window_minutes=$window,updated_at=$updatedAt WHERE id=$id;"
                : "INSERT INTO trend_definitions(id,name,default_window_minutes,created_at,updated_at) VALUES($id,$name,$window,$createdAt,$updatedAt);";
            command.Parameters.AddWithValue("$id",trend.Id.ToString());
            command.Parameters.AddWithValue("$name",trend.Name);
            command.Parameters.AddWithValue("$window",trend.DefaultWindowMinutes);
            command.Parameters.AddWithValue("$createdAt",trend.CreatedAt.ToString("O"));
            command.Parameters.AddWithValue("$updatedAt",trend.UpdatedAt.ToString("O"));
            await command.ExecuteNonQueryAsync(ct);
        }
        if(update)
        {
            await using var del=connection.CreateCommand();
            del.Transaction=(SqliteTransaction)tx;
            del.CommandText="DELETE FROM trend_tags WHERE trend_id=$id;";
            del.Parameters.AddWithValue("$id",trend.Id.ToString());
            await del.ExecuteNonQueryAsync(ct);
        }
        for(var i=0;i<trend.TagIds.Count;i++)
        {
            await using var c=connection.CreateCommand();
            c.Transaction=(SqliteTransaction)tx;
            c.CommandText="INSERT INTO trend_tags(trend_id,tag_id,sort_order,color) VALUES($trendId,$tagId,$sortOrder,$color);";
            c.Parameters.AddWithValue("$trendId",trend.Id.ToString());
            c.Parameters.AddWithValue("$tagId",trend.TagIds[i].ToString());
            c.Parameters.AddWithValue("$sortOrder",i);
            c.Parameters.AddWithValue("$color",trend.Colors[i]);
            await c.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);
    }

    private static async Task<IReadOnlyList<(Guid TagId,string Color)>> GetSeriesAsync(SqliteConnection connection,Guid trendId,CancellationToken ct)
    {
        var result=new List<(Guid,string)>();
        await using var command=connection.CreateCommand();
        command.CommandText="SELECT tag_id,color FROM trend_tags WHERE trend_id=$id ORDER BY sort_order ASC;";
        command.Parameters.AddWithValue("$id",trendId.ToString());
        await using var reader=await command.ExecuteReaderAsync(ct);
        while(await reader.ReadAsync(ct)) result.Add((Guid.Parse(reader.GetString(0)),reader.GetString(1)));
        return result;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var c=new SqliteConnection(options.ConnectionString); await c.OpenAsync(ct);
        await using var command=c.CreateCommand(); command.CommandText="PRAGMA foreign_keys=ON;"; await command.ExecuteNonQueryAsync(ct);
        return c;
    }
}
