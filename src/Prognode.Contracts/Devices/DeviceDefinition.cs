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
    DateTimeOffset UpdatedAt,
    // User name of the device credential (MQTT broker, OPC UA server) for display; the password
    // never leaves the credential store.
    string? Username = null
);
