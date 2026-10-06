using Prognode.Backup;

if (args.Length is <4 or >5 || args[0] is not ("verify" or "restore" or "create") || args[2] != "--data" ||
    (args.Length == 5 && args[4] is not ("--json" or "--adopt-source-identity")))
{
    Console.Error.WriteLine("Usage: create|verify|restore <file.pgnbackup> --data <dataRoot>");
    Console.Error.WriteLine("Provide PROGNODE_BACKUP_PASSWORD in the calling process environment (not command line).");
    return 2;
}
var password = Environment.GetEnvironmentVariable("PROGNODE_BACKUP_PASSWORD");
if (string.IsNullOrWhiteSpace(password)) { Console.Error.WriteLine("Backup passphrase is required."); return 2; }
try
{
    if (args[0] == "create")
    {
        var settings=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(args[3]))!, "src", "Prognode.Host", "appsettings.json");
        var created=BackupArchive.Create(args[3],args[1],password,"pre-upgrade-HF3",
            File.Exists(settings)?settings:null);
        BackupArchive.Verify(args[1],password);
        Console.WriteLine($"CREATED AND VERIFIED: {created.FileName}, {created.SizeBytes} bytes, {created.Manifest.Files.Count} files");
        return 0;
    }
    if (args[0] == "verify")
    {
        var manifest = BackupArchive.Verify(args[1], password);
        if (args.Length==5 && args[4]=="--json")
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new {manifest.ServerId,manifest.CreatedAtUtc,manifest.DatabaseSchema,manifest.Overview,files=manifest.Files.Count}));
        else Console.WriteLine($"VERIFIED: {manifest.Files.Count} files, schema {manifest.DatabaseSchema}, created {manifest.CreatedAtUtc}, server {manifest.ServerId}");
        return 0;
    }
    var processData = Path.GetFullPath(args[3]);
    if (string.IsNullOrWhiteSpace(processData) || Path.GetPathRoot(processData) == processData)
        throw new InvalidOperationException("Do not restore into a disk root.");
    // The PowerShell launcher stops the service first. CLI adds a Windows safety check.
    if (OperatingSystem.IsWindows())
    {
        using var mutex = new System.Threading.Mutex(false, @"Global\PROGNODE_BACKUP_RESTORE");
        if (!mutex.WaitOne(TimeSpan.FromSeconds(5))) throw new InvalidOperationException("Another restore is running.");
        try { Restore(); } finally { mutex.ReleaseMutex(); }
    }
    else Restore();
    return 0;
    void Restore()
    {
        var manifest = BackupArchive.RestoreOffline(args[1], processData, password,
            adoptSourceIdentity: args.Length==5 && args[4]=="--adopt-source-identity");
        Console.WriteLine($"RESTORED: {manifest.Files.Count} files, schema {manifest.DatabaseSchema}");
        Console.WriteLine("Before-restore data directory is preserved next to the restored data directory.");
        Console.WriteLine("Review restored appsettings, local license, machine-bound TLS certificate and remote binding before starting Core.");
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine("Backup operation failed: " + ex.Message);
    return 1;
}
