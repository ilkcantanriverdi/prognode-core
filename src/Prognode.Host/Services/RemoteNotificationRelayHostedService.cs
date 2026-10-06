using Prognode.RemoteAccess;
using Prognode.Core.Connectivity;
namespace Prognode.Host.Services;

/// <summary>Retries only eligible rows already persisted in the same DB transaction as events.</summary>
public sealed class RemoteNotificationRelayHostedService(
    RemoteAccessService remoteAccess,
    ServerAccessService delivery,
    RemoteNotificationOutboxStore outbox,
    ILogger<RemoteNotificationRelayHostedService> logger) : BackgroundService
{
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
                }
            }
            catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested) { break; }
            catch(Exception ex) { logger.LogError(ex,"Remote outbox issue. Alarm monitoring continues; check notification health."); }
            await Task.Delay(TimeSpan.FromSeconds(3),stoppingToken);
        }
    }
}
