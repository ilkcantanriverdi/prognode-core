using Prognode.Contracts.Alarms;

namespace Prognode.Web.Models;

public sealed record CreateOrUpdateAlarmRequest(
    Guid TagId,
    string? Text,
    AlarmPriority Priority,
    int? BitIndex,
    bool TriggerValue,
    int DelayOnMs,
    int DelayOffMs,
    bool NotifyOnActive,
    bool NotifyOnCleared,
    AlarmNotificationMode NotificationMode = AlarmNotificationMode.NotifyOnce,
    int RepeatIntervalSeconds = 60,
    bool ContinueAfterClearUntilAcknowledged = false,
    AlarmCondition Condition = AlarmCondition.DigitalEquals,
    double? Threshold = null,
    double Deadband = 0,
    bool? RequiresAcknowledgement = null
);

public sealed record AcknowledgeAlarmRequest(string? AlarmKey);
