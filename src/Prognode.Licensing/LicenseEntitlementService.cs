using Prognode.Contracts.Licensing;

namespace Prognode.Licensing;

public sealed class LicenseEntitlementService
{
    public const int GracePeriodDays = 7;
    public const int ExpiringSoonDays = 7;

    private readonly Func<DateTimeOffset> _clock;

    /// <param name="clock">Lifecycle clock; production passes <see cref="TrustedClock.UtcNow"/> so a
    /// clock set back cannot extend a license. Defaults to the system clock.</param>
    public LicenseEntitlementService(Func<DateTimeOffset>? clock = null) =>
        _clock = clock ?? (() => DateTimeOffset.UtcNow);

    /// <summary>Lifecycle time for a signed license: never earlier than its signed issue time.</summary>
    private DateTimeOffset LifecycleNow(LicensePayloadV2 payload)
    {
        var instant = _clock();
        return instant < payload.IssuedAtUtc ? payload.IssuedAtUtc : instant;
    }

    public IReadOnlySet<string> ValidateAndGetModules(LicensePayloadV2 payload, bool rejectExpired)
    {
        var status = GetLicenseStatus(payload);
        if (rejectExpired && status is "EXPIRED" or "INVALID")
            throw new InvalidOperationException(status == "EXPIRED"
                ? "This PROGNODE license is outside its signed validity and grace period."
                : "This PROGNODE license is not currently valid.");

        var product = payload.Subscription.Product;
        var e = payload.Entitlements;

        var expectedAlarm = product is "ALARM_MONITORING" or "ALARM_HISTORIAN";
        var expectedHistorian = product is "HISTORIAN" or "ALARM_HISTORIAN";

        if (!expectedAlarm && !expectedHistorian)
            throw new InvalidOperationException($"Unsupported PROGNODE product '{product}'.");

        if (e.AlarmMonitoring != expectedAlarm || e.Historian != expectedHistorian)
            throw new InvalidOperationException("Signed product and module entitlement flags do not match.");

        if (string.Equals(payload.LicenseType, "TRIAL", StringComparison.OrdinalIgnoreCase))
        {
            if (payload.PaymentRequired || payload.Subscription.BillingPeriod != "TRIAL" ||
                payload.Subscription.Product != "ALARM_HISTORIAN" ||
                payload.Subscription.ExpiresAtUtc != payload.Subscription.ValidFromUtc.AddDays(14) ||
                payload.Subscription.GraceUntilUtc != payload.Subscription.ExpiresAtUtc ||
                payload.Entitlements.MaxTags != LicenseSignatureVerifier.TrialMaxTags || payload.Subscription.MaxTags != LicenseSignatureVerifier.TrialMaxTags ||
                payload.Entitlements.UnlimitedTags || payload.Subscription.UnlimitedTags ||
                !string.Equals(payload.Entitlements.Devices, "1", StringComparison.Ordinal))
                throw new InvalidOperationException("Signed TRIAL capacity and duration must be 1 device, 20 Tags and 14 days.");
        }

        // Device count is deliberately not a commercial capacity dimension in V1.8.
        if (!string.Equals(payload.LicenseType, "TRIAL", StringComparison.OrdinalIgnoreCase) &&
            !string.IsNullOrWhiteSpace(e.Devices) &&
            !string.Equals(e.Devices, "unlimited", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("PROGNODE V1.8 device capacity must not be limited by the commercial license.");

        ValidateTagCapacity(payload);

        // Legacy capacity claims remain accepted for signed v2 files. Alarm/Historian
        // reuse existing process Tags and never consume another licensed Tag.
        if (!string.IsNullOrWhiteSpace(e.Alarms))
        {
            var expected = expectedAlarm ? "unlimited" : "not_entitled";
            if (!string.Equals(e.Alarms, expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Signed alarm entitlement does not match the selected product.");
        }

        if (!string.IsNullOrWhiteSpace(e.HistorianSamples))
        {
            var expected = expectedHistorian ? "unlimited" : "not_entitled";
            if (!string.Equals(e.HistorianSamples, expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Signed Historian entitlement does not match the selected product.");
        }

        ValidateRemoteAccess(payload);

        var modules = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (expectedAlarm) modules.Add("ALARM");
        if (expectedHistorian) modules.Add("HISTORIAN");
        return modules;
    }

    public CapacityLimit GetTagCapacity(LicensePayloadV2 payload)
    {
        ValidateTagCapacity(payload);
        return payload.Entitlements.UnlimitedTags
            ? CapacityLimit.Unlimited
            : CapacityLimit.Limited(payload.Entitlements.MaxTags!.Value);
    }

    public bool IsLegacyUnlimitedTagCapacity(LicensePayloadV2 payload) =>
        payload.Subscription.IsLegacyV17 && payload.Entitlements.UnlimitedTags;

    public string GetLicenseStatus(LicensePayloadV2 payload, DateTimeOffset? now = null)
    {
        var instant = now ?? LifecycleNow(payload);
        var subscription = payload.Subscription;

        var isTrial = string.Equals(payload.LicenseType, "TRIAL", StringComparison.OrdinalIgnoreCase);
        if (subscription.ExpiresAtUtc <= subscription.ValidFromUtc ||
            (isTrial ? subscription.GraceUntilUtc != subscription.ExpiresAtUtc :
                subscription.GraceUntilUtc <= subscription.ExpiresAtUtc))
            return "INVALID";

        if (instant < subscription.ValidFromUtc)
            return "INVALID";

        var expiringSoonAt = subscription.ExpiresAtUtc.AddDays(-ExpiringSoonDays);
        if (instant < expiringSoonAt)
            return "ACTIVE";
        if (instant < subscription.ExpiresAtUtc)
            return "EXPIRING_SOON";
        if (!isTrial && instant <= subscription.GraceUntilUtc)
            return "GRACE";
        return "EXPIRED";
    }

    public bool IsLocalOperational(LicensePayloadV2 payload, DateTimeOffset? now = null) =>
        GetLicenseStatus(payload, now) is "ACTIVE" or "EXPIRING_SOON" or "GRACE";

    public LicenseRemoteAccessClaimV1 GetRemoteAccess(LicensePayloadV2 payload)
    {
        ValidateRemoteAccess(payload);
        return payload.Entitlements.RemoteAccess ?? new LicenseRemoteAccessClaimV1(false, 0, false, null);
    }

    public bool IsRemoteAccessOperational(LicensePayloadV2 payload, DateTimeOffset? now = null)
    {
        var remote = GetRemoteAccess(payload);
        if (!remote.Enabled)
            return false;

        var instant = now ?? LifecycleNow(payload);
        var remoteExpiry = remote.ExpiresAtUtc ?? payload.Subscription.ExpiresAtUtc;
        return instant < remoteExpiry && GetLicenseStatus(payload, instant) is not ("EXPIRED" or "INVALID");
    }

    private static void ValidateTagCapacity(LicensePayloadV2 payload)
    {
        var s = payload.Subscription;
        var e = payload.Entitlements;

        if (s.UnlimitedTags != e.UnlimitedTags)
            throw new InvalidOperationException("subscription.maxTags and entitlements.maxTags must describe the same capacity.");

        if (s.UnlimitedTags)
        {
            if (!s.IsLegacyV17)
                throw new InvalidOperationException("Web V1.8 does not offer unlimited Tag capacity; use a signed numeric maxTags value.");
            if (s.MaxTags is not null || e.MaxTags is not null)
                throw new InvalidOperationException("Unlimited tag capacity cannot also contain a numeric maxTags value.");
            return;
        }

        if (s.MaxTags is null || e.MaxTags is null || s.MaxTags.Value != e.MaxTags.Value)
            throw new InvalidOperationException("subscription.maxTags and entitlements.maxTags must match.");

        if (payload.IsManualOrInternal)
        {
            if (s.MaxTags.Value is < 1 or > 1_000_000)
                throw new InvalidOperationException("Manual/Internal PROGNODE maxTags must be between 1 and 1,000,000.");
            return;
        }

        if (s.MaxTags.Value is not (100 or 250 or 500))
            throw new InvalidOperationException("Commercial PROGNODE V1.8 maxTags must be 100, 250 or 500.");
    }

    private static void ValidateRemoteAccess(LicensePayloadV2 payload)
    {
        var remote = payload.Entitlements.RemoteAccess;
        if (remote is null || !remote.Enabled)
            return;

        if (remote.UnlimitedClients)
        {
            if (!payload.Subscription.IsLegacyV17)
                throw new InvalidOperationException("Web V1.8 Remote Access does not offer unlimited client capacity; use 5, 10 or 25 clients.");
            if (remote.MaxClients is not null)
                throw new InvalidOperationException("Remote Access entitlement cannot set both unlimitedClients and maxClients.");
        }
        else if (string.Equals(payload.LicenseType, "TRIAL", StringComparison.OrdinalIgnoreCase))
        {
            if (remote.MaxClients != LicenseSignatureVerifier.TrialRemoteClients)
                throw new InvalidOperationException("TRIAL Remote Access maxClients must be 2.");
        }
        else if (remote.MaxClients is not (5 or 10 or 25))
        {
            throw new InvalidOperationException("Remote Access maxClients must be 5, 10 or 25.");
        }

        var remoteExpiry = remote.ExpiresAtUtc ?? payload.Subscription.ExpiresAtUtc;
        if (remoteExpiry <= payload.Subscription.ValidFromUtc)
            throw new InvalidOperationException("Remote Access expiresAtUtc must be later than the license valid-from time.");
    }
}
