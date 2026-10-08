namespace Prognode.Contracts.RemoteAccess;

public sealed record RemoteAccessStatusSnapshot(
    bool Entitled,
    bool CloudConfigured,
    bool ServerBound,
    string BindingStatus,
    string SubscriptionStatus,
    bool UnlimitedClients,
    int? MaxClients,
    int UsedClients,
    int? RemainingClients,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset? LastSyncedAtUtc,
    string? LastError);

public sealed record RemoteAccessClientSnapshot(
    Guid LocalClientId,
    Guid? RemoteClientId,
    string DeviceName,
    string Platform,
    string? UserId,
    string? UserDisplayName,
    string? DevicePublicKey,
    bool RemoteEnabled,
    string Status,
    DateTime CreatedAtUtc,
    DateTime LastSeenAtUtc,
    DateTimeOffset? RemoteRegisteredAtUtc,
    DateTimeOffset? CloudLastSeenAtUtc);

public sealed record RemoteAccessOperationResult(
    bool Success,
    string Code,
    string Message,
    RemoteAccessStatusSnapshot Status,
    Guid? RemoteClientId = null,
    string? RemoteClientToken = null,
    DateTimeOffset? RemoteClientTokenExpiresAtUtc = null);
