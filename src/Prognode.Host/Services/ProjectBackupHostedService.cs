using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Prognode.Host.Services;

public sealed class ProjectBackupHostedService(ProjectBackupService backup,
    ILogger<ProjectBackupHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // No passphrase is persisted. Unconfigured automatic backups never create plaintext exports.
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        do
        {
            try { await backup.TryRunDailyAsync(stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError(ex,"Automatic PROGNODE backup failed"); }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
