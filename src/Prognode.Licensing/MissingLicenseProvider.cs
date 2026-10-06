using Prognode.Contracts.Licensing;

namespace Prognode.Licensing;

public sealed class MissingLicenseProvider : ILicenseProvider
{
    public LicenseSnapshot GetCurrent()
    {
        var entitlements = new LicenseEntitlements(
            Devices: CapacityLimit.Limited(0),
            MonitoredSignals: CapacityLimit.Limited(0),
            AlarmDefinitions: CapacityLimit.Limited(0),
            RecordedSignals: CapacityLimit.Limited(0),
            LanAccessEnabled: false,
            CloudPushEnabled: false,
            ApiEnabled: false,
            Protocols: new HashSet<string>(),
            Modules: new HashSet<string>(),
            RemoteAccessEnabled: false,
            RemoteAccessUnlimited: false,
            MaxRemoteClients: 0,
            RemoteAccessExpiresAtUtc: null
        );

        return new LicenseSnapshot(
            LicenseId: "NONE",
            Customer: "UNLICENSED",
            Plan: "NONE",
            Status: "INVALID",
            IsValid: false,
            ExpiresAt: null,
            GraceUntil: null,
            Entitlements: entitlements,
            Source: "Production Safety",
            GracePeriodDays: LicenseEntitlementService.GracePeriodDays,
            PricingVersion: "UNKNOWN"
        );
    }
}
