namespace Prognode.Client.Windows;

/// Writes unexpected errors to %LOCALAPPDATA%\PROGNODE\Client\client-error.log so a client that
/// fails on a customer PC can be diagnosed; a failure at start-up is also shown to the user.
internal static class CrashLog
{
    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PROGNODE", "Client", "client-error.log");

    public static void Write(Exception? error, string where)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var info = new FileInfo(FilePath);
            if (info.Exists && info.Length > 1_000_000) info.Delete();
            File.AppendAllText(FilePath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{where}] PROGNODE Client {Application.ProductVersion} on {Environment.OSVersion}{Environment.NewLine}" +
                $"{error}{Environment.NewLine}{Environment.NewLine}");
        }
        catch { /* Logging must never take the client down. */ }
    }

    public static void Fatal(Exception? error)
    {
        Write(error, "start-up");
        try
        {
            MessageBox.Show(
                $"PROGNODE Client could not start.\n\n{error?.Message}\n\nDetails were saved to:\n{FilePath}",
                "PROGNODE Client", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch { }
    }
}
