namespace Prognode.Contracts.Notifications;

public sealed record NotificationEvent(
    long Id,
    string Severity,
    string Title,
    string Message,
    DateTimeOffset Timestamp,
    string? AlarmKey = null,
    bool RequiresAcknowledgement = false,
    int RepeatSequence = 0,
    Guid? OccurrenceId = null,
    string EventType = "INFO",
    string? SourceName = null,
    DateTimeOffset? ActiveAtUtc = null,
    DateTimeOffset? ClearedAtUtc = null
);
