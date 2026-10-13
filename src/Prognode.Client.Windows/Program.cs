namespace Prognode.Client.Windows;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // One client per Windows user. Starting it again (Start menu, desktop) shows the running one.
        using var show = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\PROGNODE.Client.Show");
        using var single = new Mutex(true, @"Local\PROGNODE.Client", out var first);
        if (!first)
        {
            if (!args.Contains("--background")) show.Set();
            return;
        }
        ApplicationConfiguration.Initialize();
        var context = new ClientContext(background: args.Contains("--background"));
        var wait = ThreadPool.RegisterWaitForSingleObject(show, (_, _) => context.RequestShow(), null, Timeout.Infinite, executeOnlyOnce: false);
        Application.Run(context);
        wait.Unregister(null);
    }
}
