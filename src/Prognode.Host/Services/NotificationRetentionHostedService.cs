using Prognode.Notifications;
namespace Prognode.Host.Services;
public sealed class NotificationRetentionHostedService(
    NotificationEventStore store,ILogger<NotificationRetentionHostedService> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        while(!stop.IsCancellationRequested)
        {
            try { store.PruneOld(90); }
            catch(Exception ex) { logger.LogError(ex,"PROGNODE notification retention/storage error; check disk space and SQLite health."); }
            await Task.Delay(TimeSpan.FromHours(24),stop);
        }
    }
}
