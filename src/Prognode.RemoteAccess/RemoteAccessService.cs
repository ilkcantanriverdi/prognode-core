using Prognode.Contracts.RemoteAccess;
using Prognode.Core.Connectivity;
using Prognode.Licensing;

namespace Prognode.RemoteAccess;

public sealed class RemoteAccessService(
    LicenseService license,
    ServerAccessService serverAccess,
    RemoteAccessCloudClient cloud,
    RemoteAccessStateStore store,
    CoreCloudLicenseStateStore cloudLicense)
{
    private readonly SemaphoreSlim _syncGate = new(1, 1);

    /// <summary>Core authenticates to the Remote Access relay with its license activation token.</summary>
    private string? CloudToken() => cloudLicense.Load(configured: true).ActivationToken;

    public RemoteAccessStatusSnapshot GetStatus()
    {
        var localLicense = license.Current;
        var entitlement = localLicense.Entitlements;
        var cache = store.Load();

        if (!localLicense.IsValid || !entitlement.RemoteAccessEnabled)
        {
            return new RemoteAccessStatusSnapshot(
                Entitled: false,
                CloudConfigured: cloud.IsConfigured,
                ServerBound: false,
                BindingStatus: "NOT_ENTITLED",
                SubscriptionStatus: localLicense.IsValid ? "NOT_PURCHASED" : localLicense.Status,
                UnlimitedClients: false,
                MaxClients: 0,
                UsedClients: 0,
                RemainingClients: 0,
                ExpiresAtUtc: entitlement.RemoteAccessExpiresAtUtc,
                LastSyncedAtUtc: cache.LastSyncedAtUtc,
                LastError: null);
        }

        var signedRemoteExpiry = entitlement.RemoteAccessExpiresAtUtc ?? localLicense.ExpiresAt;
        var signedRemoteExpired = signedRemoteExpiry is not null && DateTimeOffset.UtcNow >= signedRemoteExpiry.Value;
        if (signedRemoteExpired)
        {
            return new RemoteAccessStatusSnapshot(
                Entitled: true,
                CloudConfigured: cloud.IsConfigured,
                ServerBound: cache.ServerBound && !string.IsNullOrWhiteSpace(CloudToken()),
                BindingStatus: cache.ServerBound ? cache.BindingStatus : "NOT_BOUND",
                SubscriptionStatus: "EXPIRED",
                UnlimitedClients: entitlement.RemoteAccessUnlimited,
                MaxClients: entitlement.RemoteAccessUnlimited ? null : entitlement.MaxRemoteClients,
                UsedClients: Math.Max(0, cache.UsedClients),
                RemainingClients: 0,
                ExpiresAtUtc: signedRemoteExpiry,
                LastSyncedAtUtc: cache.LastSyncedAtUtc,
                LastError: cache.LastError);
        }

        var hasCloudSnapshot = cache.LastSyncedAtUtc is not null;
        var unlimited = entitlement.RemoteAccessUnlimited && (!hasCloudSnapshot || cache.UnlimitedClients);
        var signedMax = entitlement.RemoteAccessUnlimited ? null : entitlement.MaxRemoteClients;
        // Cloud may further REDUCE seats but must never overrule signed entitlement.
        var max = unlimited ? null : (signedMax.HasValue && cache.MaxClients.HasValue && cache.MaxClients.Value > 0
            ? Math.Min(signedMax.Value,cache.MaxClients.Value) : signedMax);
        var used = Math.Max(0, cache.UsedClients);
        int? remaining = unlimited || max is null ? null : Math.Max(0, max.Value - used);
        var effectiveExpiry = signedRemoteExpiry;
        if (cache.ExpiresAtUtc is not null && (effectiveExpiry is null || cache.ExpiresAtUtc.Value < effectiveExpiry.Value))
            effectiveExpiry = cache.ExpiresAtUtc;

        return new RemoteAccessStatusSnapshot(
            Entitled: true,
            CloudConfigured: cloud.IsConfigured,
            ServerBound: cache.ServerBound && !string.IsNullOrWhiteSpace(CloudToken()),
            BindingStatus: cloud.IsConfigured ? cache.BindingStatus : "CLOUD_NOT_CONFIGURED",
            SubscriptionStatus: cloud.IsConfigured ? cache.SubscriptionStatus : "PENDING_CLOUD",
            UnlimitedClients: unlimited,
            MaxClients: max,
            UsedClients: used,
            RemainingClients: remaining,
            ExpiresAtUtc: effectiveExpiry ?? localLicense.ExpiresAt,
            LastSyncedAtUtc: cache.LastSyncedAtUtc,
            LastError: cache.LastError);
    }

    public IReadOnlyList<RemoteAccessClientSnapshot> GetClients()
    {
        var cache = store.Load();
        var cloudByRemoteId = cache.Clients.ToDictionary(x => x.RemoteClientId, x => x);

        return serverAccess.GetClients()
            .Select(local =>
            {
                RemoteAccessCloudClientDocument? remote = null;
                if (local.RemoteClientId is Guid remoteId)
                    cloudByRemoteId.TryGetValue(remoteId, out remote);

                return new RemoteAccessClientSnapshot(
                    local.ClientId,
                    local.RemoteClientId,
                    local.Name,
                    local.Platform,
                    remote?.UserId,
                    remote?.UserDisplayName,
                    local.DevicePublicKey,
                    local.RemoteEnabled,
                    remote?.Status ?? (local.RemoteEnabled ? "ACTIVE" : "LAN_ONLY"),
                    local.CreatedAtUtc,
                    local.LastSeenAtUtc,
                    local.RemoteRegisteredAtUtc,
                    remote?.LastSeenAtUtc);
            })
            .OrderByDescending(x => x.RemoteEnabled)
            .ThenBy(x => x.DeviceName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    public async Task<RemoteAccessOperationResult> SyncAsync(CancellationToken cancellationToken = default)
    {
        await _syncGate.WaitAsync(cancellationToken);
        try
        {
            var preflight = Preflight(requireCloud: true);
            if (preflight is not null)
                return preflight;

            try
            {
                var remote = await cloud.GetStatusAsync(CloudToken()!, cancellationToken);
                SaveCloudStatus(remote, null);
                return Success("SYNCED", "Remote Access status synchronized.");
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
            {
                SaveError(ex.Message);
                return Failure("CLOUD_UNAVAILABLE", ex.Message);
            }
        }
        finally
        {
            _syncGate.Release();
        }
    }

    public async Task<RemoteAccessOperationResult> BindServerAsync(CancellationToken cancellationToken = default)
    {
        var preflight = Preflight(requireCloud: true);
        if (preflight is not null)
            return preflight;
        try
        {
            var result = await cloud.BindServerAsync(CloudToken()!, cancellationToken);
            if (!result.Success)
            {
                SaveError(result.Message);
                return Failure(result.Code, result.Message);
            }
            if (result.Status is not null)
                SaveCloudStatus(result.Status, null);
            return Success(result.Code, string.IsNullOrWhiteSpace(result.Message)
                ? "Remote Access subscription is bound to this PROGNODE Server."
                : result.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            SaveError(ex.Message);
            return Failure("CLOUD_UNAVAILABLE", ex.Message);
        }
    }

    /// <summary>
    /// Step 1 of device registration: a one-time challenge from PROGNODE Cloud that the paired device
    /// signs with its Ed25519 key, proving it holds the key being registered.
    /// </summary>
    public async Task<(RemoteAccessOperationResult Result, RemoteAccessChallenge? Challenge)> CreateRegistrationChallengeAsync(
        Guid localClientId,
        CancellationToken cancellationToken = default)
    {
        var ready = RegistrationReady(localClientId, out _);
        if (ready is not null)
            return (ready, null);
        try
        {
            var challenge = await cloud.CreateChallengeAsync(CloudToken()!, cancellationToken);
            return (Success("CHALLENGE_ISSUED", "Sign the challenge with the device key to finish Remote Access registration."), challenge);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            SaveError(ex.Message);
            return (Failure("CLOUD_UNAVAILABLE", ex.Message), null);
        }
    }

    /// <summary>
    /// Step 2: registers the paired device with its signed challenge. The returned device token lets
    /// the device reach PROGNODE Cloud off-site and is handed only to the paired device itself.
    /// Registering the same device key again issues a fresh token (for example after reinstalling).
    /// </summary>
    public async Task<RemoteAccessOperationResult> RegisterClientAsync(
        Guid localClientId,
        string? devicePublicKey,
        string? platform,
        Guid challengeId,
        string? deviceSignature,
        CancellationToken cancellationToken = default)
    {
        var ready = RegistrationReady(localClientId, out var local);
        if (ready is not null)
            return ready;
        var key = string.IsNullOrWhiteSpace(devicePublicKey) ? local!.DevicePublicKey : devicePublicKey.Trim();
        var clientPlatform = string.IsNullOrWhiteSpace(platform) ? local!.Platform : platform.Trim();
        if (string.IsNullOrWhiteSpace(key))
            return Failure("DEVICE_IDENTITY_REQUIRED", "A device public key is required before Remote Access can be enabled.");
        if (challengeId == Guid.Empty || string.IsNullOrWhiteSpace(deviceSignature))
            return Failure("DEVICE_PROOF_REQUIRED", "Sign the Remote Access challenge with the device key.");

        try
        {
            var result = await cloud.RegisterClientAsync(
                CloudToken()!,
                local!.Name,
                clientPlatform,
                key,
                challengeId,
                deviceSignature.Trim(),
                license.Current.AssignedUserId,
                cancellationToken);
            if (!result.Success || result.RemoteClientId is null)
            {
                SaveError(result.Message);
                return Failure(result.Code, result.Message);
            }

            serverAccess.SetRemoteRegistration(local.ClientId, result.RemoteClientId.Value, key, clientPlatform);
            await SyncAsync(cancellationToken);
            return Success(result.Code, string.IsNullOrWhiteSpace(result.Message)
                ? "Remote Access enabled for this device. One remote seat is now in use."
                : result.Message) with
            {
                RemoteClientId = result.RemoteClientId,
                RemoteClientToken = result.RemoteClientToken,
                RemoteClientTokenExpiresAtUtc = result.RemoteClientTokenExpiresAtUtc,
            };
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            SaveError(ex.Message);
            return Failure("CLOUD_UNAVAILABLE", ex.Message);
        }
    }

    private RemoteAccessOperationResult? RegistrationReady(Guid localClientId, out PairedClientSnapshot? local)
    {
        local = null;
        var preflight = Preflight(requireCloud: true);
        if (preflight is not null)
            return preflight;
        local = serverAccess.GetClient(localClientId);
        if (local is null)
            return Failure("CLIENT_NOT_FOUND", "The paired LAN client was not found.");
        var status = GetStatus();
        if (!status.ServerBound)
            return Failure("SERVER_NOT_BOUND", "Remote Access is not bound to this PROGNODE Server yet. It binds automatically shortly after activation.");
        if (!string.Equals(status.SubscriptionStatus, "ACTIVE", StringComparison.OrdinalIgnoreCase))
            return Failure("REMOTE_ACCESS_INACTIVE", $"Remote Access cloud status is {status.SubscriptionStatus}.");
        // A device that already holds a seat may re-register (new token) without needing a free seat.
        if (!local.RemoteEnabled && !status.UnlimitedClients && status.RemainingClients is <= 0)
            return Failure("REMOTE_CLIENT_LIMIT_REACHED", "All Remote Access seats are in use. Revoke a device or upgrade the add-on.");
        return null;
    }

    public async Task<RemoteAccessOperationResult> RevokeClientAsync(
        Guid localClientId,
        CancellationToken cancellationToken = default)
    {
        // Revocation is cleanup, not entitlement consumption. Allow a previously registered
        // client to release its cloud seat even after REMOTE_ACCESS was removed from a refreshed
        // local license. This avoids orphaned seats and preserves the LAN pairing separately.
        var local = serverAccess.GetClient(localClientId);
        if (local is null)
            return Failure("CLIENT_NOT_FOUND", "The paired client was not found.");

        if (local.RemoteClientId is null)
            return Success("NOT_REMOTE", "This client does not consume a Remote Access seat.");

        if (!cloud.IsConfigured)
            return Failure("CLOUD_NOT_CONFIGURED", "PROGNODE Remote Access cloud endpoint is not configured. The remote seat was not released.");

        var token = CloudToken();
        if (string.IsNullOrWhiteSpace(token))
            return Failure("CORE_NOT_ACTIVATED", "This PROGNODE Core is not activated with PROGNODE Cloud. The remote seat was not released.");

        try
        {
            var result = await cloud.RevokeClientAsync(token, local.RemoteClientId.Value, cancellationToken);
            if (!result.Success)
            {
                SaveError(result.Message);
                return Failure(result.Code, result.Message);
            }

            serverAccess.ClearRemoteRegistration(local.ClientId);
            await SyncAsync(cancellationToken);
            return Success(result.Code, string.IsNullOrWhiteSpace(result.Message)
                ? "Remote Access revoked and the seat was released. LAN pairing remains available."
                : result.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            SaveError(ex.Message);
            return Failure("CLOUD_UNAVAILABLE", ex.Message);
        }
    }

    public async Task<bool> TryPublishNotificationAsync(
        Prognode.Contracts.Notifications.NotificationEvent notification,
        CancellationToken cancellationToken = default)
    {
        var status = GetStatus();
        if (!status.Entitled || !status.CloudConfigured || !status.ServerBound ||
            !string.Equals(status.SubscriptionStatus, "ACTIVE", StringComparison.OrdinalIgnoreCase))
            return false;

        var token = CloudToken();
        if (string.IsNullOrWhiteSpace(token))
            return false;

        try
        {
            return await cloud.PublishNotificationAsync(token, notification, cancellationToken);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            SaveError(ex.Message);
            return false;
        }
    }

    public async Task<IReadOnlyList<RemoteAccessCommand>> GetPendingCommandsAsync(
        CancellationToken cancellationToken = default)
    {
        var status = GetStatus();
        if (!status.Entitled || !status.CloudConfigured || !status.ServerBound ||
            !string.Equals(status.SubscriptionStatus, "ACTIVE", StringComparison.OrdinalIgnoreCase))
            return [];

        var token = CloudToken();
        if (string.IsNullOrWhiteSpace(token))
            return [];

        try
        {
            return await cloud.GetCommandsAsync(token, cancellationToken);
        }
        catch
        {
            return [];
        }
    }

    public bool IsRemoteAckUserAuthorized(string? userId)
    {
        var current = license.Current;
        return current.IsValid &&
               !string.IsNullOrWhiteSpace(current.AssignedUserId) &&
               string.Equals(current.AssignedUserId, userId, StringComparison.Ordinal);
    }

    public async Task<bool> CompleteCommandAsync(
        string commandId,
        bool success,
        string resultCode,
        CancellationToken cancellationToken = default)
    {
        var token = CloudToken();
        if (string.IsNullOrWhiteSpace(token) || !cloud.IsConfigured)
            return false;

        try
        {
            return await cloud.CompleteCommandAsync(
                token,
                commandId,
                success,
                resultCode,
                cancellationToken);
        }
        catch
        {
            return false;
        }
    }

    private RemoteAccessOperationResult? Preflight(bool requireCloud)
    {
        var current = license.Current;
        if (!current.IsValid)
            return Failure("LOCAL_LICENSE_INVALID", "An ACTIVE, EXPIRING_SOON or GRACE signed PROGNODE base license is required.");
        if (!current.Entitlements.RemoteAccessEnabled)
            return Failure("REMOTE_ACCESS_NOT_ENTITLED", "REMOTE_ACCESS is not included in this license.");
        var remoteExpiry = current.Entitlements.RemoteAccessExpiresAtUtc ?? current.ExpiresAt;
        if (remoteExpiry is not null && DateTimeOffset.UtcNow >= remoteExpiry.Value)
            return Failure("REMOTE_ACCESS_EXPIRED", "Remote Access has expired. Base-license grace does not extend the Remote Access add-on.");
        if (requireCloud && !cloud.IsConfigured)
            return Failure("CLOUD_NOT_CONFIGURED", "PROGNODE Remote Access cloud endpoint is not configured yet.");
        if (requireCloud && string.IsNullOrWhiteSpace(CloudToken()))
            return Failure("CORE_NOT_ACTIVATED", "This PROGNODE Core is not activated with PROGNODE Cloud yet.");
        return null;
    }

    private void SaveCloudStatus(
        RemoteAccessCloudStatusResponse remote,
        string? error)
    {
        var current = license.Current;
        var unlimited = current.Entitlements.RemoteAccessUnlimited;
        var signedMax = unlimited ? null : current.Entitlements.MaxRemoteClients;

        var cloudMax = remote.UnlimitedClients ? null : remote.MaxClients;
        int? effectiveMax = unlimited
            ? cloudMax
            : signedMax is null
                ? cloudMax
                : cloudMax is null
                    ? signedMax
                    : Math.Min(signedMax.Value, cloudMax.Value);

        store.Save(new RemoteAccessCacheDocument
        {
            ServerBound = remote.ServerBound,
            ServerAccessToken = null, // the relay is reached with the license activation token
            BindingStatus = remote.BindingStatus,
            SubscriptionStatus = remote.SubscriptionStatus,
            UnlimitedClients = unlimited && remote.UnlimitedClients,
            MaxClients = effectiveMax,
            UsedClients = Math.Max(0, remote.UsedClients),
            ExpiresAtUtc = remote.ExpiresAtUtc,
            LastSyncedAtUtc = DateTimeOffset.UtcNow,
            LastError = error,
            Clients = remote.Clients ?? []
        });
    }

    private void SaveError(string? message)
    {
        var cache = store.Load();
        cache.LastError = string.IsNullOrWhiteSpace(message) ? "Remote Access cloud operation failed." : message;
        store.Save(cache);
    }

    private RemoteAccessOperationResult Success(string code, string message) =>
        new(true, code, message, GetStatus());

    private RemoteAccessOperationResult Failure(string code, string message) =>
        new(false, code, string.IsNullOrWhiteSpace(message) ? code : message, GetStatus());
}
