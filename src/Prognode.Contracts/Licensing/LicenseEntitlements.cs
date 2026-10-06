namespace Prognode.Contracts.Licensing;

public sealed record LicenseEntitlements(
    CapacityLimit Devices,
    CapacityLimit MonitoredSignals,
    CapacityLimit AlarmDefinitions,
    CapacityLimit RecordedSignals,
    bool LanAccessEnabled,
    bool CloudPushEnabled,
    bool ApiEnabled,
    IReadOnlySet<string> Protocols,
    IReadOnlySet<string> Modules,
    bool RemoteAccessEnabled,
    bool RemoteAccessUnlimited,
    int? MaxRemoteClients,
    DateTimeOffset? RemoteAccessExpiresAtUtc = null
);
