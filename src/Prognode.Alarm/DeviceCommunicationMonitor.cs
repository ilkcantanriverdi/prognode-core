using System.Collections.Concurrent;
using Prognode.Contracts.Alarms;
using Prognode.Contracts.Devices;
using Prognode.Notifications;
using Prognode.Core.Batches;

namespace Prognode.Alarm;

public sealed class DeviceCommunicationMonitor(
    AlarmRuntimeStore runtime,
    IAlarmEventRepository events,
    NotificationEventStore notifications,
    BatchService batches)
{
    private const int FailureThreshold = 3;
    private readonly ConcurrentDictionary<Guid, CommunicationState> _states = new();

    public async Task ReportAsync(
        DeviceDefinition device,
        DeviceHealthResult result,
        CancellationToken cancellationToken)
    {
        var state = _states.GetOrAdd(device.Id, _ => new CommunicationState());

        Transition transition;

        lock (state.Gate)
        {
            if (result.Success)
            {
                state.ConsecutiveFailures = 0;

                if (state.IsAlarmActive)
                {
                    state.IsAlarmActive = false;
                    transition = Transition.Clear;
                }
                else
                {
                    transition = Transition.None;
                }
            }
            else
            {
                state.ConsecutiveFailures++;
                state.LastError = result.Message;

                if (!state.IsAlarmActive &&
                    state.ConsecutiveFailures >= FailureThreshold)
                {
                    state.IsAlarmActive = true;
                    transition = Transition.Activate;
                }
                else
                {
                    transition = Transition.None;
                }
            }
        }

        if (transition == Transition.Activate)
            await ActivateAsync(device, state.LastError, cancellationToken);
        else if (transition == Transition.Clear)
            await ClearAsync(device, cancellationToken);
    }


    public void PruneMissingDevices(IReadOnlySet<Guid> existingDeviceIds)
    {
        foreach (var deviceId in _states.Keys)
        {
            if (existingDeviceIds.Contains(deviceId))
                continue;

            _states.TryRemove(deviceId, out _);
            runtime.Remove(KeyFor(deviceId));
        }
    }

    private async Task ActivateAsync(
        DeviceDefinition device,
        string? error,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var key = KeyFor(device.Id);
        var currentBatch = await batches.GetCurrentAsync(cancellationToken);

        var snapshot = new AlarmRuntimeSnapshot(
            AlarmKey: key,
            DefinitionId: null,
            TagId: null,
            DeviceId: device.Id,
            IsSystem: true,
            SourceName: device.Name,
            Text: "Communication lost",
            Priority: AlarmPriority.Critical,
            State: AlarmRuntimeState.Active,
            ActiveSince: now,
            LastChangedAt: now,
            BatchId: currentBatch?.Id,OccurrenceId:Guid.NewGuid());

        runtime.SetActive(snapshot);

        notifications.Publish(
            "Critical",
            "Communication",
            $"{device.Name} • Communication lost",alarmKey:snapshot.AlarmKey,
            occurrenceId:snapshot.OccurrenceId,eventType:"ACTIVE",
            sourceName:device.Name,activeAtUtc:now,
            alarmEvent:AlarmService.ToEvent(snapshot,AlarmEventType.Active));

        if (currentBatch is not null)
        {
            await events.LinkOccurrenceToBatchAsync(
                snapshot.AlarmKey,
                snapshot.ActiveSince,
                currentBatch.Id,
                cancellationToken);
        }
    }

    private async Task ClearAsync(
        DeviceDefinition device,
        CancellationToken cancellationToken)
    {
        var removed = runtime.Remove(KeyFor(device.Id));

        notifications.Publish(
            "Information",
            "Communication",
            $"{device.Name} • Communication restored",alarmKey:removed?.AlarmKey,
            occurrenceId:removed?.OccurrenceId,eventType:"CLEARED",sourceName:device.Name,
            activeAtUtc:removed?.ActiveSince,clearedAtUtc:DateTimeOffset.UtcNow,
            alarmEvent:removed is null? null:AlarmService.ToEvent(removed,AlarmEventType.Cleared));
    }

    public static string KeyFor(Guid deviceId) =>
        $"system:communication:{deviceId:N}";

    private enum Transition
    {
        None,
        Activate,
        Clear
    }

    private sealed class CommunicationState
    {
        public object Gate { get; } = new();
        public int ConsecutiveFailures { get; set; }
        public bool IsAlarmActive { get; set; }
        public string? LastError { get; set; }
    }
}
