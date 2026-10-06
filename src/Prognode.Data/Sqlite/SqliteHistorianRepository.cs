using Microsoft.Data.Sqlite;
using System.Runtime.CompilerServices;
using Prognode.Contracts.Historian;
using Prognode.Contracts.Tags;
using Prognode.Contracts.Trends;
using Prognode.Historian;

namespace Prognode.Data.Sqlite;

public sealed class SqliteHistorianRepository(
    SqliteDatabaseOptions options) : IHistorianRepository
{
    public async IAsyncEnumerable<HistorianSample> StreamRawAsync(
        IReadOnlyList<Guid> tagIds,
        DateTimeOffset from,
        DateTimeOffset to,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        if (tagIds.Count == 0) yield break;
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        var names = tagIds.Select((_, i) => $"$tag{i}").ToArray();
        command.CommandText = $"""
        SELECT tag_id,timestamp_unix_ms,value,quality,batch_id
        FROM historian_samples
        WHERE tag_id IN ({string.Join(",", names)})
          AND timestamp_unix_ms >= $from AND timestamp_unix_ms < $to
        ORDER BY timestamp_unix_ms ASC,tag_id ASC;
        """;
        for (var i = 0; i < tagIds.Count; i++)
            command.Parameters.AddWithValue(names[i], tagIds[i].ToString());
        command.Parameters.AddWithValue("$from", from.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$to", to.ToUnixTimeMilliseconds());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            yield return new HistorianSample(
                Guid.Parse(reader.GetString(0)), reader.GetInt64(1),
                reader.IsDBNull(2) ? null : reader.GetDouble(2), reader.GetString(3),
                reader.IsDBNull(4) ? null : Guid.Parse(reader.GetString(4)));
    }

    public async Task WriteBatchAsync(
        IReadOnlyList<HistorianSample> samples,
        CancellationToken cancellationToken = default)
    {
        if (samples.Count == 0) return;

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        foreach (var sample in samples)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
            INSERT OR IGNORE INTO historian_samples(
                tag_id, timestamp_unix_ms, value, quality, batch_id)
            VALUES($tagId,$timestamp,$value,$quality,$batchId);
            """;
            command.Parameters.AddWithValue("$tagId", sample.TagId.ToString());
            command.Parameters.AddWithValue("$timestamp", sample.TimestampUnixMs);
            command.Parameters.AddWithValue("$value", (object?)sample.Value ?? DBNull.Value);
            command.Parameters.AddWithValue("$quality", sample.Quality);
            command.Parameters.AddWithValue("$batchId", (object?)sample.BatchId?.ToString() ?? DBNull.Value);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<HistorianSeriesResult> QuerySeriesAsync(
        Guid tagId,
        DateTimeOffset from,
        DateTimeOffset to,
        int maxPoints,
        CancellationToken cancellationToken = default)
    {
        var fromMs = from.ToUnixTimeMilliseconds();
        var toMs = to.ToUnixTimeMilliseconds();
        await using var connection = await OpenAsync(cancellationToken);

        var stats = await GetSeriesStatsAsync(connection, tagId, fromMs, toMs, cancellationToken);
        if (stats.Count == 0)
            return new HistorianSeriesResult(tagId, [], 0, null, null, null);

        var points = stats.Count <= maxPoints
            ? await QueryRawPointsAsync(connection, tagId, fromMs, toMs, cancellationToken)
            : await QueryBucketedPointsAsync(connection, tagId, fromMs, toMs, maxPoints, cancellationToken);

        return new HistorianSeriesResult(
            tagId, points, stats.Count, stats.Minimum, stats.Maximum, stats.Average);
    }

    public async Task<IReadOnlyList<HistorianSample>> QueryRawAsync(
        IReadOnlyList<Guid> tagIds,
        DateTimeOffset from,
        DateTimeOffset to,
        int maxRows,
        CancellationToken cancellationToken = default)
    {
        if (tagIds.Count == 0) return [];
        var result = new List<HistorianSample>();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        var names = tagIds.Select((_,i) => $"$tag{i}").ToArray();
        command.CommandText = $"""
        SELECT tag_id,timestamp_unix_ms,value,quality,batch_id
        FROM historian_samples
        WHERE tag_id IN ({string.Join(",", names)})
          AND timestamp_unix_ms >= $from
          AND timestamp_unix_ms <= $to
        ORDER BY timestamp_unix_ms ASC
        LIMIT $limit;
        """;

        for (var i=0;i<tagIds.Count;i++)
            command.Parameters.AddWithValue(names[i], tagIds[i].ToString());
        command.Parameters.AddWithValue("$from", from.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$to", to.ToUnixTimeMilliseconds());
        command.Parameters.AddWithValue("$limit", maxRows);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new HistorianSample(
                Guid.Parse(reader.GetString(0)),
                reader.GetInt64(1),
                reader.IsDBNull(2) ? null : reader.GetDouble(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : Guid.Parse(reader.GetString(4))));
        }
        return result;
    }

    public async Task<IReadOnlyList<HistorianSample>> QueryByBatchAsync(
        Guid batchId,
        IReadOnlyList<Guid> tagIds,
        int maxRows,
        CancellationToken cancellationToken = default)
    {
        var result = new List<HistorianSample>();
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        var tagFilter = string.Empty;
        if (tagIds.Count > 0)
        {
            var names = tagIds.Select((_, i) => $"$batchTag{i}").ToArray();
            tagFilter = $" AND tag_id IN ({string.Join(",", names)})";
            for (var i = 0; i < tagIds.Count; i++)
                command.Parameters.AddWithValue(names[i], tagIds[i].ToString());
        }

        command.CommandText = $"""
        SELECT tag_id,timestamp_unix_ms,value,quality,batch_id
        FROM historian_samples
        WHERE batch_id=$batchId{tagFilter}
        ORDER BY timestamp_unix_ms ASC
        LIMIT $limit;
        """;
        command.Parameters.AddWithValue("$batchId", batchId.ToString());
        command.Parameters.AddWithValue("$limit", Math.Clamp(maxRows, 1, 250_000));

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            result.Add(new HistorianSample(
                Guid.Parse(reader.GetString(0)),
                reader.GetInt64(1),
                reader.IsDBNull(2) ? null : reader.GetDouble(2),
                reader.GetString(3),
                reader.IsDBNull(4) ? null : Guid.Parse(reader.GetString(4))));
        }

        return result;
    }

    public async Task<IReadOnlyList<HistorianTagStatus>> GetTagStatusesAsync(
        IReadOnlyList<HistorianRecordingConfiguration> configurations,
        CancellationToken cancellationToken = default)
    {
        var result = new List<HistorianTagStatus>();
        await using var connection = await OpenAsync(cancellationToken);

        foreach (var config in configurations)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = """
            SELECT COUNT(*), MIN(timestamp_unix_ms), MAX(timestamp_unix_ms)
            FROM historian_samples WHERE tag_id=$tagId;
            """;
            command.Parameters.AddWithValue("$tagId", config.TagId.ToString());

            long count;
            DateTimeOffset? first;
            DateTimeOffset? last;
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                await reader.ReadAsync(cancellationToken);
                count = reader.GetInt64(0);
                first = reader.IsDBNull(1) ? null : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(1));
                last = reader.IsDBNull(2) ? null : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(2));
            }

            double? lastValue = null;
            string? lastQuality = null;
            if (count > 0)
            {
                await using var lastCommand = connection.CreateCommand();
                lastCommand.CommandText = """
                SELECT value, quality
                FROM historian_samples
                WHERE tag_id=$tagId
                ORDER BY timestamp_unix_ms DESC LIMIT 1;
                """;
                lastCommand.Parameters.AddWithValue("$tagId", config.TagId.ToString());
                await using var lastReader = await lastCommand.ExecuteReaderAsync(cancellationToken);
                if (await lastReader.ReadAsync(cancellationToken))
                {
                    lastValue = lastReader.IsDBNull(0) ? null : lastReader.GetDouble(0);
                    lastQuality = lastReader.GetString(1);
                }
            }

            result.Add(new HistorianTagStatus(config, count, first, last, lastValue, lastQuality));
        }

        return result;
    }

    public async Task<HistorianStats> GetStatsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
        SELECT COUNT(*), COUNT(DISTINCT tag_id),
               MIN(timestamp_unix_ms), MAX(timestamp_unix_ms)
        FROM historian_samples;
        """;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        var count = reader.GetInt64(0);
        var recorded = Convert.ToInt32(reader.GetInt64(1));
        DateTimeOffset? oldest = reader.IsDBNull(2) ? null : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(2));
        DateTimeOffset? newest = reader.IsDBNull(3) ? null : DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(3));
        var bytes = FileSize(options.DatabasePath) + FileSize(options.DatabasePath+"-wal") + FileSize(options.DatabasePath+"-shm");
        return new HistorianStats(count, recorded, bytes, oldest, newest);
    }

    public async Task DeleteBeforeAsync(Guid tagId, DateTimeOffset cutoff,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM historian_samples WHERE tag_id=$tagId AND timestamp_unix_ms < $cutoff;";
        command.Parameters.AddWithValue("$tagId", tagId.ToString());
        command.Parameters.AddWithValue("$cutoff", cutoff.ToUnixTimeMilliseconds());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteTagDataAsync(Guid tagId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = "DELETE FROM historian_samples WHERE tag_id=$tagId;";
        command.Parameters.AddWithValue("$tagId", tagId.ToString());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<(long Count,double? Minimum,double? Maximum,double? Average)> GetSeriesStatsAsync(
        SqliteConnection connection, Guid tagId, long fromMs, long toMs, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
        SELECT COUNT(*),
               MIN(CASE WHEN quality='Good' THEN value END),
               MAX(CASE WHEN quality='Good' THEN value END),
               AVG(CASE WHEN quality='Good' THEN value END)
        FROM historian_samples
        WHERE tag_id=$tagId AND timestamp_unix_ms >= $from AND timestamp_unix_ms <= $to;
        """;
        command.Parameters.AddWithValue("$tagId", tagId.ToString());
        command.Parameters.AddWithValue("$from", fromMs);
        command.Parameters.AddWithValue("$to", toMs);
        await using var reader = await command.ExecuteReaderAsync(ct);
        await reader.ReadAsync(ct);
        return (
            reader.GetInt64(0),
            reader.IsDBNull(1) ? null : reader.GetDouble(1),
            reader.IsDBNull(2) ? null : reader.GetDouble(2),
            reader.IsDBNull(3) ? null : reader.GetDouble(3));
    }

    private static async Task<IReadOnlyList<TrendPoint>> QueryRawPointsAsync(
        SqliteConnection connection, Guid tagId, long fromMs, long toMs, CancellationToken ct)
    {
        var result = new List<TrendPoint>();
        await using var command = connection.CreateCommand();
        command.CommandText = """
        SELECT timestamp_unix_ms,value,quality
        FROM historian_samples
        WHERE tag_id=$tagId AND timestamp_unix_ms >= $from AND timestamp_unix_ms <= $to
        ORDER BY timestamp_unix_ms ASC;
        """;
        command.Parameters.AddWithValue("$tagId", tagId.ToString());
        command.Parameters.AddWithValue("$from", fromMs);
        command.Parameters.AddWithValue("$to", toMs);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new TrendPoint(
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)),
                reader.IsDBNull(1) ? null : reader.GetDouble(1),
                reader.GetString(2)));
        }
        return result;
    }

    private static async Task<IReadOnlyList<TrendPoint>> QueryBucketedPointsAsync(
        SqliteConnection connection, Guid tagId, long fromMs, long toMs, int maxPoints, CancellationToken ct)
    {
        // HF3+ long-range sampling: keep REAL first/last/min/max GOOD samples,
        // plus first/last of each non-GOOD quality state within each time bucket.
        // The previous AVG-only query erased brief spikes and changed BAD/STALE
        // intervals to invented aggregate values. Zoom queries fetch raw data.
        // Reserve room for all Good extremes, first/last samples, and first/last
        // of every non-Good TagQuality. Inclusive end times can add one bucket.
        var result = new List<TrendPoint>();
        var duration = Math.Max(1L, toMs - fromMs);
        var representativesPerBucket = 4 + 2 * (Enum.GetValues<TagQuality>().Length - 1);
        var bucketCount = Math.Max(1, maxPoints / representativesPerBucket - 1);
        var bucketMs = Math.Max(1L, (long)Math.Ceiling(duration / (double)bucketCount));
        await using var command = connection.CreateCommand();
        command.CommandText = """
        WITH ranged AS (
            SELECT timestamp_unix_ms, value, quality,
                   ((timestamp_unix_ms-$from)/$bucketMs) AS bucket
              FROM historian_samples
             WHERE tag_id=$tagId AND timestamp_unix_ms >= $from AND timestamp_unix_ms <= $to
        ), ranked AS (
            SELECT timestamp_unix_ms, value, quality,
                   ROW_NUMBER() OVER (PARTITION BY bucket ORDER BY timestamp_unix_ms ASC) AS first_rank,
                   ROW_NUMBER() OVER (PARTITION BY bucket ORDER BY timestamp_unix_ms DESC) AS last_rank,
                   ROW_NUMBER() OVER (PARTITION BY bucket ORDER BY
                       CASE WHEN quality='Good' AND value IS NOT NULL THEN 0 ELSE 1 END,
                       value ASC, timestamp_unix_ms ASC) AS min_good_rank,
                   ROW_NUMBER() OVER (PARTITION BY bucket ORDER BY
                       CASE WHEN quality='Good' AND value IS NOT NULL THEN 0 ELSE 1 END,
                       value DESC, timestamp_unix_ms ASC) AS max_good_rank,
                   ROW_NUMBER() OVER (PARTITION BY bucket, quality ORDER BY timestamp_unix_ms ASC) AS quality_first_rank,
                   ROW_NUMBER() OVER (PARTITION BY bucket, quality ORDER BY timestamp_unix_ms DESC) AS quality_last_rank
              FROM ranged
        )
        SELECT timestamp_unix_ms, value, quality FROM ranked
         WHERE first_rank=1 OR last_rank=1
            OR (quality='Good' AND value IS NOT NULL AND (min_good_rank=1 OR max_good_rank=1))
            OR (quality<>'Good' AND (quality_first_rank=1 OR quality_last_rank=1))
         ORDER BY timestamp_unix_ms ASC;
        """;
        command.Parameters.AddWithValue("$tagId", tagId.ToString());
        command.Parameters.AddWithValue("$from", fromMs);
        command.Parameters.AddWithValue("$to", toMs);
        command.Parameters.AddWithValue("$bucketMs", bucketMs);
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            result.Add(new TrendPoint(
                DateTimeOffset.FromUnixTimeMilliseconds(reader.GetInt64(0)),
                reader.IsDBNull(1) ? null : reader.GetDouble(1),
                reader.GetString(2)));
        }
        return result;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken ct)
    {
        var connection = new SqliteConnection(options.ConnectionString);
        await connection.OpenAsync(ct);
        return connection;
    }

    private static long FileSize(string path) => File.Exists(path) ? new FileInfo(path).Length : 0;
}
