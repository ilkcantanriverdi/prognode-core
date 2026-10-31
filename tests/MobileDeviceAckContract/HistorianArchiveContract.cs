using System.IO.Compression;
using Prognode.Contracts.Devices;
using Prognode.Contracts.Historian;
using Prognode.Contracts.Tags;
using Prognode.Data.Sqlite;
using Prognode.Historian;
using Prognode.Web;

/// <summary>Historian ZIP: 10,000-row parts named by their first and last sample, the ZIP named Device_Tag.</summary>
internal static class HistorianArchiveContract
{
    public static async Task RunAsync(string root)
    {
        var options = new SqliteDatabaseOptions(Path.Combine(root, "archive-contract.db"));
        await new DatabaseInitializer(options).InitializeAsync();
        var now = DateTimeOffset.UtcNow;
        var device = new DeviceDefinition(Guid.NewGuid(), "PLC1", "Modbus TCP", "Configured", "127.0.0.1", 502, 1, 1000, now, now);
        await new SqliteDeviceRepository(options).AddAsync(device);
        var tag = new TagDefinition(Guid.NewGuid(), device.Id, "tag1", "40001", TagDataType.Float32, null, ModbusByteOrder.ABCD, "", 1, 0, 2, true, now, now);
        var tags = new SqliteTagRepository(options);
        await tags.AddAsync(tag);
        var repository = new SqliteHistorianRepository(options);
        var historian = new HistorianService(repository, new SqliteHistorianConfigurationRepository(options), tags);

        // 25,000 samples one second apart from 2026-10-18 10:00:00 UTC: parts of 10,000, 10,000 and 5,000.
        var start = new DateTimeOffset(2026, 10, 18, 10, 0, 0, TimeSpan.Zero);
        await repository.WriteBatchAsync(Enumerable.Range(0, 25_000)
            .Select(i => new HistorianSample(tag.Id, start.AddSeconds(i).ToUnixTimeMilliseconds(), 26.67311f, "Good")).ToList());

        var map = new Dictionary<Guid, TagDefinition> { [tag.Id] = tag };
        using var zip = new MemoryStream();
        await EndpointExtensions.WriteHistorianArchiveAsync(zip, historian, map, start, start.AddDays(1), default);
        zip.Position = 0;
        using var archive = new ZipArchive(zip, ZipArchiveMode.Read);
        var names = archive.Entries.Select(e => e.FullName).ToArray();
        Require(names.SequenceEqual([
            "001_2026-10-18_100000_to_2026-10-18_124639.csv",
            "002_2026-10-18_124640_to_2026-10-18_153319.csv",
            "003_2026-10-18_153320_to_2026-10-18_165639.csv"]), "parts named by first and last sample: " + string.Join(", ", names));
        using (var reader = new StreamReader(archive.Entries[0].Open()))
        {
            var lines = (await reader.ReadToEndAsync()).Split('\n', StringSplitOptions.RemoveEmptyEntries);
            Require(lines.Length == 10_001 && lines[1].Contains(";\"tag1\";26") && lines[1].Contains("67;\"Good\""), $"10,000 rows plus header, 2 decimals: {lines.Length} {lines[1]}");
        }

        Require(EndpointExtensions.HistorianZipName(map, [device], "Trend") == "PLC1_tag1.zip", "single tag: Device_Tag.zip");
        Require(EndpointExtensions.HistorianZipName(new Dictionary<Guid, TagDefinition>(), [device], "Line 3") == "Line_3.zip", "several tags: the trend name");

        using var empty = new MemoryStream();
        await EndpointExtensions.WriteHistorianArchiveAsync(empty, historian, map, start.AddYears(-1), start.AddYears(-1).AddDays(1), default);
        empty.Position = 0;
        using var emptyArchive = new ZipArchive(empty, ZipArchiveMode.Read);
        Require(emptyArchive.Entries.Single().FullName == "no_samples.csv", "an empty range still gives a readable ZIP");
        Console.WriteLine("PASS: historian ZIP (10,000-row parts named by date, Device_Tag.zip)");

        // Disk use: no time index, the batch index holds only samples recorded in a batch, and reads still use the key.
        await using var db = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={options.DatabasePath}");
        await db.OpenAsync();
        string Scalar(string sql) { using var c = db.CreateCommand(); c.CommandText = sql; return string.Join(" | ", Rows(c)); }
        static IEnumerable<string> Rows(Microsoft.Data.Sqlite.SqliteCommand c) { using var r = c.ExecuteReader(); while (r.Read()) yield return r.GetValue(r.FieldCount - 1).ToString()!; }
        Require(Scalar("SELECT name FROM sqlite_master WHERE type='index' AND tbl_name='historian_samples' AND sql IS NOT NULL") == "ix_historian_samples_batch_set",
            "only the partial batch index on historian_samples");
        Require(Scalar("EXPLAIN QUERY PLAN SELECT * FROM historian_samples WHERE batch_id='b1'").Contains("ix_historian_samples_batch_set"), "batch reads use the partial index");
        Require(Scalar("EXPLAIN QUERY PLAN SELECT * FROM historian_samples WHERE tag_id='t' AND timestamp_unix_ms>=1 AND timestamp_unix_ms<2").Contains("PRIMARY KEY"), "range reads use the key");
        Console.WriteLine("PASS: historian indexes (no time index, partial batch index)");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
