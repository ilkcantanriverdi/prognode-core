namespace Prognode.Core;

public sealed class SystemStatusService
{
    public object GetSnapshot() => new
    {
        product = "PROGNODE",
        coreVersion = Prognode.Contracts.ProductVersion.Current,
        status = "Running",
        architecture = "Modular Monolith",
        runtime = ".NET 10",
        historian = "Independent SQLite / WAL Historian",
        timestampUtc = DateTimeOffset.UtcNow
    };
}
