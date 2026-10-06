namespace Prognode.Web.Models;

public sealed record TestModbusTcpRequest(
    string? Host,
    int Port,
    int UnitId,
    int TimeoutMs
);
