using System.Text;
using Prognode.Core.Connectivity;
using Prognode.Licensing;

namespace Prognode.Host.Services;

/// <summary>
/// Web V1.8 Cloud activation/heartbeat. Signed local entitlements remain usable offline until the
/// server has positively reported REVOKED for the exact licenseId. That revoke is persisted across
/// restarts/outages and can only be cleared by a later successful ACTIVE response or a new license import.
/// Local checks remain frequent while network heartbeats are throttled.
/// </summary>
public sealed class CoreCloudLicenseHostedService(
    CoreCloudLicenseClient cloud,
    CoreCloudLicenseOptions options,
    CoreCloudLicenseStateStore stateStore,
    FileBackedLicenseProvider provider,
    LocalLicenseStore localLicenseStore,
    ServerAccessService serverAccess,
    ILogger<CoreCloudLicenseHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (cloud.IsConfigured)
                await TrySyncAsync(stoppingToken);

            // Short local check interval lets a freshly imported license appear in Control Center
            // quickly. Heartbeat network calls themselves are still limited by HeartbeatIntervalSeconds.
            await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
        }
    }

    private async Task TrySyncAsync(CancellationToken stoppingToken)
    {
        try
        {
            var local = provider.GetCurrent();
            if (local.LicenseId is "NONE" or "INVALID" || !localLicenseStore.Exists)
                return;

            var cached = stateStore.Load(configured: true);
            if (!string.IsNullOrWhiteSpace(cached.LicenseId) &&
                !string.Equals(cached.LicenseId, local.LicenseId, StringComparison.OrdinalIgnoreCase))
            {
                // Never let a persisted state from another license influence the newly imported license.
                stateStore.Clear();
                cached = stateStore.Load(configured: true);
            }

            var heartbeatSeconds = Math.Clamp(options.HeartbeatIntervalSeconds, 60, 3600);
            var heartbeatDue = cached.LastSyncedAtUtc is null ||
                DateTimeOffset.UtcNow - cached.LastSyncedAtUtc.Value >= TimeSpan.FromSeconds(heartbeatSeconds);

            CoreCloudLicenseStatus? result = null;
            if (string.IsNullOrWhiteSpace(cached.ActivationToken))
            {
                var signed = Encoding.UTF8.GetString(localLicenseStore.ReadAllBytes());
                result = await cloud.ActivateAsync(
                    local.LicenseId,
                    signed,
                    serverAccess.Identity.ServerId,
                    serverAccess.Identity.DisplayName,
                    stoppingToken);
            }
            else if (heartbeatDue)
            {
                result = await cloud.HeartbeatAsync(
                    cached.ActivationToken,
                    local.LicenseId,
                    serverAccess.Identity.ServerId,
                    stoppingToken);
            }

            if (result is not null)
                stateStore.Save(result);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (Exception ex)
        {
            // Cloud sync is diagnostic/commercial metadata only. A malformed response, temporary
            // DNS/TLS issue or Web deployment error must never stop the local PROGNODE runtime.
            var previous = stateStore.Load(configured: true);
            stateStore.Save(previous with
            {
                LastError = ex.Message,
                LastSyncedAtUtc = previous.LastSyncedAtUtc
            });
            logger.LogWarning(ex, "PROGNODE Cloud license synchronization failed. Signed offline license remains authoritative.");
        }
    }
}
