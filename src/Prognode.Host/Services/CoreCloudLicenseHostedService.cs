using System.Text;
using Prognode.Core.Connectivity;
using Prognode.Licensing;

namespace Prognode.Host.Services;

/// <summary>
/// Web V1.8 Cloud activation/heartbeat. A signed license only runs on the Core and machine it was
/// activated for: activation returns a certificate signed by PROGNODE Cloud, stored next to the
/// license and checked locally (offline). A server-reported REVOKED for the exact licenseId is
/// persisted; a released installation loses its activation. Cloud unavailability never invents a
/// revocation, and an activated Core keeps running offline.
/// </summary>
public sealed class CoreCloudLicenseHostedService(
    CoreCloudLicenseClient cloud,
    CoreCloudLicenseOptions options,
    CoreCloudLicenseStateStore stateStore,
    FileBackedLicenseProvider provider,
    LocalLicenseStore localLicenseStore,
    ServerAccessService serverAccess,
    IMachineFingerprintProvider machine,
    ILogger<CoreCloudLicenseHostedService> logger) : BackgroundService
{
    // Cloud codes meaning this installation is no longer allowed to run the license.
    private static readonly HashSet<string> InstallationEndedCodes = new(StringComparer.Ordinal)
    {
        "installation_revoked", "installation_released", "installation_machine_mismatch"
    };

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            if (cloud.IsConfigured)
                await TrySyncAsync(stoppingToken);

            // Short local check interval lets a freshly imported license activate quickly. Heartbeat
            // network calls themselves are still limited by HeartbeatIntervalSeconds.
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

            var activation = provider.GetActivation();
            var needsActivation = string.IsNullOrWhiteSpace(cached.ActivationToken) || activation is { Activated: false };
            var heartbeatSeconds = Math.Clamp(options.HeartbeatIntervalSeconds, 60, 3600);
            var heartbeatDue = cached.LastSyncedAtUtc is null ||
                DateTimeOffset.UtcNow - cached.LastSyncedAtUtc.Value >= TimeSpan.FromSeconds(heartbeatSeconds);
            // Do not hammer Cloud with activation attempts that keep failing (e.g. limit reached).
            if (needsActivation && !heartbeatDue && cached.LastError is not null)
                return;

            CoreCloudLicenseStatus? result = null;
            if (needsActivation)
            {
                var signed = Encoding.UTF8.GetString(localLicenseStore.ReadAllBytes());
                result = await cloud.ActivateAsync(
                    local.LicenseId,
                    signed,
                    serverAccess.Identity.ServerId,
                    serverAccess.Identity.DisplayName,
                    machine.GetFingerprint(),
                    CoreVersionInfo.Current,
                    stoppingToken);

                if (!string.IsNullOrWhiteSpace(result.ActivationCertificate))
                    provider.ImportActivation(Encoding.UTF8.GetBytes(result.ActivationCertificate));
            }
            else if (heartbeatDue)
            {
                result = await cloud.HeartbeatAsync(
                    cached.ActivationToken!,
                    local.LicenseId,
                    serverAccess.Identity.ServerId,
                    stoppingToken);
            }

            if (result is not null)
                stateStore.Save(result with { ActivationCertificate = null });
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            return;
        }
        catch (CloudLicenseRejectedException ex)
        {
            var previous = stateStore.Load(configured: true);
            if (InstallationEndedCodes.Contains(ex.Code))
            {
                // Released in PROGNODE Account or revoked in Control: this PC is no longer licensed.
                provider.ClearActivation();
                stateStore.Save(previous with { ActivationToken = null, LastError = ex.Message, LastSyncedAtUtc = DateTimeOffset.UtcNow });
            }
            else if (ex.Code == "unauthorized")
            {
                // Stale activation token: activate again on the next pass.
                stateStore.Save(previous with { ActivationToken = null, LastError = null });
            }
            else
            {
                stateStore.Save(previous with { LastError = ex.Message, LastSyncedAtUtc = DateTimeOffset.UtcNow });
            }
            logger.LogWarning("PROGNODE Cloud refused license synchronization: {Code}", ex.Code);
        }
        catch (Exception ex)
        {
            // Network/TLS/deployment problems never stop the local runtime of an activated Core.
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
