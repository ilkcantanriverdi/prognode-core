using System.Text;
using System.Text.Json;
using Prognode.Backup;

namespace Prognode.Host.Services;

public sealed class ProjectBackupService
{
    readonly SemaphoreSlim _gate = new(1,1);
    readonly object _auditGate = new();
    readonly string _dataRoot, _configurationPath;
    public string BackupRoot { get; }
    public int RetentionDays { get; }
    public int DailyHourLocal { get; }
    public string? AutoPassword => Environment.GetEnvironmentVariable("PROGNODE_BACKUP_PASSWORD");
    public ProjectBackupService(string dataRoot, string configurationPath, int dailyHourLocal, int retentionDays)
    {
        _dataRoot = Path.GetFullPath(dataRoot);
        _configurationPath = configurationPath;
        BackupRoot = Path.Combine(Path.GetDirectoryName(_dataRoot)!, "backups");
        DailyHourLocal = Math.Clamp(dailyHourLocal, 0, 23);
        RetentionDays = Math.Clamp(retentionDays, 1, 365);
        Directory.CreateDirectory(BackupRoot);
    }
    public object GetStatus() => new
    {
        autoEnabled = AutoPassword is { Length: >=12 },
        autoRequiresEnvironmentVariable = "PROGNODE_BACKUP_PASSWORD",
        dailyHourLocal = DailyHourLocal, retentionDays = RetentionDays,
        backupDirectory = BackupRoot,
        backups = List()
    };
    public object[] List() => Directory.EnumerateFiles(BackupRoot, "*.pgnbackup", SearchOption.TopDirectoryOnly)
        .Select(p => new FileInfo(p)).OrderByDescending(f => f.CreationTimeUtc).Take(100)
        .Select(f => (object)new { fileName = f.Name, createdAtUtc = f.CreationTimeUtc, sizeBytes = f.Length,
            automated = f.Name.StartsWith("AUTO-", StringComparison.OrdinalIgnoreCase) }).ToArray();
    public string? GetFile(string fileName)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(fileName, @"^[a-zA-Z0-9_.-]+\.pgnbackup$") ||
            fileName.Contains("..", StringComparison.Ordinal)) return null;
        var p = Path.Combine(BackupRoot, fileName);
        return File.Exists(p) ? p : null;
    }
    public async Task<BackupResult> CreateManualAsync(string password, string actor, CancellationToken ct)
    {
        if (!await _gate.WaitAsync(0, ct)) throw new InvalidOperationException("Backup operation is already running.");
        string? output = null;
        try
        {
            var name = $"PROGNODE-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.pgnbackup";
            output = Path.Combine(BackupRoot, name);
            var result = await Task.Run(() => BackupArchive.Create(_dataRoot,
                output, password, Prognode.Contracts.ProductVersion.Current,
                _configurationPath), ct);
            // An encrypted snapshot is not marked successful until its manifest and each file hash validates.
            await Task.Run(() => BackupArchive.Verify(output, password), ct);
            Audit(actor, "BACKUP_CREATED", "backup/manual", true);
            return result;
        }
        catch
        {
            if(output is not null) { try { File.Delete(output); } catch { } }
            Audit(actor, "BACKUP_FAILED", "backup/manual", false);
            throw;
        }
        finally { _gate.Release(); }
    }
    public async Task<bool> TryRunDailyAsync(CancellationToken ct)
    {
        var password = AutoPassword;
        if (string.IsNullOrWhiteSpace(password) || password.Length < 12) return false;
        var today = DateTime.Now.Date;
        if (DateTime.Now.Hour < DailyHourLocal || Directory.EnumerateFiles(BackupRoot, "AUTO-*.pgnbackup")
                .Any(p => File.GetCreationTime(p).Date == today)) return false;
        if (!await _gate.WaitAsync(0, ct)) return false;
        var output = Path.Combine(BackupRoot, $"AUTO-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.pgnbackup");
        try
        {
            await Task.Run(() => BackupArchive.Create(_dataRoot, output, password,
                Prognode.Contracts.ProductVersion.Current, _configurationPath), ct);
            await Task.Run(() => BackupArchive.Verify(output, password), ct);
            Audit("CORE", "AUTO_BACKUP_CREATED", "backup/automatic", true);
            PruneAutoBackups();
            return true;
        }
        catch
        {
            try { File.Delete(output); } catch { }
            Audit("CORE", "AUTO_BACKUP_FAILED", "backup/automatic", false);
            throw;
        }
        finally { _gate.Release(); }
    }
    public void PruneAutoBackups()
    {
        foreach (var p in Directory.EnumerateFiles(BackupRoot, "AUTO-*.pgnbackup"))
        {
            if (File.GetCreationTimeUtc(p) < DateTime.UtcNow.AddDays(-RetentionDays)) File.Delete(p);
        }
    }
    public void Audit(string actor, string operation, string route, bool success)
    {
        // Never persist passphrases, bearer tokens, query strings, request bodies or ZIP bytes.
        var entry = JsonSerializer.Serialize(new { utc = DateTimeOffset.UtcNow, actor,
            operation, route, success });
        var directory = Path.Combine(_dataRoot, "audit");
        lock (_auditGate)
        {
            Directory.CreateDirectory(directory);
            File.AppendAllText(Path.Combine(directory, "changes.jsonl"), entry + Environment.NewLine, Encoding.UTF8);
        }
    }
    public string[] ReadAudit(int take = 50)
    {
        var path = Path.Combine(_dataRoot, "audit", "changes.jsonl");
        lock (_auditGate) return File.Exists(path) ? File.ReadLines(path).TakeLast(Math.Clamp(take,1,100)).ToArray() : [];
    }
    public object? GetLayout()
    {
        var path = Path.Combine(_dataRoot, "ui", "trend-hf3plus-layout.json");
        if (!File.Exists(path)) return null;
        return JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(path));
    }
    public void DeleteLayout()
    {
        var path=Path.Combine(_dataRoot,"ui","trend-hf3plus-layout.json");
        if (File.Exists(path)) File.Delete(path);
    }
    public void SaveLayout(JsonElement layout)
    {
        if (layout.ValueKind != JsonValueKind.Object || layout.GetRawText().Length > 32768)
            throw new ArgumentException("Invalid layout or too many settings.");
        if (!layout.TryGetProperty("charts",out var charts) || charts.ValueKind != JsonValueKind.Array || charts.GetArrayLength()>15)
            throw new ArgumentException("Invalid Trend Studio layout.");
        var directory = Path.Combine(_dataRoot,"ui");Directory.CreateDirectory(directory);
        var path = Path.Combine(directory,"trend-hf3plus-layout.json");
        var tmp = path + ".tmp";
        File.WriteAllText(tmp,layout.GetRawText());File.Move(tmp,path,true);
    }
}
