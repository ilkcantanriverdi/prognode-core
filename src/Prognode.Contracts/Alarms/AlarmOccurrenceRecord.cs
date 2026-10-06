namespace Prognode.Contracts.Alarms;

public sealed record AlarmOccurrenceRecord(
    string AlarmKey,
    Guid? DefinitionId,
    Guid? TagId,
    Guid? DeviceId,
    bool IsSystem,
    string SourceName,
    string Text,
    AlarmPriority Priority,
    DateTimeOffset ActiveAt,
    DateTimeOffset? AcknowledgedAt,
    DateTimeOffset? ClearedAt,
    string State,
    Guid? BatchId = null,
    Guid OccurrenceId = default,
    string? AcknowledgedBy = null
);
