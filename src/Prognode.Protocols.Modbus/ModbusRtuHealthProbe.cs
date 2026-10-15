using System.Diagnostics;
using Prognode.Contracts.Devices;
using Prognode.Protocols.Abstractions;

namespace Prognode.Protocols.Modbus;

/// <summary>
/// A Modbus RTU device is reachable when it answers on its line. A recent answer to a poll counts;
/// otherwise one holding register is read, and an exception answer (e.g. illegal address) still
/// proves the device is there.
/// </summary>
public sealed class ModbusRtuHealthProbe(ModbusRtuRegisterClient client) : IDeviceHealthProbe
{
    private const int TimeoutMs = 1000;
    private static readonly TimeSpan PollWindow = TimeSpan.FromSeconds(30);

    public bool CanHandle(DeviceDefinition device) =>
        string.Equals(device.Protocol, ModbusTagReader.RtuProtocol, StringComparison.OrdinalIgnoreCase);

    public async Task<DeviceHealthResult> CheckAsync(DeviceDefinition device, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(device.Host) || device.UnitId is null)
            return new DeviceHealthResult(false, "Device serial configuration is incomplete.", 0);
        if (client.HasRecentSuccess(device.Host, device.UnitId.Value, PollWindow))
            return new DeviceHealthResult(true, "Device answers on the RS-485 line.", 0);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            await client.ReadHoldingRegistersAsync(device.Host, 0, device.UnitId.Value, 0, 1, TimeoutMs, cancellationToken);
            return new DeviceHealthResult(true, "Device answered on the RS-485 line.", Math.Round(stopwatch.Elapsed.TotalMilliseconds, 1));
        }
        catch (ModbusExceptionResponseException)
        {
            return new DeviceHealthResult(true, "Device answered on the RS-485 line.", Math.Round(stopwatch.Elapsed.TotalMilliseconds, 1));
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException)
        {
            return new DeviceHealthResult(false, ex.Message, Math.Round(stopwatch.Elapsed.TotalMilliseconds, 1));
        }
    }
}
