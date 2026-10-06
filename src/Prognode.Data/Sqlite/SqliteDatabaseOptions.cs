namespace Prognode.Data.Sqlite;

public sealed record SqliteDatabaseOptions(string DatabasePath)
{
    public string ConnectionString =>
        $"Data Source={DatabasePath};Pooling=True";
}
