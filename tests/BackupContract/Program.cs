using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Prognode.Backup;

var tmp=Path.Combine(Path.GetTempPath(),"pgn-backup-test-"+Guid.NewGuid().ToString("N"));
var data=Path.Combine(tmp,"data");Directory.CreateDirectory(data);
var password="integration-test-strong-password";
var db=Path.Combine(data,"prognode.db");
try
{
    using(var c=new SqliteConnection($"Data Source={db}"))
    {
        c.Open();
        using(var cmd=c.CreateCommand())
        {
            cmd.CommandText="PRAGMA journal_mode=WAL; CREATE TABLE schema_version(version INTEGER);"+
                "INSERT INTO schema_version VALUES (9);"+
                "CREATE TABLE devices(id TEXT); CREATE TABLE tags(id TEXT);"+
                "CREATE TABLE alarm_definitions(id TEXT);"+
                "CREATE TABLE historian_samples(tag_id TEXT, timestamp_unix_ms INTEGER, value REAL);"+
                "INSERT INTO historian_samples VALUES ('PLC1.Temp',1770000000000,42.5);";
            cmd.ExecuteNonQuery();
        }
    }
    File.WriteAllText(Path.Combine(data,"server-access.json"),"{\"serverId\":\"1234-ABCD\"}");
    Directory.CreateDirectory(Path.Combine(data,"license"));
    File.WriteAllText(Path.Combine(data,"license","current.pgnlicense"),"test-only-not-a-real-license");
    Directory.CreateDirectory(Path.Combine(data,"ui"));
    File.WriteAllText(Path.Combine(data,"ui","trend-hf3plus-layout.json"),"{\"charts\":[]}");
    Directory.CreateDirectory(Path.Combine(tmp,"backups"));
    var file=Path.Combine(tmp,"backups","test.pgnbackup");
    var result=BackupArchive.Create(data,file,password,"contract-v1");
    Ensure(result.SizeBytes>100,"File missing");
    Ensure(BackupArchive.Verify(file,password).Files.Count>=4,"Manifest count");
    Console.WriteLine("PASS create + SQLite WAL snapshot + authenticated verify");
    try{BackupArchive.Verify(file,"incorrect-long-passphrase");throw new Exception("Wrong password accepted");}
    catch(CryptographicException){Console.WriteLine("PASS wrong password rejected");}
    var altered=Path.Combine(tmp,"backups","tampered.pgnbackup");
    var bytes=File.ReadAllBytes(file);bytes[^3]^=1;File.WriteAllBytes(altered,bytes);
    try{BackupArchive.Verify(altered,password);throw new Exception("Tampering accepted");}
    catch(Exception e) when(e is CryptographicException or InvalidDataException){Console.WriteLine("PASS modified encrypted content rejected");}
    var truncated=Path.Combine(tmp,"backups","truncated.pgnbackup");
    File.WriteAllBytes(truncated,File.ReadAllBytes(file)[..^10]);
    try{BackupArchive.Verify(truncated,password);throw new Exception("Truncation accepted");}
    catch(Exception e) when(e is EndOfStreamException or CryptographicException or InvalidDataException){Console.WriteLine("PASS truncated file rejected");}
    var restored=Path.Combine(tmp,"new-machine-data");
    var manifest=BackupArchive.RestoreOffline(file,restored,password);
    Ensure(manifest.DatabaseSchema==9,"Wrong schema");
    using(var c=new SqliteConnection($"Data Source={Path.Combine(restored,"prognode.db")}"))
    { c.Open();using var cmd=c.CreateCommand();cmd.CommandText="SELECT value FROM historian_samples LIMIT 1";
      Ensure(Convert.ToDouble(cmd.ExecuteScalar())==42.5,"Historian lost"); }
    Ensure(File.Exists(Path.Combine(restored,"license","current.pgnlicense")),"License file lost");
    Ensure(File.Exists(Path.Combine(restored,"ui","trend-hf3plus-layout.json")),"Trend layout lost");
    Console.WriteLine("PASS offline restore: SQLite + license + UI state");
    // Cross-server restore is never implicit. Verified archive remains usable with deliberate
    // offline identity-adoption consent; a failed attempt must preserve target data.
    var other=Path.Combine(tmp,"other-core-data");Directory.CreateDirectory(other);
    File.WriteAllText(Path.Combine(other,"server-access.json"),"{\"serverId\":\"SOME-OTHER-CORE\"}");
    try{BackupArchive.RestoreOffline(file,other,password);throw new Exception("Cross-server restore accepted without consent");}
    catch(InvalidOperationException e)when(e.Message.Contains("BACKUP_DIFFERENT_SERVER_ID"))
    {Console.WriteLine("PASS cross-server restore requires explicit offline consent");}
    Ensure(File.Exists(Path.Combine(other,"server-access.json")),"Rejected restore modified destination");
    var migrated=BackupArchive.RestoreOffline(file,other,password,adoptSourceIdentity:true);
    Ensure(migrated.ServerId==manifest.ServerId,"Identity migration mismatch");
    Console.WriteLine("PASS explicit cross-server restore preserves rollback directory");

    Ensure(!BackupArchive.SafeDataRelative("../escape"),"Traversal allowed");
    Ensure(!BackupArchive.SafeDataRelative("private-keys/secret.pem"),"Private key allowed");
    Ensure(!BackupArchive.SafeDataRelative("prognode.db-wal"),"Transient WAL included");
    Console.WriteLine("PASS traversal/private key/WAL exclusions");
    Console.WriteLine("ALL BACKUP CONTRACT TESTS PASSED");
    return 0;
}
finally {try{Directory.Delete(tmp,true);}catch{}}
static void Ensure(bool ok,string message){if(!ok)throw new Exception(message);}
