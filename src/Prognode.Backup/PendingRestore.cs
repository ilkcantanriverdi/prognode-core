using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;

namespace Prognode.Backup;

/// <summary>
/// A restore approved in Settings › Backup while Core runs: the backup is staged next to the data
/// folder and this marker asks the next Core start to swap it in before anything opens the data.
/// The marker and staging folder must be written by Core itself (or an administrator), so a standard
/// Windows user cannot plant a replacement data folder.
/// </summary>
public static class PendingRestore
{
    public sealed record Request(string StagingPath, string SourceFile, DateTimeOffset RequestedAtUtc, string RequestedBy);
    public sealed record Result(bool Success, string Message, string? RollbackPath, DateTimeOffset AppliedAtUtc);

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    static string Parent(string dataRoot) => Path.GetDirectoryName(Path.GetFullPath(dataRoot).TrimEnd(Path.DirectorySeparatorChar))!;
    public static string MarkerPath(string dataRoot) => Path.Combine(Parent(dataRoot), ".pgn-restore-pending.json");
    public static string ResultPath(string dataRoot) => Path.Combine(Parent(dataRoot), ".pgn-restore-result.json");

    public static void Save(string dataRoot, Request request)
    {
        var marker = MarkerPath(dataRoot);
        var stage = marker + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllText(stage, JsonSerializer.Serialize(request, Json));
        File.Move(stage, marker, overwrite: true);
    }

    public static Request? Read(string dataRoot)
    {
        try { return JsonSerializer.Deserialize<Request>(File.ReadAllText(MarkerPath(dataRoot)), Json); }
        catch { return null; }
    }

    public static Result? LastResult(string dataRoot)
    {
        try { return JsonSerializer.Deserialize<Result>(File.ReadAllText(ResultPath(dataRoot)), Json); }
        catch { return null; }
    }

    /// <summary>Called at Core start, before the data folder is locked or opened. Never throws.</summary>
    public static Result? ApplyIfPresent(string dataRoot)
    {
        dataRoot = Path.GetFullPath(dataRoot);
        var marker = MarkerPath(dataRoot);
        if (!File.Exists(marker)) return null;
        Result result;
        // Set only once the path is proven to be our own staging folder: the request file is input,
        // and cleanup must never delete a folder it names elsewhere.
        string? staging = null;
        try
        {
            var request = Read(dataRoot) ?? throw new InvalidDataException("The restore request is unreadable.");
            var candidate = Path.GetFullPath(request.StagingPath);
            if (!candidate.StartsWith(dataRoot + ".restore-", StringComparison.OrdinalIgnoreCase) ||
                candidate.IndexOfAny(['\\', '/'], dataRoot.Length) >= 0 || !Directory.Exists(candidate))
                throw new InvalidDataException("The staged restore folder is missing or not next to the data folder.");
            staging = candidate;
            if (!TrustedOwner(marker) || !TrustedOwner(staging))
                throw new UnauthorizedAccessException("The restore request was not created by PROGNODE Monitor or an administrator.");
            var rollback = BackupArchive.ApplyStagedRestore(staging, dataRoot);
            result = new Result(true, $"Restored {Path.GetFileName(request.SourceFile)}.", rollback, DateTimeOffset.UtcNow);
        }
        catch (Exception error)
        {
            if (staging is not null) { try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { } }
            result = new Result(false, "Restore was not applied; the previous data is unchanged. " + error.Message, null, DateTimeOffset.UtcNow);
        }
        try { File.Delete(marker); } catch { }
        try { File.WriteAllText(ResultPath(dataRoot), JsonSerializer.Serialize(result, Json)); } catch { }
        return result;
    }

    // Owner is LocalSystem, Administrators or the account Core runs as (developer runs).
    static bool TrustedOwner(string path)
    {
        if (!OperatingSystem.IsWindows()) return true;
        FileSystemSecurity security = Directory.Exists(path)
            ? new DirectoryInfo(path).GetAccessControl()
            : new FileInfo(path).GetAccessControl();
        var owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        if (owner is null) return false;
        using var me = WindowsIdentity.GetCurrent();
        return owner.IsWellKnown(WellKnownSidType.LocalSystemSid) ||
               owner.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid) ||
               (me.User is not null && owner.Equals(me.User));
    }
}
