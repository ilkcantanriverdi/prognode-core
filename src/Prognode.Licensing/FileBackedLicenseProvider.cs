using Prognode.Contracts.Licensing;

namespace Prognode.Licensing;

public sealed class FileBackedLicenseProvider(
    LocalLicenseStore store,
    LicenseSignatureVerifier verifier,
    LicenseEntitlementService entitlementService,
    LicenseActivationService? activation = null) : ILicenseProvider
{
    /// <summary>Status of a valid, signed license that has no activation for this Core and machine.</summary>
    public const string ActivationRequiredStatus = "ACTIVATION_REQUIRED";
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

    /// <summary>Activation of the installed license on this Core; null when no valid license is installed.</summary>
    public LicenseActivationState? GetActivation()
    {
        if (activation is null || !store.Exists)
            return null;
        try
        {
            var payload = GetVerifiedCurrent().Payload;
            return activation.Evaluate(payload.LicenseId, payload.LicenseKey);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>Verifies and stores an activation certificate for the installed license.</summary>
    public ActivationCertificateV1 ImportActivation(byte[] certificateBytes)
    {
        if (activation is null)
            throw new InvalidOperationException("Activation is not configured on this Core.");
        var payload = GetVerifiedCurrent().Payload;
        return activation.Import(certificateBytes, payload.LicenseId, payload.LicenseKey);
    }

    public void ClearActivation() => activation?.Clear();

    public ActivationRequestV1 CreateActivationRequest(string serverName, string coreVersion)
    {
        if (activation is null)
            throw new InvalidOperationException("Activation is not configured on this Core.");
        var payload = GetVerifiedCurrent().Payload;
        return activation.CreateRequest(payload.LicenseId, payload.LicenseKey, serverName, coreVersion);
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
        // A signed license only runs on the Core and machine it was activated for.
        if (localOperational && activation is not null &&
            !activation.Evaluate(payload.LicenseId, payload.LicenseKey).Activated)
        {
            status = ActivationRequiredStatus;
            localOperational = false;
        }
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
