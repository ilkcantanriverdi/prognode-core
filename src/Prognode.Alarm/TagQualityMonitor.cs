using Prognode.Contracts.Alarms;
using Prognode.Contracts.Tags;
using Prognode.Core.Batches;
using Prognode.Core.Tags;
using Prognode.Notifications;

namespace Prognode.Alarm;

/// <summary>
/// System alarm for alarmed Tags whose value is not Good for a sustained period (review K2).
/// The process alarm engine deliberately freezes on BAD/STALE data; without this monitor a
/// Tag-level failure (Modbus exception, S7 rejected DB, stale MQTT topic) on a device that still
/// answers health checks would freeze the process alarm silently. One occurrence per Tag, separate
/// from the process alarm, suppressed while the whole device is already in Communication lost.
/// </summary>
public sealed class TagQualityMonitor(
    CurrentTagValueStore values,
    AlarmRuntimeStore runtime,
    NotificationEventStore notifications,
    BatchService batches)
{
    public static readonly TimeSpan BadQualityDelay = TimeSpan.FromSeconds(30);

    private readonly Dictionary<Guid, DateTimeOffset> _notGoodSince = new();
    private readonly object _gate = new();

    public async Task EvaluateAsync(
        IReadOnlyList<AlarmDefinition> definitions,
        IReadOnlyDictionary<Guid, TagDefinition> tagsById,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var watched = definitions
            .Where(x => x.Enabled && tagsById.ContainsKey(x.TagId))
            .Select(x => x.TagId)
            .ToHashSet();

        // Tags without an enabled alarm definition are no longer watched: close their alarm.
        foreach (var active in runtime.GetAllActive()
                     .Where(x => x.IsSystem && x.TagId is { } id && x.AlarmKey == KeyFor(id) && !watched.Contains(id))
                     .ToArray())
            Clear(active, now, "no longer monitored");

        foreach (var tagId in watched)
        {
            var tag = tagsById[tagId];
            var snapshot = values.Get(tagId);
            var key = KeyFor(tagId);
            var active = runtime.Get(key);

            if (snapshot is { Quality: TagQuality.Good })
            {
                lock (_gate) _notGoodSince.Remove(tagId);
                if (active is not null) Clear(active, now, "restored");
                continue;
            }

            DateTimeOffset since;
            lock (_gate)
            {
                if (!_notGoodSince.TryGetValue(tagId, out since))
                    _notGoodSince[tagId] = since = now;
            }

            if (active is not null || now - since < BadQualityDelay)
                continue;

            // The device-level Communication lost alarm already covers every Tag on that device.
            if (runtime.Get(DeviceCommunicationMonitor.KeyFor(tag.DeviceId)) is not null)
                continue;

            await ActivateAsync(tag, snapshot, now, cancellationToken);
        }

        lock (_gate)
            foreach (var stale in _notGoodSince.Keys.Where(x => !watched.Contains(x)).ToArray())
                _notGoodSince.Remove(stale);
    }

    private async Task ActivateAsync(
        TagDefinition tag,
        TagValueSnapshot? snapshot,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var currentBatch = await batches.GetCurrentAsync(cancellationToken);
        var alarm = new AlarmRuntimeSnapshot(
            AlarmKey: KeyFor(tag.Id),
            DefinitionId: null,
            TagId: tag.Id,
            DeviceId: tag.DeviceId,
            IsSystem: true,
            SourceName: tag.Name,
            Text: DescribeQuality(snapshot),
            Priority: AlarmPriority.High,
            State: AlarmRuntimeState.Active,
            ActiveSince: now,
            LastChangedAt: now,
            BatchId: currentBatch?.Id,
            OccurrenceId: Guid.NewGuid(),
            RequiresAcknowledgement: false);

        runtime.SetActive(alarm);
        notifications.Publish(
            "High",
            "Signal quality",
            $"{tag.Name} • {alarm.Text}",
            alarmKey: alarm.AlarmKey,
            occurrenceId: alarm.OccurrenceId,
            eventType: "ACTIVE",
            sourceName: tag.Name,
            activeAtUtc: now,
            alarmEvent: AlarmService.ToEvent(alarm, AlarmEventType.Active));
    }

    private void Clear(AlarmRuntimeSnapshot active, DateTimeOffset now, string reason)
    {
        var removed = runtime.Remove(active.AlarmKey);
        if (removed is null) return;
        notifications.Publish(
            "Information",
            "Signal quality",
            $"{removed.SourceName} • Signal quality {reason}",
            alarmKey: removed.AlarmKey,
            occurrenceId: removed.OccurrenceId,
            eventType: "CLEARED",
            sourceName: removed.SourceName,
            activeAtUtc: removed.ActiveSince,
            clearedAtUtc: now,
            alarmEvent: AlarmService.ToEvent(removed, AlarmEventType.Cleared));
    }

    internal static string DescribeQuality(TagValueSnapshot? snapshot)
    {
        if (snapshot is null) return "Signal quality bad: no value received";
        var text = $"Signal quality bad: {snapshot.Quality.ToString().ToUpperInvariant()}";
        if (string.IsNullOrWhiteSpace(snapshot.Error)) return text;
        var error = snapshot.Error.Trim();
        return $"{text} · {(error.Length > 120 ? error[..120] + "…" : error)}";
    }

    public static string KeyFor(Guid tagId) => $"system:signal-quality:{tagId:N}";
}
