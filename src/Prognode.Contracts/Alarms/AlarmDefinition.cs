namespace Prognode.Contracts.Alarms;

public enum AlarmPriority
{
    Low,
    Medium,
    High,
    Critical
}

public enum AlarmNotificationMode
{
    NotifyOnce,
    RepeatUntilAcknowledged
}

public enum AlarmCondition
{
    DigitalEquals,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual
}

public sealed record AlarmDefinition(
    Guid Id,
    Guid TagId,
    string Text,
    AlarmPriority Priority,
    int? BitIndex,
    bool TriggerValue,
    AlarmCondition Condition,
    double? Threshold,
    double Deadband,
    int DelayOnMs,
    int DelayOffMs,
    bool NotifyOnActive,
    bool NotifyOnCleared,
    AlarmNotificationMode NotificationMode,
    int RepeatIntervalSeconds,
    bool ContinueAfterClearUntilAcknowledged,
    bool Enabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    bool RequiresAcknowledgement = true
);
