namespace Prognode.Contracts.Alarms;

public enum AlarmRuntimeState
{
    Active,
    Acknowledged
}

public sealed record AlarmRuntimeSnapshot(
    string AlarmKey,
    Guid? DefinitionId,
    Guid? TagId,
    Guid? DeviceId,
    bool IsSystem,
    string SourceName,
    string Text,
    AlarmPriority Priority,
    AlarmRuntimeState State,
    DateTimeOffset ActiveSince,
    DateTimeOffset LastChangedAt,
    Guid? BatchId = null,
    Guid OccurrenceId = default,
    DateTimeOffset? AcknowledgedAt = null,
    string? AcknowledgedBy = null,
    bool? RequiresAcknowledgement = null
);
