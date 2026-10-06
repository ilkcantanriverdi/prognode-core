namespace Prognode.Licensing;

public sealed record LicenseSubscriptionV2(
    string Product,
    string BillingPeriod,
    DateTimeOffset ValidFromUtc,
    DateTimeOffset ExpiresAtUtc,
    DateTimeOffset GraceUntilUtc,
    int? MaxTags,
    bool UnlimitedTags,
    string PricingVersion)
{
    // Compatibility aliases for earlier Core code/contracts.
    public DateTimeOffset ValidUntilUtc => ExpiresAtUtc;
    public int GracePeriodDays => (int)Math.Round((GraceUntilUtc - ExpiresAtUtc).TotalDays);
    public bool IsLegacyV17 => string.Equals(PricingVersion, "LEGACY_V1_7", StringComparison.Ordinal);
}

public sealed record LicenseAccountV2(
    string UserId,
    string Email,
    string DisplayName,
    string PortalRole);

public sealed record LicenseRemoteAccessClaimV1(
    bool Enabled,
    int? MaxClients,
    bool UnlimitedClients = false,
    DateTimeOffset? ExpiresAtUtc = null);

public sealed record LicenseEntitlementClaimsV2(
    bool AlarmMonitoring,
    bool Historian,
    string? Devices,
    string? Tags,
    string? Alarms,
    string? HistorianSamples,
    int? MaxTags = null,
    bool UnlimitedTags = false,
    LicenseRemoteAccessClaimV1? RemoteAccess = null);

public sealed record LicensePayloadV2(
    string Schema,
    string Issuer,
    string LicenseId,
    string LicenseKey,
    int LicenseRevision,
    string OrganizationId,
    string OrganizationName,
    LicenseSubscriptionV2 Subscription,
    LicenseAccountV2 Account,
    OfflineAuthClaimV1 OfflineAuth,
    LicenseEntitlementClaimsV2 Entitlements,
    DateTimeOffset IssuedAtUtc,
    string LicenseType = "CUSTOMER",
    bool PaymentRequired = true)
{
    public bool IsManualOrInternal =>
        string.Equals(LicenseType, "INTERNAL_QA", StringComparison.OrdinalIgnoreCase) ||
        !PaymentRequired;
}
