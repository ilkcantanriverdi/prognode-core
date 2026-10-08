using Prognode.RemoteAccess;

namespace Prognode.Host.Services;

public sealed class RemoteAccessSyncHostedService(
    RemoteAccessService remoteAccess,
    RemoteAccessOptions options,
    ILogger<RemoteAccessSyncHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var status = remoteAccess.GetStatus();
                if (status.Entitled && status.CloudConfigured)
                {
                    // A licensed, activated Core binds itself to its Remote Access subscription.
                    // Binding is idempotent, so a re-imported license or a replacement PC re-binds.
                    if (!status.ServerBound)
                        await remoteAccess.BindServerAsync(stoppingToken);
                    await remoteAccess.SyncAsync(stoppingToken);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Remote Access background sync failed. Local PROGNODE runtime is unaffected.");
            }

            var seconds = Math.Clamp(options.SyncIntervalSeconds, 30, 3600);
            await Task.Delay(TimeSpan.FromSeconds(seconds), stoppingToken);
        }
    }
}
