namespace Prognode.Contracts.Devices;

public sealed record DeviceHealthResult(
    bool Success,
    string Message,
    double ResponseTimeMs
);
