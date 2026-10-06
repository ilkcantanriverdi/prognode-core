using Prognode.Contracts.Alarms;
using Prognode.Contracts.Tags;
using Prognode.Core.Tags;
using Prognode.Notifications;

namespace Prognode.Alarm;

public enum OccurrenceAckResult { Acknowledged, StaleOccurrence, NotFound }

public sealed record PendingMobileAlert(Guid OccurrenceId,string Profile,int ProfileRevision,
    int ReminderIntervalSec,int RepeatAudioWindowMin,DateTimeOffset ActiveAtUtc,
    string Title,string Message,string SourceName,string Severity);

public sealed class AlarmService(
    IAlarmDefinitionRepository definitions,
    IAlarmEventRepository events,
    ITagRepository tags,
    AlarmRuntimeStore runtime,
    NotificationEventStore notifications)
{
    public Task<IReadOnlyList<AlarmDefinition>> GetDefinitionsAsync(
        CancellationToken cancellationToken = default) =>
        definitions.GetAllAsync(cancellationToken);

    public Task<int> CountAsync(
        CancellationToken cancellationToken = default) =>
        definitions.CountAsync(cancellationToken);

    public IReadOnlyList<AlarmRuntimeSnapshot> GetActive() =>
        runtime.GetAllActive();

    public async Task<IReadOnlyList<PendingMobileAlert>> GetPendingMobileAlertsAsync(
        CancellationToken cancellationToken=default)
    {
        var all=await definitions.GetAllAsync(cancellationToken);
        var byId=all.ToDictionary(x=>x.Id);
        var occurrences=runtime.GetAllActive()
            .Concat(runtime.GetAllPendingAcknowledgement()).ToArray();
        var result=new List<PendingMobileAlert>();
        foreach(var occurrence in occurrences)
        {
            if(occurrence.OccurrenceId==Guid.Empty ||
                occurrence.State==AlarmRuntimeState.Acknowledged ||
                occurrence.DefinitionId is not Guid definitionId ||
                !byId.TryGetValue(definitionId,out var rule) ||
                !rule.Enabled || !rule.NotifyOnActive ||
                !rule.RequiresAcknowledgement ||
                rule.NotificationMode!=AlarmNotificationMode.RepeatUntilAcknowledged)
                continue;
            var pendingAfterClear=runtime.GetPendingAcknowledgement(occurrence.AlarmKey) is not null;
            if(pendingAfterClear && !rule.ContinueAfterClearUntilAcknowledged) continue;
            result.Add(new PendingMobileAlert(occurrence.OccurrenceId,"ACK_REMINDER",1,
                rule.RepeatIntervalSeconds,10,occurrence.ActiveSince,
                $"{rule.Priority} · ACK required",rule.Text,occurrence.SourceName,
                rule.Priority.ToString()));
        }
        return result;
    }

    public Task<IReadOnlyList<AlarmOccurrenceRecord>> GetHistoryAsync(
        int limit,
        CancellationToken cancellationToken = default) =>
        events.GetRecentOccurrencesAsync(
            Math.Clamp(limit, 1, 500),
            cancellationToken);

    public Task<long> CountHistoryAsync(CancellationToken cancellationToken = default) =>
        events.CountOccurrencesAsync(cancellationToken);

    public Task<IReadOnlyList<AlarmOccurrenceRecord>> GetHistoryPageAsync(
        int offset, int limit, string? search = null, string? priority = null,
        CancellationToken cancellationToken = default) =>
        events.GetOccurrencePageAsync(offset, limit, search, priority, cancellationToken);

    public Task<long> CountMatchingHistoryAsync(string? search, string? priority,
        CancellationToken cancellationToken = default) =>
        events.CountMatchingOccurrencesAsync(search, priority, cancellationToken);

    public Task<int> DeleteHistoryOccurrencesAsync(IReadOnlyCollection<Guid> occurrenceIds,
        CancellationToken cancellationToken = default) =>
        events.DeleteOccurrencesAsync(occurrenceIds, cancellationToken);

    public Task<IReadOnlyList<AlarmOccurrenceRecord>> GetHistoryByBatchAsync(
        Guid batchId,
        int limit,
        CancellationToken cancellationToken = default) =>
        events.GetOccurrencesByBatchAsync(
            batchId,
            Math.Clamp(limit, 1, 500),
            cancellationToken);

    public async Task<AlarmDefinition> CreateAsync(
        Guid tagId,
        string? text,
        AlarmPriority priority,
        int? bitIndex,
        bool triggerValue,
        AlarmCondition condition,
        double? threshold,
        double deadband,
        int delayOnMs,
        int delayOffMs,
        bool notifyOnActive,
        bool notifyOnCleared,
        AlarmNotificationMode notificationMode,
        int repeatIntervalSeconds,
        bool continueAfterClearUntilAcknowledged,
        CancellationToken cancellationToken = default,
        bool? requiresAcknowledgement = null)
    {
        var now = DateTimeOffset.UtcNow;

        var definition = await BuildValidatedAsync(
            Guid.NewGuid(),
            tagId,
            text,
            priority,
            bitIndex,
            triggerValue,
            condition,
            threshold,
            deadband,
            delayOnMs,
            delayOffMs,
            notifyOnActive,
            notifyOnCleared,
            notificationMode,
            repeatIntervalSeconds,
            continueAfterClearUntilAcknowledged,
            now,
            cancellationToken,
            requiresAcknowledgement);

        await definitions.AddAsync(definition, cancellationToken);
        return definition;
    }

    public async Task<AlarmDefinition> UpdateAsync(
        Guid id,
        Guid tagId,
        string? text,
        AlarmPriority priority,
        int? bitIndex,
        bool triggerValue,
        AlarmCondition condition,
        double? threshold,
        double deadband,
        int delayOnMs,
        int delayOffMs,
        bool notifyOnActive,
        bool notifyOnCleared,
        AlarmNotificationMode notificationMode,
        int repeatIntervalSeconds,
        bool continueAfterClearUntilAcknowledged,
        CancellationToken cancellationToken = default,
        bool? requiresAcknowledgement = null)
    {
        var existing = await definitions.GetByIdAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException("Alarm definition was not found.");

        var definition = await BuildValidatedAsync(
            id,
            tagId,
            text,
            priority,
            bitIndex,
            triggerValue,
            condition,
            threshold,
            deadband,
            delayOnMs,
            delayOffMs,
            notifyOnActive,
            notifyOnCleared,
            notificationMode,
            repeatIntervalSeconds,
            continueAfterClearUntilAcknowledged,
            existing.CreatedAt,
            cancellationToken,
            requiresAcknowledgement);

        var tag = await tags.GetByIdAsync(tagId, cancellationToken)
            ?? throw new ArgumentException("Source Tag was not found.");
        await definitions.UpdateAsync(definition, cancellationToken);
        runtime.ApplyDefinitionUpdate(definition, tag);
        return definition;
    }

    public async Task<bool> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var existing = await definitions.GetByIdAsync(id, cancellationToken);
        if (existing is null)
            return false;

        await definitions.DeleteAsync(id, cancellationToken);
        var removed=runtime.Remove(KeyFor(id));
        if(removed is not null)
            await events.AddAsync(ToEvent(removed,AlarmEventType.Cleared),cancellationToken);
        return true;
    }

    public async Task<bool> AcknowledgeAsync(
        string alarmKey,
        CancellationToken cancellationToken = default)
    {
        var snapshot = runtime.Acknowledge(alarmKey);
        if (snapshot is null)
            return false;

        notifications.Publish("Information","Alarm acknowledged",snapshot.Text,snapshot.AlarmKey,
            occurrenceId: snapshot.OccurrenceId == Guid.Empty ? null : snapshot.OccurrenceId,
            eventType:"ACK",sourceName:snapshot.SourceName,activeAtUtc:snapshot.ActiveSince,
            alarmEvent:ToEvent(snapshot,AlarmEventType.Acknowledged));
        return true;
    }

    public async Task<OccurrenceAckResult> AcknowledgeOccurrenceAsync(
        Guid occurrenceId, CancellationToken cancellationToken=default)
    {
        if (occurrenceId==Guid.Empty) return OccurrenceAckResult.NotFound;
        var current=runtime.AcknowledgeOccurrence(occurrenceId,out var alreadyAcknowledged);
        if(current is null)
        {
            if(notifications.WasAcknowledgedWithoutNewActivation(occurrenceId))
                return OccurrenceAckResult.Acknowledged;
            return notifications.KnowsOccurrence(occurrenceId)
                ? OccurrenceAckResult.StaleOccurrence : OccurrenceAckResult.NotFound;
        }
        if (alreadyAcknowledged) return OccurrenceAckResult.Acknowledged;
        notifications.Publish("Information","Alarm acknowledged",current.Text,current.AlarmKey,
            occurrenceId:occurrenceId,eventType:"ACK",sourceName:current.SourceName,
            activeAtUtc:current.ActiveSince,
            alarmEvent:ToEvent(current,AlarmEventType.Acknowledged));
        return OccurrenceAckResult.Acknowledged;
    }

    public static string KeyFor(Guid definitionId) =>
        $"alarm:{definitionId:N}";

    public static AlarmEventRecord ToEvent(
        AlarmRuntimeSnapshot snapshot,
        AlarmEventType eventType) =>
        new(
            Id: 0,
            AlarmKey: snapshot.AlarmKey,
            DefinitionId: snapshot.DefinitionId,
            TagId: snapshot.TagId,
            DeviceId: snapshot.DeviceId,
            IsSystem: snapshot.IsSystem,
            SourceName: snapshot.SourceName,
            Text: snapshot.Text,
            Priority: snapshot.Priority,
            EventType: eventType,
            Timestamp: eventType == AlarmEventType.Active
                ? snapshot.ActiveSince
                : DateTimeOffset.UtcNow,
            BatchId: snapshot.BatchId,
            OccurrenceId: snapshot.OccurrenceId);

    private async Task<AlarmDefinition> BuildValidatedAsync(
        Guid id,
        Guid tagId,
        string? text,
        AlarmPriority priority,
        int? bitIndex,
        bool triggerValue,
        AlarmCondition condition,
        double? threshold,
        double deadband,
        int delayOnMs,
        int delayOffMs,
        bool notifyOnActive,
        bool notifyOnCleared,
        AlarmNotificationMode notificationMode,
        int repeatIntervalSeconds,
        bool continueAfterClearUntilAcknowledged,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken,
        bool? requiresAcknowledgement)
    {
        var tag = await tags.GetByIdAsync(tagId, cancellationToken)
            ?? throw new ArgumentException("Source Tag was not found.");

        var numeric = tag.DataType is
            TagDataType.UInt16 or
            TagDataType.Int16 or
            TagDataType.UInt32 or
            TagDataType.Int32 or
            TagDataType.Float32;

        if (condition == AlarmCondition.DigitalEquals)
        {
            if (tag.DataType is not (TagDataType.Bool or TagDataType.Word))
                throw new ArgumentException("DigitalEquals alarm source must be a BOOL or WORD Tag.");

            if (tag.DataType == TagDataType.Word)
            {
                if (bitIndex is null or < 0 or > 15)
                    throw new ArgumentException("WORD alarm bit must be between 0 and 15.");
            }
            else
            {
                bitIndex = null;
            }

            threshold = null;
            deadband = 0;
        }
        else
        {
            if (!numeric)
                throw new ArgumentException("Numeric alarm source must be UInt16, Int16, UInt32, Int32 or Float32.");

            if (threshold is null || double.IsNaN(threshold.Value) || double.IsInfinity(threshold.Value))
                throw new ArgumentException("Numeric alarm Threshold must be a valid number.");

            if (double.IsNaN(deadband) || double.IsInfinity(deadband) || deadband < 0)
                throw new ArgumentException("Numeric alarm Deadband must be a finite value greater than or equal to zero.");

            bitIndex = null;
        }

        var cleanText = (text ?? string.Empty).Trim();
        if (cleanText.Length < 1)
            throw new ArgumentException("Alarm text is required.");
        if (cleanText.Length > 240)
            throw new ArgumentException("Alarm text cannot exceed 240 characters.");

        delayOnMs = Math.Clamp(delayOnMs, 0, 3_600_000);
        delayOffMs = Math.Clamp(delayOffMs, 0, 3_600_000);
        repeatIntervalSeconds = Math.Clamp(repeatIntervalSeconds, 15, 86_400);

        return new AlarmDefinition(
            Id: id,
            TagId: tagId,
            Text: cleanText,
            Priority: priority,
            BitIndex: bitIndex,
            TriggerValue: triggerValue,
            Condition: condition,
            Threshold: threshold,
            Deadband: deadband,
            DelayOnMs: delayOnMs,
            DelayOffMs: delayOffMs,
            NotifyOnActive: notifyOnActive,
            NotifyOnCleared: notifyOnCleared,
            NotificationMode: notificationMode,
            RepeatIntervalSeconds: repeatIntervalSeconds,
            ContinueAfterClearUntilAcknowledged: continueAfterClearUntilAcknowledged,
            Enabled: true,
            CreatedAt: createdAt,
            UpdatedAt: DateTimeOffset.UtcNow,
            RequiresAcknowledgement: requiresAcknowledgement ??
                notificationMode == AlarmNotificationMode.RepeatUntilAcknowledged);
    }
}
