using System.Text;
using Prognode.Contracts.RemoteAccess;
using Prognode.Core.Connectivity;
using Prognode.Licensing;

namespace Prognode.RemoteAccess;

public sealed class RemoteAccessService(
    LicenseService license,
    LocalLicenseStore localLicenseStore,
    ServerAccessService serverAccess,
    RemoteAccessCloudClient cloud,
    RemoteAccessStateStore store)
{
    private readonly SemaphoreSlim _syncGate = new(1, 1);

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
                ServerBound: cache.ServerBound && !string.IsNullOrWhiteSpace(cache.ServerAccessToken),
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
            ServerBound: cache.ServerBound && !string.IsNullOrWhiteSpace(cache.ServerAccessToken),
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

            var cache = store.Load();
            if (string.IsNullOrWhiteSpace(cache.ServerAccessToken))
                return Failure("SERVER_NOT_BOUND", "Bind this PROGNODE Server to the Remote Access subscription first.");

            try
            {
                var remote = await cloud.GetStatusAsync(
                    cache.ServerAccessToken,
                    serverAccess.Identity.ServerId,
                    cancellationToken);
                SaveCloudStatus(remote, null, cache.ServerAccessToken);
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

        var current = license.Current;
        try
        {
            var signedLicense = Encoding.UTF8.GetString(localLicenseStore.ReadAllBytes());
            var result = await cloud.BindServerAsync(
                current.LicenseId,
                signedLicense,
                serverAccess.Identity.ServerId,
                serverAccess.Identity.DisplayName,
                cancellationToken);

            if (!result.Success)
            {
                if (result.Status is not null)
                    SaveCloudStatus(result.Status, result.Message, null);
                else
                    SaveError(result.Message);
                return Failure(result.Code, result.Message);
            }

            if (string.IsNullOrWhiteSpace(result.ServerAccessToken))
                return Failure("INVALID_CLOUD_CONTRACT", "Cloud binding succeeded without a server access token.");

            if (result.Status is not null)
                SaveCloudStatus(result.Status, null, result.ServerAccessToken);
            else
            {
                var cache = store.Load();
                cache.ServerBound = true;
                cache.ServerAccessToken = result.ServerAccessToken;
                cache.BindingStatus = "BOUND";
                cache.SubscriptionStatus = "UNKNOWN";
                cache.LastSyncedAtUtc = DateTimeOffset.UtcNow;
                cache.LastError = null;
                store.Save(cache);
            }

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

    public async Task<RemoteAccessOperationResult> RegisterClientAsync(
        Guid localClientId,
        string? devicePublicKey,
        string? platform,
        CancellationToken cancellationToken = default)
    {
        var preflight = Preflight(requireCloud: true);
        if (preflight is not null)
            return preflight;

        var local = serverAccess.GetClient(localClientId);
        if (local is null)
            return Failure("CLIENT_NOT_FOUND", "The paired LAN client was not found.");

        if (local.RemoteEnabled && local.RemoteClientId is not null)
            return Success("ALREADY_REGISTERED", "This client already has Remote Access enabled.");

        var status = GetStatus();
        if (!status.ServerBound)
            return Failure("SERVER_NOT_BOUND", "Bind this PROGNODE Server to Remote Access before enabling a remote client.");
        if (status.CloudConfigured && status.SubscriptionStatus is not ("ACTIVE" or "PENDING_CLOUD" or "UNKNOWN"))
            return Failure("REMOTE_ACCESS_INACTIVE", $"Remote Access cloud status is {status.SubscriptionStatus}.");
        if (!status.UnlimitedClients && status.RemainingClients is <= 0)
            return Failure("REMOTE_CLIENT_LIMIT_REACHED", "Remote client limit reached.");

        var key = string.IsNullOrWhiteSpace(devicePublicKey) ? local.DevicePublicKey : devicePublicKey.Trim();
        var clientPlatform = string.IsNullOrWhiteSpace(platform) ? local.Platform : platform.Trim();
        if (string.IsNullOrWhiteSpace(key))
            return Failure("DEVICE_IDENTITY_REQUIRED", "A device public key is required before Remote Access can be enabled.");

        var cache = store.Load();
        if (string.IsNullOrWhiteSpace(cache.ServerAccessToken))
            return Failure("SERVER_NOT_BOUND", "Remote Access server credential is missing. Bind the Server again.");

        var current = license.Current;
        try
        {
            var result = await cloud.RegisterClientAsync(
                cache.ServerAccessToken,
                serverAccess.Identity.ServerId,
                local.ClientId,
                local.Name,
                clientPlatform,
                key,
                current.AssignedUserId,
                current.AssignedUserName,
                cancellationToken);

            if (!result.Success || result.RemoteClientId is null)
            {
                if (result.Status is not null)
                    SaveCloudStatus(result.Status, result.Message, cache.ServerAccessToken);
                else
                    SaveError(result.Message);
                return Failure(result.Code, result.Message);
            }

            serverAccess.SetRemoteRegistration(local.ClientId, result.RemoteClientId.Value, key, clientPlatform);
            if (result.Status is not null)
                SaveCloudStatus(result.Status, null, cache.ServerAccessToken);
            else
                await SyncAsync(cancellationToken);

            return Success(result.Code, string.IsNullOrWhiteSpace(result.Message)
                ? "Remote Access enabled for this client. One remote seat is now in use."
                : result.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        {
            SaveError(ex.Message);
            return Failure("CLOUD_UNAVAILABLE", ex.Message);
        }
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

        var cache = store.Load();
        if (string.IsNullOrWhiteSpace(cache.ServerAccessToken))
            return Failure("SERVER_NOT_BOUND", "Remote Access server credential is missing. The remote seat was not released.");

        try
        {
            var result = await cloud.RevokeClientAsync(
                cache.ServerAccessToken,
                serverAccess.Identity.ServerId,
                local.RemoteClientId.Value,
                cancellationToken);

            if (!result.Success)
            {
                if (result.Status is not null)
                    SaveCloudStatus(result.Status, result.Message, cache.ServerAccessToken);
                else
                    SaveError(result.Message);
                return Failure(result.Code, result.Message);
            }

            serverAccess.ClearRemoteRegistration(local.ClientId);
            if (result.Status is not null)
                SaveCloudStatus(result.Status, null, cache.ServerAccessToken);
            else
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

    public async Task<RemoteAccessOperationResult> RegisterPushTokenAsync(
        Guid localClientId,string platform,string pushToken,CancellationToken cancellationToken=default)
    {
        var preflight=Preflight(requireCloud:true);
        if(preflight is not null) return preflight;
        var status=GetStatus();
        if(!status.ServerBound || !string.Equals(status.SubscriptionStatus,"ACTIVE",StringComparison.OrdinalIgnoreCase))
            return Failure("REMOTE_ACCESS_INACTIVE","Remote Access is not active or the server is not bound.");
        var local=serverAccess.GetClient(localClientId);
        if(local is null || !local.RemoteEnabled || local.RemoteClientId is null)
            return Failure("REMOTE_SEAT_REQUIRED","Enable remote access for this paired device first.");
        if(platform is not ("ANDROID_FCM" or "IOS_APNS") ||
            string.IsNullOrWhiteSpace(pushToken) || pushToken.Length is < 32 or > 4096)
            return Failure("INVALID_PUSH_TOKEN","Platform or push token is invalid.");
        var token=store.Load().ServerAccessToken;
        if(string.IsNullOrWhiteSpace(token)) return Failure("SERVER_NOT_BOUND","Server credentials not available.");
        try
        {
            var result=await cloud.RegisterPushTokenAsync(token,serverAccess.Identity.ServerId,
                localClientId,local.RemoteClientId.Value,platform,pushToken,cancellationToken);
            return result.Success ? Success("PUSH_TOKEN_REGISTERED","Push token sent to relay.") :
                Failure(result.Code,result.Message);
        }
        catch(Exception ex) when(ex is HttpRequestException or InvalidOperationException or TaskCanceledException)
        { SaveError(ex.Message);return Failure("CLOUD_UNAVAILABLE",ex.Message); }
    }

    public async Task<bool> TryPublishNotificationAsync(
        Prognode.Contracts.Notifications.NotificationEvent notification,
        CancellationToken cancellationToken = default)
    {
        var status = GetStatus();
        if (!status.Entitled || !status.CloudConfigured || !status.ServerBound ||
            !string.Equals(status.SubscriptionStatus, "ACTIVE", StringComparison.OrdinalIgnoreCase))
            return false;

        var token = store.Load().ServerAccessToken;
        if (string.IsNullOrWhiteSpace(token))
            return false;

        try
        {
            return await cloud.PublishNotificationAsync(
                token,
                serverAccess.Identity.ServerId,
                notification,
                cancellationToken);
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

        var token = store.Load().ServerAccessToken;
        if (string.IsNullOrWhiteSpace(token))
            return [];

        try
        {
            return await cloud.GetCommandsAsync(token, serverAccess.Identity.ServerId, cancellationToken);
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
        var token = store.Load().ServerAccessToken;
        if (string.IsNullOrWhiteSpace(token) || !cloud.IsConfigured)
            return false;

        try
        {
            return await cloud.CompleteCommandAsync(
                token,
                serverAccess.Identity.ServerId,
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
        return null;
    }

    private void SaveCloudStatus(
        RemoteAccessCloudStatusResponse remote,
        string? error,
        string? serverAccessToken)
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

        var existing = store.Load();
        store.Save(new RemoteAccessCacheDocument
        {
            ServerBound = remote.ServerBound,
            ServerAccessToken = string.IsNullOrWhiteSpace(serverAccessToken)
                ? existing.ServerAccessToken
                : serverAccessToken,
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
