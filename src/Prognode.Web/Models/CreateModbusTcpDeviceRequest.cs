namespace Prognode.Web.Models;

public sealed record CreateModbusTcpDeviceRequest(
    string? Name,
    string? Host,
    int Port,
    int UnitId,
    int PollIntervalMs
);
