namespace Prognode.Core;

public sealed class SystemStatusService
{
    public object GetSnapshot() => new
    {
        product = "PROGNODE",
        coreVersion = "0.7.2-rc6.4.7-hf6.3-auto-lan-trend",
        status = "Running",
        architecture = "Modular Monolith",
        runtime = ".NET 10",
        historian = "Independent SQLite / WAL Historian",
        timestampUtc = DateTimeOffset.UtcNow
    };
}
