namespace Prognode.Agent.Windows;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var singleInstance =
            new Mutex(
                initiallyOwned: true,
                name: @"Local\PROGNODE.Agent",
                createdNew: out var createdNew);

        if (!createdNew)
            return;

        ApplicationConfiguration.Initialize();
        Application.Run(
            new AgentApplicationContext());
    }
}
