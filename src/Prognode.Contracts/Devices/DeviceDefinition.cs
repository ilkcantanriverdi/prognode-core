namespace Prognode.Contracts.Devices;

public sealed record DeviceDefinition(
    Guid Id,
    string Name,
    string Protocol,
    string Status,
    string? Host,
    int? Port,
    int? UnitId,
    int PollIntervalMs,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);
