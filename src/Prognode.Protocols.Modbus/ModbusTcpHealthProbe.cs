using System.Diagnostics;
using System.Net.Sockets;
using Prognode.Contracts.Devices;
using Prognode.Protocols.Abstractions;

namespace Prognode.Protocols.Modbus;

public sealed class ModbusTcpHealthProbe(ModbusTcpRegisterClient client) : IDeviceHealthProbe
{
    private const int TimeoutMs = 1500;
    private static readonly TimeSpan PollWindow = TimeSpan.FromSeconds(30);

    public bool CanHandle(DeviceDefinition device) =>
        string.Equals(
            device.Protocol,
            "Modbus TCP",
            StringComparison.OrdinalIgnoreCase);

    public async Task<DeviceHealthResult> CheckAsync(
        DeviceDefinition device,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(device.Host) || device.Port is null)
        {
            return new DeviceHealthResult(
                false,
                "Device TCP configuration is incomplete.",
                0);
        }

        // A recent successful poll on the persistent connection proves the link; opening a second
        // socket every 10 s would consume one of the few connections gateways allow (review Y3).
        if (client.HasRecentSuccess(device.Host, device.Port.Value, PollWindow))
            return new DeviceHealthResult(true, "Polling connection is healthy.", 0);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var timeout =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            timeout.CancelAfter(TimeoutMs);

            using var client = new TcpClient();

            await client.ConnectAsync(
                device.Host,
                device.Port.Value,
                timeout.Token);

            stopwatch.Stop();

            return new DeviceHealthResult(
                true,
                "TCP connection is available.",
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 1));
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            stopwatch.Stop();

            return new DeviceHealthResult(
                false,
                $"TCP timeout after {TimeoutMs} ms.",
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 1));
        }
        catch (SocketException ex)
        {
            stopwatch.Stop();

            return new DeviceHealthResult(
                false,
                $"TCP connection failed: {ex.SocketErrorCode}.",
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 1));
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            return new DeviceHealthResult(
                false,
                ex.Message,
                Math.Round(stopwatch.Elapsed.TotalMilliseconds, 1));
        }
    }
}
