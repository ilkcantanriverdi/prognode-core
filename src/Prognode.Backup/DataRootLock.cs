using System.Security.Cryptography;
using System.Text;

namespace Prognode.Backup;

/// <summary>Stable out-of-dataRoot process lock; stops restore from racing with a running
/// Core even when Core runs on a custom port, or runs on Linux.</summary>
public static class DataRootLock
{
    public static string LockPath(string dataRoot)
    {
        var full=Path.GetFullPath(dataRoot).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);
        var key=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(full.ToUpperInvariant())))[..16];
        return Path.Combine(Path.GetDirectoryName(full)!,".pgn-data-"+key+".lock");
    }
    public static FileStream Acquire(string dataRoot)
    {
        var path=LockPath(dataRoot);Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try { return new FileStream(path,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None); }
        catch(IOException e) { throw new InvalidOperationException("Core is running or another restore is active for this data directory. Stop Core first.",e); }
    }
}
