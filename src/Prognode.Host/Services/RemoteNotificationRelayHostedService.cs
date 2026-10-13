using Prognode.RemoteAccess;
using Prognode.Core.Connectivity;
namespace Prognode.Host.Services;

/// <summary>Retries only eligible rows already persisted in the same DB transaction as events.</summary>
public sealed class RemoteNotificationRelayHostedService(
    RemoteAccessService remoteAccess,
    ServerAccessService delivery,
    RemoteNotificationOutboxStore outbox,
    Prognode.Alarm.AlarmService alarms,
    ILogger<RemoteNotificationRelayHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan ActiveSyncInterval = TimeSpan.FromSeconds(30);
    private DateTimeOffset _lastActiveSync = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var status=remoteAccess.GetStatus();
                if(delivery.NotificationDeliveryEnabled && status.Entitled && status.CloudConfigured && status.ServerBound &&
                   string.Equals(status.SubscriptionStatus,"ACTIVE",StringComparison.OrdinalIgnoreCase))
                {
                    foreach(var pending in outbox.Due())
                    {
                        if(!delivery.NotificationDeliveryEnabled)break;
                        bool accepted=false;
                        try { accepted=await remoteAccess.TryPublishNotificationAsync(pending.Event,stoppingToken); }
                        catch(Exception ex) when(ex is not OperationCanceledException)
                        { logger.LogWarning(ex,"Remote relay temporarily unavailable for event {Id}",pending.Event.Id); }
                        if(accepted) outbox.MarkDelivered(pending.Event.Id);
                        else outbox.MarkFailed(pending.Event.Id);
                    }
                    // Clears and ACKs reach off-site phones even for alarms that do not notify on clear.
                    if(DateTimeOffset.UtcNow-_lastActiveSync>=ActiveSyncInterval)
                    {
                        var takenAt=DateTimeOffset.UtcNow;
                        var active=alarms.GetActive()
                            .Where(a=>a.OccurrenceId!=Guid.Empty)
                            .Select(a=>new RemoteActiveAlarm(a.OccurrenceId.ToString("D"),
                                a.State==Prognode.Contracts.Alarms.AlarmRuntimeState.Acknowledged?"ACKNOWLEDGED":"ACTIVE"))
                            .ToArray();
                        if(await remoteAccess.TrySyncActiveAlarmsAsync(active,takenAt,stoppingToken))
                            _lastActiveSync=takenAt;
                    }
                }
            }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) { break; }
            catch(Exception ex) { logger.LogError(ex,"Remote outbox issue. Alarm monitoring continues; check notification health."); }
            await Task.Delay(TimeSpan.FromSeconds(3),stoppingToken);
        }
    }
}
