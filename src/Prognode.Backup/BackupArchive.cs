using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Prognode.Backup;

public sealed record BackupFile(string Path, long Size, string Sha256);
public sealed record BackupOverview(int Devices, int Tags, int AlarmDefinitions,
    long HistorianSamples, long? FirstSampleUnixMs, long? LastSampleUnixMs);
public sealed record BackupManifest(int FormatVersion, string CreatedAtUtc, string ServerId,
    int DatabaseSchema, string CoreVersion, List<BackupFile> Files, BackupOverview? Overview = null);
public sealed record BackupResult(string FileName, long SizeBytes, BackupManifest Manifest);

/// <summary>PGNBK001: PBKDF2-SHA256 (310k), one-MiB independently authenticated AES-256-GCM
/// chunks. A final authenticated zero-length record detects truncation. The ZIP contains only
/// an online SQLite snapshot and whitelisted persisted Core configuration.</summary>
public static class BackupArchive
{
    public const int FormatVersion = 1;
    const int ChunkSize = 1024 * 1024;
    const int Iterations = 310_000;
    static readonly byte[] Magic = "PGNBK001"u8.ToArray();
    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static BackupResult Create(string dataRoot, string outputPath, string passphrase,
        string coreVersion, string? appSettingsPath = null)
    {
        CheckPassword(passphrase);
        dataRoot = Path.GetFullPath(dataRoot);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        // Temp snapshot is on the local server only. The final export is always encrypted.
        var temp = Path.Combine(Path.GetTempPath(), "prognode-backup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temp);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(temp,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        var tempOutput = outputPath + ".partial-" + Guid.NewGuid().ToString("N");
        try
        {
            var snapshot = Path.Combine(temp, "prognode.db");
            var db = Path.Combine(dataRoot, "prognode.db");
            if (!File.Exists(db)) throw new FileNotFoundException("Core database not found", db);
            using (var source = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = db, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString()))
            using (var dest = new SqliteConnection(new SqliteConnectionStringBuilder
                { DataSource = snapshot, Pooling = false }.ToString()))
            {
                source.Open(); dest.Open(); source.BackupDatabase(dest);
            }
            var files = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
                { ["data/prognode.db"] = snapshot };
            foreach (var source in Directory.EnumerateFiles(dataRoot, "*",new EnumerationOptions {
                RecurseSubdirectories=true, IgnoreInaccessible=false, AttributesToSkip=FileAttributes.ReparsePoint }))
            {
                var relative = Path.GetRelativePath(dataRoot, source).Replace('\\', '/');
                if (!SafeDataRelative(relative) || relative.Equals("prognode.db", StringComparison.OrdinalIgnoreCase)) continue;
                files["data/" + relative] = source;
            }
            if (!string.IsNullOrWhiteSpace(appSettingsPath) && File.Exists(appSettingsPath))
                files["configuration/appsettings.json"] = appSettingsPath;
            var manifestFiles = new List<BackupFile>();
            BackupManifest? finalManifest = null;
            // Hash immediately before archiving and verify that the source has not changed during copy.
            var serverId = TryGetServerId(Path.Combine(dataRoot, "server-access.json"));
            var schema = ReadSchema(snapshot);
            var overview = ReadOverview(snapshot);
            using (var target = new FileStream(tempOutput, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var cipher = new GcmChunkWriter(target, passphrase))
            {
                using (var zip = new ZipArchive(cipher, ZipArchiveMode.Create, leaveOpen: true))
                {
                    foreach (var (name, file) in files.OrderBy(x => x.Key, StringComparer.Ordinal))
                    {
                        // File contents may change while services run. SQLite uses online backup;
                        // mutable JSON is retried once if its metadata changes during the copy.
                        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
                        if (name == "data/prognode.db")
                        {
                            // Never materialize a whole Historian database into managed memory.
                            using var sourceFile = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read);
                            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                            using var stream = entry.Open();
                            var block = new byte[81920]; long bytes = 0;
                            for(int read; (read=sourceFile.Read(block)) != 0;)
                            { stream.Write(block,0,read); digest.AppendData(block.AsSpan(0,read));bytes+=read; }
                            manifestFiles.Add(new BackupFile(name,bytes,Convert.ToHexString(digest.GetHashAndReset())));
                        }
                        else
                        {
                            var contents=ReadStable(file);
                            using (var stream = entry.Open()) stream.Write(contents);
                            manifestFiles.Add(new BackupFile(name,contents.LongLength,Convert.ToHexString(SHA256.HashData(contents))));
                        }
                    }
                    finalManifest = new BackupManifest(FormatVersion, DateTimeOffset.UtcNow.ToString("O"),
                        serverId, schema, coreVersion, manifestFiles, overview);
                    using (var stream = zip.CreateEntry("manifest.json", CompressionLevel.Optimal).Open())
                        JsonSerializer.Serialize(stream, finalManifest, Json);
                }
                cipher.Seal();
            }
            File.Move(tempOutput, outputPath, overwrite: false);
            return new BackupResult(Path.GetFileName(outputPath), new FileInfo(outputPath).Length,
                finalManifest ?? throw new InvalidDataException("Backup manifest was not written."));
        }
        finally
        {
            try { if (File.Exists(tempOutput)) File.Delete(tempOutput); } catch { }
            try { Directory.Delete(temp, true); } catch { }
        }
    }

    public static BackupManifest Verify(string encryptedFile, string passphrase)
    {
        using var decrypted = DecryptToTemp(encryptedFile, passphrase);
        return VerifyZip(decrypted.Path);
    }

    public static BackupManifest RestoreOffline(string encryptedFile, string dataRoot, string passphrase,
        bool allowOlderSchema = false, bool adoptSourceIdentity = false)
    {
        dataRoot = Path.GetFullPath(dataRoot);
        if (Path.GetPathRoot(dataRoot) == dataRoot) throw new InvalidOperationException("Cannot restore disk root.");
        using var decrypted = DecryptToTemp(encryptedFile, passphrase);
        var manifest = VerifyZip(decrypted.Path);
        using var restoreGuard = DataRootLock.Acquire(dataRoot);
        var currentServerId=TryGetServerId(Path.Combine(dataRoot,"server-access.json"));
        if(!string.IsNullOrWhiteSpace(currentServerId) && !string.IsNullOrWhiteSpace(manifest.ServerId) &&
           !string.Equals(currentServerId,manifest.ServerId,StringComparison.OrdinalIgnoreCase) &&
           !adoptSourceIdentity)
            throw new InvalidOperationException("BACKUP_DIFFERENT_SERVER_ID: source and target installation identities differ. This verified backup is safe, but restoring it requires explicit offline --adopt-source-identity approval. The existing data remains unchanged.");
        if (manifest.DatabaseSchema > 9 && !allowOlderSchema)
            throw new InvalidDataException("This backup needs a newer PROGNODE Core database schema.");
        if (!manifest.Files.Any(x => x.Path == "data/prognode.db"))
            throw new InvalidDataException("Backup has no Core database.");
        var staging = dataRoot + ".restore-" + Guid.NewGuid().ToString("N");
        var rollback = dataRoot + ".before-restore-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N")[..6];
        Directory.CreateDirectory(staging);
        try
        {
            // Extract only verified, whitelisted data paths, not arbitrary ZIP entries.
            using (var zip = ZipFile.OpenRead(decrypted.Path))
            foreach (var file in manifest.Files)
            {
                if (!file.Path.StartsWith("data/", StringComparison.Ordinal) ||
                    !SafeDataRelative(file.Path[5..])) continue;
                var destination = Path.GetFullPath(Path.Combine(staging, file.Path[5..].Replace('/', Path.DirectorySeparatorChar)));
                if (!destination.StartsWith(staging + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Unsafe backup path.");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                using var input = zip.GetEntry(file.Path)!.Open();
                using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                input.CopyTo(output);
            }
            // A newer schema must not be silently installed into older Core binaries.
            if (!allowOlderSchema && Directory.Exists(dataRoot) && File.Exists(Path.Combine(dataRoot, "prognode.db")))
            {
                var currentSchema = ReadSchema(Path.Combine(dataRoot, "prognode.db"));
                if (manifest.DatabaseSchema > currentSchema)
                    throw new InvalidOperationException($"Backup DB schema {manifest.DatabaseSchema} exceeds local {currentSchema}. Upgrade Core first.");
            }
            // Restore TLS/network settings only as a review file, and write it BEFORE swapping data.
            // A failed review-file write must never leave an ambiguous partial data restore.
            using (var zip = ZipFile.OpenRead(decrypted.Path))
            {
                var configuration = zip.GetEntry("configuration/appsettings.json");
                if(configuration is not null)
                {
                    var reviewPath = Path.Combine(Path.GetDirectoryName(dataRoot)!,
                        "PROGNODE_RESTORED_APPSETTINGS_REVIEW-"+Guid.NewGuid().ToString("N")[..8]+".json");
                    using var output = new FileStream(reviewPath,FileMode.CreateNew,FileAccess.Write,FileShare.None);
                    using var input=configuration.Open();input.CopyTo(output);
                }
            }
            // The caller MUST stop Core first. This also detects active SQLite connections on Windows.
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(dataRoot)) Directory.Move(dataRoot, rollback);
            try { Directory.Move(staging, dataRoot); }
            catch
            {
                if (Directory.Exists(rollback) && !Directory.Exists(dataRoot)) Directory.Move(rollback, dataRoot);
                throw;
            }
            // The previous data folder remains intact next to the new one for rollback.
            return manifest;
        }
        finally { try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { } }
    }

    // The service keeps backups outside dataRoot. Exclude untrusted/redundant runtime cache,
    // transient SQLite sidecars and any key material; encrypted server-access stays included.
    public static bool SafeDataRelative(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.StartsWith('/') || path.Contains('\\')) return false;
        var parts = path.Split('/');
        if (parts.Any(p => p is "" or "." or ".." || p.Contains(':'))) return false;
        return !parts.Any(p => p.Equals("backups", StringComparison.OrdinalIgnoreCase)
            || p.Equals(".work", StringComparison.OrdinalIgnoreCase)
            || p.Equals("private-keys", StringComparison.OrdinalIgnoreCase))
            && !path.EndsWith("-wal", StringComparison.OrdinalIgnoreCase)
            && !path.EndsWith("-shm", StringComparison.OrdinalIgnoreCase)
            && !path.EndsWith(".partial", StringComparison.OrdinalIgnoreCase)
            && !path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
            && !path.EndsWith(".pfx", StringComparison.OrdinalIgnoreCase)
            && !path.EndsWith(".pem", StringComparison.OrdinalIgnoreCase)
            && !path.EndsWith(".key", StringComparison.OrdinalIgnoreCase);
    }

    static byte[] ReadStable(string path)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var before = new FileInfo(path);
            var data = File.ReadAllBytes(path);
            var after = new FileInfo(path);
            if (before.Length == after.Length && before.LastWriteTimeUtc == after.LastWriteTimeUtc) return data;
        }
        throw new IOException("A live file changed during backup: " + Path.GetFileName(path));
    }
    static string TryGetServerId(string path)
    {
        try { using var j = JsonDocument.Parse(File.ReadAllText(path));
            return j.RootElement.GetProperty("serverId").GetString() ?? ""; }
        catch { return ""; }
    }
    public static int ReadSchema(string path)
    {
        using var c = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        c.Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT version FROM schema_version LIMIT 1";
        return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
    }
    public static BackupOverview ReadOverview(string databasePath)
    {
        using var c = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = databasePath, Mode = SqliteOpenMode.ReadOnly, Pooling = false }.ToString());
        c.Open();
        long Count(string table)
        {
            using var cmd=c.CreateCommand();
            cmd.CommandText="SELECT COUNT(*) FROM "+table;
            return Convert.ToInt64(cmd.ExecuteScalar()??0L);
        }
        using var range=c.CreateCommand();
        range.CommandText="SELECT (SELECT timestamp_unix_ms FROM historian_samples ORDER BY timestamp_unix_ms ASC LIMIT 1),"+
            " (SELECT timestamp_unix_ms FROM historian_samples ORDER BY timestamp_unix_ms DESC LIMIT 1)";
        using var reader=range.ExecuteReader();
        long? min=null,max=null;
        if(reader.Read())
        {
            if(!reader.IsDBNull(0)) min=reader.GetInt64(0);
            if(!reader.IsDBNull(1)) max=reader.GetInt64(1);
        }
        reader.Close();
        return new BackupOverview((int)Count("devices"),(int)Count("tags"),
            (int)Count("alarm_definitions"),Count("historian_samples"),min,max);
    }
    static void CheckPassword(string passphrase)
    {
        if (string.IsNullOrWhiteSpace(passphrase) || passphrase.Length < 12)
            throw new ArgumentException("Backup password must contain at least 12 characters.");
    }
    static BackupManifest VerifyZip(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        var manifestEntry = zip.GetEntry("manifest.json") ?? throw new InvalidDataException("Manifest missing.");
        if (manifestEntry.Length > 2_000_000) throw new InvalidDataException("Manifest too large.");
        BackupManifest? manifest;
        using (var s = manifestEntry.Open()) manifest = JsonSerializer.Deserialize<BackupManifest>(s, Json);
        if (manifest is null || manifest.FormatVersion != FormatVersion || manifest.Files is null)
            throw new InvalidDataException("Unsupported backup format.");
        if (manifest.Files.Count > 100_000) throw new InvalidDataException("Too many backup files.");
        if (zip.Entries.Count != manifest.Files.Count + 1) throw new InvalidDataException("Unexpected ZIP entries.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "manifest.json" };
        foreach (var file in manifest.Files)
        {
            var valid = (file.Path.StartsWith("data/", StringComparison.Ordinal) && SafeDataRelative(file.Path[5..]))
                || file.Path == "configuration/appsettings.json";
            if (!valid || !names.Add(file.Path)) throw new InvalidDataException("Unsafe or duplicate entry.");
            var entry = zip.GetEntry(file.Path) ?? throw new InvalidDataException("Missing file " + file.Path);
            if (entry.Length != file.Size) throw new InvalidDataException("Invalid entry length " + file.Path);
            using var stream = entry.Open(); using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            var buffer = new byte[81920]; long size = 0;
            for (int n; (n = stream.Read(buffer)) != 0;)
            {
                size = checked(size + n);
                if (size > file.Size) throw new InvalidDataException("Entry overrun");
                hash.AppendData(buffer.AsSpan(0, n));
            }
            if (size != file.Size || !Convert.ToHexString(hash.GetHashAndReset()).Equals(file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Corrupted backup entry " + file.Path);
        }
        return manifest;
    }

    sealed class Decrypted : IDisposable
    {
        public string Path { get; }
        public Decrypted(string path) { Path = path; }
        public void Dispose() { try { Directory.Delete(System.IO.Path.GetDirectoryName(Path)!,true); } catch { } }
    }
    static Decrypted DecryptToTemp(string encryptedFile, string passphrase)
    {
        CheckPassword(passphrase);
        var directory = Path.Combine(Path.GetTempPath(), "prognode-verify-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(directory,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        var output = Path.Combine(directory,"snapshot.zip");
        try
        {
            using (var source = File.OpenRead(encryptedFile))
            using (var dest = new FileStream(output, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                Span<byte> header = stackalloc byte[36];source.ReadExactly(header);
                if (!header[..8].SequenceEqual(Magic)) throw new InvalidDataException("Not a PROGNODE encrypted backup.");
                var key = Rfc2898DeriveBytes.Pbkdf2(passphrase, header.Slice(8, 16), Iterations, HashAlgorithmName.SHA256, 32);
                try
                {
                    using var gcm = new AesGcm(key, 16);
                    int index = 0;
                    Span<byte> lengthBytes = stackalloc byte[4];
                    while (true)
                    {
                        source.ReadExactly(lengthBytes);
                        var length = BinaryPrimitives.ReadInt32LittleEndian(lengthBytes);
                        if (length < 0 || length > ChunkSize) throw new InvalidDataException("Bad encrypted chunk length.");
                        var cipher = new byte[length];source.ReadExactly(cipher);
                        var tag = new byte[16];source.ReadExactly(tag);
                        var nonce = Nonce(header.Slice(24, 12), index);
                        var aad = AssociatedData(header, index, length);
                        var plain = new byte[length];
                        gcm.Decrypt(nonce, cipher, tag, plain, aad);
                        if (length == 0)
                        {
                            if (source.ReadByte() != -1) throw new InvalidDataException("Trailing backup data.");
                            break;
                        }
                        dest.Write(plain);
                        index = checked(index + 1);
                    }
                }
                finally { CryptographicOperations.ZeroMemory(key); }
            }
            return new Decrypted(output);
        }
        catch { try { Directory.Delete(directory,true); } catch { } throw; }
    }
    static byte[] Nonce(ReadOnlySpan<byte> baseNonce, int index)
    {
        var nonce = baseNonce.ToArray();
        BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(8, 4), (uint)index);
        return nonce;
    }
    static byte[] AssociatedData(ReadOnlySpan<byte> header, int index, int length)
    {
        var aad = new byte[44];header.CopyTo(aad);
        BinaryPrimitives.WriteInt32LittleEndian(aad.AsSpan(36,4), index);
        BinaryPrimitives.WriteInt32LittleEndian(aad.AsSpan(40,4), length);
        return aad;
    }
    sealed class GcmChunkWriter : Stream
    {
        readonly Stream _out;
        readonly AesGcm _gcm;
        readonly byte[] _key, _header = new byte[36], _buffer = new byte[ChunkSize];
        int _used, _index;
        bool _sealed;
        public GcmChunkWriter(Stream output, string passphrase)
        {
            _out = output;Magic.CopyTo(_header,0);
            RandomNumberGenerator.Fill(_header.AsSpan(8,28));
            // The last four nonce bytes are a counter and are never reused.
            _header.AsSpan(32,4).Clear();
            _key = Rfc2898DeriveBytes.Pbkdf2(passphrase, _header.AsSpan(8,16), Iterations, HashAlgorithmName.SHA256, 32);
            _gcm = new AesGcm(_key,16);_out.Write(_header);
        }
        public override void Write(byte[] buffer,int offset,int count) => Write(buffer.AsSpan(offset,count));
        public override void Write(ReadOnlySpan<byte> input)
        {
            if (_sealed) throw new InvalidOperationException("Already sealed.");
            while (!input.IsEmpty)
            {
                var length = Math.Min(ChunkSize-_used,input.Length);
                input[..length].CopyTo(_buffer.AsSpan(_used));_used+=length;input=input[length..];
                if (_used==ChunkSize) FlushChunk(_used);
            }
        }
        void FlushChunk(int count)
        {
            var nonce = Nonce(_header.AsSpan(24,12), _index);
            var aad = AssociatedData(_header,_index,count);
            var cipher = new byte[count];var tag = new byte[16];
            _gcm.Encrypt(nonce,_buffer.AsSpan(0,count),cipher,tag,aad);
            Span<byte> len = stackalloc byte[4];BinaryPrimitives.WriteInt32LittleEndian(len,count);
            _out.Write(len);_out.Write(cipher);_out.Write(tag);
            _index=checked(_index+1);_used=0;
        }
        public void Seal()
        {
            if (_sealed) return;
            if (_used>0)FlushChunk(_used);
            FlushChunk(0);
            _sealed=true;_out.Flush();
        }
        protected override void Dispose(bool disposing)
        {
            if (disposing) { try { if (!_sealed)Seal(); } finally { _gcm.Dispose();CryptographicOperations.ZeroMemory(_key);CryptographicOperations.ZeroMemory(_buffer); } }
            base.Dispose(disposing);
        }
        public override bool CanRead=>false;
        public override bool CanSeek=>false;
        public override bool CanWrite=>true;
        public override long Length=>throw new NotSupportedException();
        public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override void Flush()=>_out.Flush();
        public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
        public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();
        public override void SetLength(long value)=>throw new NotSupportedException();
    }
}
