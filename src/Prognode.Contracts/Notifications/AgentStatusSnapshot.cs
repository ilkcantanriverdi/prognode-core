namespace Prognode.Contracts.Notifications;

public sealed record AgentStatusSnapshot(
    bool IsOnline,
    DateTimeOffset? LastSeenUtc,
    string? MachineName,
    string? Version,
    string? NotificationMode
);
