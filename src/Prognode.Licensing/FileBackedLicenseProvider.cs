using Prognode.Contracts.Licensing;

namespace Prognode.Licensing;

public sealed class FileBackedLicenseProvider(
    LocalLicenseStore store,
    LicenseSignatureVerifier verifier,
    LicenseEntitlementService entitlementService) : ILicenseProvider
{
    private readonly MissingLicenseProvider _missing = new();

    public string LicensePath => store.LicensePath;

    public LicenseSnapshot GetCurrent()
    {
        if (!store.Exists)
            return _missing.GetCurrent();

        try
        {
            return ToSnapshot(GetVerifiedCurrent());
        }
        catch
        {
            return Invalid("INVALID");
        }
    }

    internal PgnLicenseFileV2 GetVerifiedCurrent()
    {
        var rawBytes = store.ReadAllBytes();
        return verifier.Verify(rawBytes);
    }

    private LicenseSnapshot ToSnapshot(PgnLicenseFileV2 file)
    {
        var payload = file.Payload;
        var modules = entitlementService.ValidateAndGetModules(payload, rejectExpired: false);
        var remoteAccess = entitlementService.GetRemoteAccess(payload);
        var tagCapacity = entitlementService.GetTagCapacity(payload);
        var status = entitlementService.GetLicenseStatus(payload);
        var localOperational = status is "ACTIVE" or "EXPIRING_SOON" or "GRACE";
        var remoteExpiry = remoteAccess.ExpiresAtUtc ?? payload.Subscription.ExpiresAtUtc;

        return new LicenseSnapshot(
            payload.LicenseId,
            payload.OrganizationName,
            payload.Subscription.Product,
            status,
            localOperational && modules.Count > 0,
            payload.Subscription.ExpiresAtUtc,
            payload.Subscription.GraceUntilUtc,
            new LicenseEntitlements(
                string.Equals(payload.LicenseType, "TRIAL", StringComparison.OrdinalIgnoreCase)
                    ? CapacityLimit.Limited(1) : CapacityLimit.Unlimited,
                tagCapacity,
                modules.Contains("ALARM") ? CapacityLimit.Unlimited : CapacityLimit.Limited(0),
                modules.Contains("HISTORIAN") ? CapacityLimit.Unlimited : CapacityLimit.Limited(0),
                LanAccessEnabled: true,
                CloudPushEnabled: remoteAccess.Enabled,
                ApiEnabled: false,
                Protocols: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                Modules: modules,
                RemoteAccessEnabled: remoteAccess.Enabled,
                RemoteAccessUnlimited: remoteAccess.UnlimitedClients,
                MaxRemoteClients: remoteAccess.UnlimitedClients ? null : remoteAccess.MaxClients,
                RemoteAccessExpiresAtUtc: remoteAccess.Enabled ? remoteExpiry : null),
            $"{payload.Issuer} • Ed25519 • {file.KeyId}",
            SiteName: null,
            AssignedUserId: payload.Account.UserId,
            AssignedUserName: payload.Account.DisplayName,
            AssignedUserEmail: payload.Account.Email,
            Organization: payload.OrganizationName,
            PortalRole: payload.Account.PortalRole,
            IndustrialRole: null,
            BillingPeriod: payload.Subscription.BillingPeriod,
            ValidFrom: payload.Subscription.ValidFromUtc,
            GracePeriodDays: payload.Subscription.GracePeriodDays,
            LegacyUnlimitedTagCapacity: entitlementService.IsLegacyUnlimitedTagCapacity(payload),
            PricingVersion: payload.Subscription.PricingVersion,
            LicenseType: payload.LicenseType);
    }

    private static LicenseSnapshot Invalid(string status) => new(
        "INVALID",
        "UNLICENSED",
        "NONE",
        status,
        false,
        null,
        null,
        new LicenseEntitlements(
            CapacityLimit.Limited(0), CapacityLimit.Limited(0), CapacityLimit.Limited(0), CapacityLimit.Limited(0),
            false, false, false, new HashSet<string>(), new HashSet<string>(),
            false, false, 0, null),
        "Local license file",
        GracePeriodDays: LicenseEntitlementService.GracePeriodDays,
        PricingVersion: "UNKNOWN");
}
