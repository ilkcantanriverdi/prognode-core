using System.Collections.Concurrent;
using Prognode.Contracts.Alarms;
using Prognode.Contracts.Tags;
using Prognode.Core.Tags;
using Prognode.Core.Batches;
using Prognode.Notifications;

namespace Prognode.Alarm;

public sealed class AlarmEngine(
    CurrentTagValueStore values,
    IAlarmEventRepository events,
    AlarmRuntimeStore runtime,
    NotificationEventStore notifications,
    BatchService batches)
{
    private readonly ConcurrentDictionary<Guid, EvaluationState> _states = new();

    public async Task EvaluateAsync(
        IReadOnlyList<AlarmDefinition> definitions,
        IReadOnlyDictionary<Guid, TagDefinition> tagsById,
        IReadOnlyDictionary<Guid, string> deviceNamesById,
        CancellationToken cancellationToken)
    {
        var enabledIds = new HashSet<Guid>(
            definitions
                .Where(x => x.Enabled)
                .Select(x => x.Id));

        foreach (var stale in _states.Keys
            .Where(x => !enabledIds.Contains(x))
            .ToArray())
        {
            _states.TryRemove(stale, out _);
            runtime.Remove(
                AlarmService.KeyFor(stale));
        }

        foreach (var definition in definitions)
        {
            if (!definition.Enabled)
                continue;

            if (!tagsById.TryGetValue(
                definition.TagId,
                out var tag))
            {
                continue;
            }

            var snapshot =
                values.Get(tag.Id);

            // BAD quality is owned by the communication/system alarm.
            // Never clear a process alarm from stale/invalid data.
            if (snapshot is null ||
                snapshot.Quality != TagQuality.Good ||
                snapshot.RawValue is null ||
                (definition.Condition != AlarmCondition.DigitalEquals && snapshot.Value is null))
            {
                // Invalid process data must not contribute time to DelayOn/DelayOff. Preserve the
                // current ACTIVE/CLEARED state, but require a fresh continuous valid interval once
                // quality returns. Communication/System alarms own the link/quality failure.
                if (_states.TryGetValue(definition.Id, out var invalidState))
                {
                    invalidState.ActivePendingSince = null;
                    invalidState.ClearPendingSince = null;
                }

                continue;
            }

            var state =
                _states.GetOrAdd(
                    definition.Id,
                    _ =>
                    {
                        var key=AlarmService.KeyFor(definition.Id);
                        var active=runtime.Get(key);
                        var pending=runtime.GetPendingAcknowledgement(key);
                        var saved=active ?? pending;
                        var cycle=saved is not null && saved.OccurrenceId!=Guid.Empty
                            ? notifications.LastReminderCycle(saved.OccurrenceId)
                            : (Sequence:0,LastAtUtc:(DateTimeOffset?)null);
                        var lastNotificationAt = cycle.LastAtUtc;
                        if (saved is not null &&
                            (lastNotificationAt is null || saved.LastChangedAt > lastNotificationAt.Value))
                            lastNotificationAt = saved.LastChangedAt;
                        return new EvaluationState {
                            IsActive=active is not null,
                            IsClearedAwaitingAcknowledgement=pending is not null,
                            LastNotificationAt=lastNotificationAt,
                            NotificationSequence=cycle.Sequence
                        };
                    });

            var runtimeSnapshot = runtime.Get(AlarmService.KeyFor(definition.Id));
            if (runtimeSnapshot is not null &&
                runtimeSnapshot.RequiresAcknowledgement != definition.RequiresAcknowledgement)
                runtime.SetActive(runtimeSnapshot with
                { RequiresAcknowledgement = definition.RequiresAcknowledgement });

            var condition =
                EvaluateCondition(
                    definition,
                    tag,
                    snapshot,
                    state.IsActive);

            var deviceName =
                deviceNamesById.TryGetValue(
                    tag.DeviceId,
                    out var name)
                    ? name
                    : "Unknown Device";

            await AdvanceAsync(
                definition,
                tag,
                deviceName,
                condition,
                state,
                cancellationToken);
        }
    }

    private async Task AdvanceAsync(
        AlarmDefinition definition,
        TagDefinition tag,
        string deviceName,
        bool condition,
        EvaluationState state,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var alarmKey = AlarmService.KeyFor(definition.Id);

        if (state.IsClearedAwaitingAcknowledgement)
        {
            if (condition)
            {
                // A new activation supersedes the old cleared reminder cycle.
                runtime.RemovePendingAcknowledgement(alarmKey);
                state.IsClearedAwaitingAcknowledgement = false;
                state.LastNotificationAt = null;
                state.NotificationSequence = 0;
            }
            else
            {
                var pending = runtime.GetPendingAcknowledgement(alarmKey);
                if (pending is null)
                {
                    state.IsClearedAwaitingAcknowledgement = false;
                    state.LastNotificationAt = null;
                    state.NotificationSequence = 0;
                    return;
                }

                if (state.LastNotificationAt is null ||
                    (now - state.LastNotificationAt.Value).TotalSeconds >= definition.RepeatIntervalSeconds)
                {
                    state.NotificationSequence++;
                    state.LastNotificationAt = now;
                    notifications.Publish(
                        SeverityFor(definition.Priority),
                        $"{definition.Priority.ToString().ToUpperInvariant()} Alarm · CLEARED · ACK required",
                        $"{deviceName} • {definition.Text}",
                        pending.AlarmKey,
                        requiresAcknowledgement: true,
                        repeatSequence: state.NotificationSequence,
                        occurrenceId:pending.OccurrenceId,eventType:"REMINDER",
                        sourceName:pending.SourceName,activeAtUtc:pending.ActiveSince);
                }

                return;
            }
        }

        if (state.IsActive &&
            runtime.Get(
                AlarmService.KeyFor(
                    definition.Id)) is null)
        {
            state.IsActive = false;
            state.ActivePendingSince = null;
            state.ClearPendingSince = null;
        }

        if (condition)
        {
            state.ClearPendingSince = null;

            if (state.IsActive)
            {
                var current = runtime.Get(AlarmService.KeyFor(definition.Id));
                if (current?.State == AlarmRuntimeState.Acknowledged &&
                    (state.LastNotificationAt is null || current.LastChangedAt > state.LastNotificationAt.Value))
                    state.LastNotificationAt = current.LastChangedAt;

                if (definition.NotifyOnActive &&
                    definition.NotificationMode == AlarmNotificationMode.RepeatUntilAcknowledged &&
                    current is not null &&
                    (state.LastNotificationAt is null ||
                     (now - state.LastNotificationAt.Value).TotalSeconds >= definition.RepeatIntervalSeconds))
                {
                    state.NotificationSequence++;
                    state.LastNotificationAt = now;
                    var requiresAck = definition.RequiresAcknowledgement &&
                        current.State != AlarmRuntimeState.Acknowledged;
                    notifications.Publish(
                        SeverityFor(definition.Priority),
                        requiresAck
                            ? $"{definition.Priority.ToString().ToUpperInvariant()} Alarm · ACK required"
                            : $"{definition.Priority.ToString().ToUpperInvariant()} Alarm · still active",
                        $"{deviceName} • {definition.Text}",
                        current.AlarmKey,
                        requiresAcknowledgement: requiresAck,
                        repeatSequence: state.NotificationSequence,
                        occurrenceId:current.OccurrenceId,eventType:"REMINDER",
                        sourceName:current.SourceName,activeAtUtc:current.ActiveSince);
                }

                return;
            }

            state.ActivePendingSince ??= now;

            if ((now -
                 state.ActivePendingSince.Value)
                .TotalMilliseconds <
                definition.DelayOnMs)
            {
                return;
            }

            state.ActivePendingSince = null;
            state.IsActive = true;

            var currentBatch = await batches.GetCurrentAsync(cancellationToken);

            var active =
                new AlarmRuntimeSnapshot(
                    AlarmKey:
                        AlarmService.KeyFor(
                            definition.Id),
                    DefinitionId: definition.Id,
                    TagId: tag.Id,
                    DeviceId: tag.DeviceId,
                    IsSystem: false,
                    SourceName: tag.Name,
                    Text: definition.Text,
                    Priority: definition.Priority,
                    State: AlarmRuntimeState.Active,
                    ActiveSince: now,
                    LastChangedAt: now,
                    BatchId: currentBatch?.Id,
                    OccurrenceId: Guid.NewGuid(),
                    RequiresAcknowledgement: definition.RequiresAcknowledgement);

            runtime.SetActive(active);

            // For notifying alarms the ACTIVE event is written in the same SQLite
            // transaction as the notification journal and remote outbox. No cloud
            // dependency participates in the process alarm runtime.
            if (!definition.NotifyOnActive)
                await events.AddAsync(AlarmService.ToEvent(active,AlarmEventType.Active),cancellationToken);

            if (definition.NotifyOnActive)
            {
                state.NotificationSequence = 1;
                state.LastNotificationAt = now;
                var requiresAck = definition.RequiresAcknowledgement;
                notifications.Publish(
                    SeverityFor(definition.Priority),
                    requiresAck
                        ? $"{definition.Priority.ToString().ToUpperInvariant()} Alarm · ACK required"
                        : $"{definition.Priority.ToString().ToUpperInvariant()} Alarm",
                    $"{deviceName} • {definition.Text}",
                    active.AlarmKey,
                    requiresAcknowledgement: requiresAck,
                    repeatSequence:1,occurrenceId:active.OccurrenceId,
                    eventType:"ACTIVE",sourceName:active.SourceName,activeAtUtc:active.ActiveSince,
                    alarmEvent:AlarmService.ToEvent(active,AlarmEventType.Active));
            }
            if (currentBatch is not null)
                await events.LinkOccurrenceToBatchAsync(active.AlarmKey,active.ActiveSince,
                    currentBatch.Id,cancellationToken);

            return;
        }

        state.ActivePendingSince = null;

        if (!state.IsActive)
            return;

        state.ClearPendingSince ??= now;

        if ((now -
             state.ClearPendingSince.Value)
            .TotalMilliseconds <
            definition.DelayOffMs)
        {
            return;
        }

        state.ClearPendingSince = null;
        state.IsActive = false;

        var keepPendingAcknowledgement =
            definition.RequiresAcknowledgement &&
            definition.NotificationMode == AlarmNotificationMode.RepeatUntilAcknowledged &&
            definition.ContinueAfterClearUntilAcknowledged;

        var removed = runtime.Clear(alarmKey, keepPendingAcknowledgement);

        if (removed is null)
            return;

        if(!definition.NotifyOnCleared)
            await events.AddAsync(AlarmService.ToEvent(removed,AlarmEventType.Cleared),cancellationToken);

        if (definition.NotifyOnCleared)
        {
            notifications.Publish(
                "Information",
                "Alarm Cleared",
                $"{deviceName} • {definition.Text}",alarmKey:removed.AlarmKey,
                occurrenceId:removed.OccurrenceId,eventType:"CLEARED",
                sourceName:removed.SourceName,activeAtUtc:removed.ActiveSince,clearedAtUtc:now,
                alarmEvent:AlarmService.ToEvent(removed,AlarmEventType.Cleared));
        }

        if (keepPendingAcknowledgement && removed.State != AlarmRuntimeState.Acknowledged)
        {
            state.IsClearedAwaitingAcknowledgement = true;
            // Keep the existing reminder clock so the next reminder follows the configured interval.
        }
        else
        {
            state.IsClearedAwaitingAcknowledgement = false;
            state.LastNotificationAt = null;
            state.NotificationSequence = 0;
        }
    }

    private static bool EvaluateCondition(
        AlarmDefinition definition,
        TagDefinition tag,
        TagValueSnapshot snapshot,
        bool currentlyActive)
    {
        if (definition.Condition == AlarmCondition.DigitalEquals)
        {
            var rawValue = snapshot.RawValue;
            if (!rawValue.HasValue)
                return false;

            bool value;
            if (tag.DataType == TagDataType.Bool)
            {
                value = rawValue.Value != 0;
            }
            else if (tag.DataType == TagDataType.Word)
            {
                var word = (ushort)Math.Clamp(
                    Math.Round(rawValue.Value),
                    ushort.MinValue,
                    ushort.MaxValue);

                var bit = definition.BitIndex ?? 0;
                value = (word & (1 << bit)) != 0;
            }
            else
            {
                return false;
            }

            return value == definition.TriggerValue;
        }

        if (!snapshot.Value.HasValue || !definition.Threshold.HasValue)
            return false;

        var valueNow = snapshot.Value.Value;
        var threshold = definition.Threshold.Value;
        var deadband = Math.Max(0, definition.Deadband);

        if (!currentlyActive || deadband <= 0)
        {
            return definition.Condition switch
            {
                AlarmCondition.GreaterThan => valueNow > threshold,
                AlarmCondition.GreaterThanOrEqual => valueNow >= threshold,
                AlarmCondition.LessThan => valueNow < threshold,
                AlarmCondition.LessThanOrEqual => valueNow <= threshold,
                _ => false
            };
        }

        // While active, hysteresis moves only the clear boundary.
        // Example: GTE 80 with deadband 2 remains active above 78 and clears at <= 78.
        return definition.Condition switch
        {
            AlarmCondition.GreaterThan or AlarmCondition.GreaterThanOrEqual =>
                valueNow > threshold - deadband,
            AlarmCondition.LessThan or AlarmCondition.LessThanOrEqual =>
                valueNow < threshold + deadband,
            _ => false
        };
    }

    private static string SeverityFor(
        AlarmPriority priority) =>
        priority switch
        {
            AlarmPriority.Critical => "Critical",
            AlarmPriority.High => "High",
            AlarmPriority.Medium => "Medium",
            _ => "Low"
        };

    private sealed class EvaluationState
    {
        public bool IsActive { get; set; }
        public DateTimeOffset? ActivePendingSince { get; set; }
        public DateTimeOffset? ClearPendingSince { get; set; }
        public DateTimeOffset? LastNotificationAt { get; set; }
        public int NotificationSequence { get; set; }
        public bool IsClearedAwaitingAcknowledgement { get; set; }
    }
}
