namespace Prognode.Contracts.Alarms;

public enum AlarmEventType
{
    Active,
    Acknowledged,
    Cleared
}

public sealed record AlarmEventRecord(
    long Id,
    string AlarmKey,
    Guid? DefinitionId,
    Guid? TagId,
    Guid? DeviceId,
    bool IsSystem,
    string SourceName,
    string Text,
    AlarmPriority Priority,
    AlarmEventType EventType,
    DateTimeOffset Timestamp,
    Guid? BatchId = null,
    Guid OccurrenceId = default,
    // Who acknowledged (Acknowledged events only): Core user or paired mobile device.
    string? AcknowledgedBy = null
);
