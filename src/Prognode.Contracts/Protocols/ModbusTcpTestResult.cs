namespace Prognode.Contracts.Protocols;

public sealed record ModbusTcpTestResult(
    bool Success,
    string Stage,
    string Message,
    double ResponseTimeMs,
    string Host,
    int Port,
    int UnitId,
    int? HoldingRegister40001 = null
);
