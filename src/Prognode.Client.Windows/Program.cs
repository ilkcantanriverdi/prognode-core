namespace Prognode.Client.Windows;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        using var single = new Mutex(true, @"Local\PROGNODE.Client", out var first);
        if (!first) return;
        ApplicationConfiguration.Initialize();
        var context = new ClientContext(background: args.Contains("--background"));
        Application.Run(context);
    }
}
