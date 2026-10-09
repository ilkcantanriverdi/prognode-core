using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace Prognode.Client.Windows;

public sealed record PairingRecord(Guid ServerId, string DisplayName, string Host, int Port, string CertificateSha256, Guid ClientId, string Token, long Cursor = 0);

/// <summary>Pairing is stored per Windows user, encrypted with DPAPI (CurrentUser).</summary>
public static class ClientStore
{
    private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PROGNODE", "Client");
    private static readonly string FilePath = Path.Combine(Folder, "pairing.dat");
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("PROGNODE-CLIENT-PAIRING-V1");
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "PROGNODE Client";

    public static PairingRecord? Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return null;
            var plain = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<PairingRecord>(plain);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException)
        {
            return null;
        }
    }

    public static void Save(PairingRecord record)
    {
        Directory.CreateDirectory(Folder);
        var cipher = ProtectedData.Protect(JsonSerializer.SerializeToUtf8Bytes(record), Entropy, DataProtectionScope.CurrentUser);
        var temp = FilePath + ".tmp";
        File.WriteAllBytes(temp, cipher);
        File.Move(temp, FilePath, overwrite: true);
    }

    public static void Clear()
    {
        try { File.Delete(FilePath); } catch (IOException) { }
    }

    public static bool StartsWithWindows
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(RunName) is string;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue(RunName, $"\"{Environment.ProcessPath}\" --background");
            else key.DeleteValue(RunName, throwOnMissingValue: false);
        }
    }
}
