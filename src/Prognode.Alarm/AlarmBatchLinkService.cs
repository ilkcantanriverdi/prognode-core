namespace Prognode.Alarm;

/// <summary>
/// Persists a many-to-many Batch -> alarm occurrence link for alarms that were already
/// active when a Batch started. This complements the direct BatchId captured when an
/// alarm first becomes active and correctly handles one long-running alarm spanning
/// more than one sequential Batch/Lot.
/// </summary>
public sealed class AlarmBatchLinkService(
    AlarmRuntimeStore runtime,
    IAlarmEventRepository events)
{
    public async Task LinkCurrentActiveAsync(
        Guid batchId,
        CancellationToken cancellationToken = default)
    {
        foreach (var alarm in runtime.GetAllActive())
        {
            await events.LinkOccurrenceToBatchAsync(
                alarm.AlarmKey,
                alarm.ActiveSince,
                batchId,
                cancellationToken);
        }
    }
}
