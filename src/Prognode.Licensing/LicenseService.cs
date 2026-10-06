using Prognode.Contracts.Licensing;

namespace Prognode.Licensing;

/// <summary>
/// Effective runtime license policy. The signed local .pgnlicense remains the offline source of
/// entitlements, while a persisted Cloud REVOKED state can only remove runtime permission for
/// the exact same licenseId. Cloud unavailability never creates a revoke by itself.
/// </summary>
public sealed class LicenseService(
    ILicenseProvider provider,
    CoreCloudLicenseStateStore cloudState) : ITagCapacityPolicy, IDeviceCapacityPolicy
{
    public LicenseSnapshot Current => ApplyServerRevocation(provider.GetCurrent());

    public CapacityLimit DeviceLimit => Current.Entitlements.Devices;
    public CapacityLimit UniqueTagLimit => Current.Entitlements.MonitoredSignals;

    public bool AllowsUniqueTagCount(int resultingCount) =>
        Current.IsValid && Allows(UniqueTagLimit, resultingCount);

    public bool IsOverCapacity(int currentCount) =>
        Current.IsValid && currentCount >= 0 && !UniqueTagLimit.IsUnlimited &&
        currentCount > UniqueTagLimit.Value!.Value;

    public bool AllowsConfigurationChange(int currentTagCount) =>
        Current.IsValid && !IsOverCapacity(currentTagCount);

    public bool AllowsDeviceCount(int resultingCount) =>
        Current.IsValid && Allows(Current.Entitlements.Devices, resultingCount);
        // Commercial licenses retain unlimited PLC/device count; signed trials have a 1-device limit.

    public bool AllowsMonitoredSignalCount(int resultingCount) =>
        AllowsUniqueTagCount(resultingCount);

    public bool AllowsAlarmDefinitionCount(int resultingCount) =>
        HasModule("ALARM"); // Alarm definitions reuse licensed Tags; no extra capacity is consumed.

    public bool AllowsRecordedSignalCount(int resultingCount) =>
        HasModule("HISTORIAN"); // Historian configuration reuses licensed Tags.

    public bool HasModule(string module) =>
        Current.IsValid && Current.Entitlements.Modules.Contains(module);

    public bool IsLifecycleOperational =>
        Current.Status is "ACTIVE" or "EXPIRING_SOON" or "GRACE";

    private LicenseSnapshot ApplyServerRevocation(LicenseSnapshot local)
    {
        if (local.LicenseId is "NONE" or "INVALID")
            return local;

        var cloud = cloudState.Load(configured: true);
        var cloudRevoked = cloud.Revoked ||
            string.Equals(cloud.LicenseStatus, "REVOKED", StringComparison.OrdinalIgnoreCase);

        if (!cloudRevoked ||
            string.IsNullOrWhiteSpace(cloud.LicenseId) ||
            !string.Equals(cloud.LicenseId, local.LicenseId, StringComparison.OrdinalIgnoreCase))
            return local;

        // Preserve signed identity/expiry/capacity for authenticated diagnostics, but remove all
        // paid runtime authority immediately. HasModule/RemoteAccess/Tag mutation all key off IsValid.
        return local with
        {
            Status = "REVOKED",
            IsValid = false,
            Source = $"{local.Source} • Server revocation"
        };
    }

    private static bool Allows(CapacityLimit limit, int resultingCount) =>
        resultingCount >= 0 && (limit.IsUnlimited || resultingCount <= limit.Value!.Value);
}
