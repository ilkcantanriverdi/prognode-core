using Prognode.Alarm;
using Prognode.Core.Devices;
using Prognode.Protocols.Abstractions;
using Prognode.Licensing;

namespace Prognode.Host.Services;

public sealed class DeviceHealthHostedService(
    IDeviceRepository devices,
    IEnumerable<IDeviceHealthProbe> probes,
    DeviceCommunicationMonitor monitor,
    LicenseService license,
    ILogger<DeviceHealthHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan ProbeInterval = TimeSpan.FromSeconds(10);
    private readonly SemaphoreSlim _concurrency = new(4);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var all = await devices.GetAllAsync(stoppingToken);
                monitor.PruneMissingDevices(all.Select(x => x.Id).ToHashSet());

                var tasks = all.Select(device => CheckAsync(device, stoppingToken));
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Device health cycle failed.");
            }

            try
            {
                await Task.Delay(ProbeInterval, stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task CheckAsync(
        Prognode.Contracts.Devices.DeviceDefinition device,
        CancellationToken cancellationToken)
    {
        var probe = probes.FirstOrDefault(x => x.CanHandle(device));
        if (probe is null) return;

        await _concurrency.WaitAsync(cancellationToken);
        try
        {
            var result = await probe.CheckAsync(device, cancellationToken);
            if (license.HasModule("ALARM"))
                await monitor.ReportAsync(device, result, cancellationToken);
        }
        finally
        {
            _concurrency.Release();
        }
    }
}
