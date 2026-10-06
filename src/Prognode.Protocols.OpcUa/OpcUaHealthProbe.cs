using System.Collections.Concurrent;
using System.Diagnostics;
using Prognode.Contracts.Devices;
using Prognode.Protocols.Abstractions;

namespace Prognode.Protocols.OpcUa;

public sealed class OpcUaHealthProbe(OpcUaConnectionFactory connections, OpcUaTagReader reader)
    : IDeviceHealthProbe
{
    private readonly ConcurrentDictionary<Guid, (DateTimeOffset At, DeviceHealthResult Result)> _last = new();

    public bool CanHandle(DeviceDefinition device) =>
        device.Protocol.Equals("OPC UA", StringComparison.OrdinalIgnoreCase);

    public async Task<DeviceHealthResult> CheckAsync(DeviceDefinition device, CancellationToken cancellationToken)
    {
        if (reader.IsConnected(device.Id))
            return new(true, "OPC UA secure session connected.", 0);
        if (_last.TryGetValue(device.Id, out var cached) &&
            DateTimeOffset.UtcNow - cached.At < TimeSpan.FromSeconds(10))
            return cached.Result;

        var timer = Stopwatch.StartNew();
        DeviceHealthResult result;
        try
        {
            using var session = await connections.OpenAsync(device.Host, cancellationToken);
            result = new(session.Connected, "OPC UA SignAndEncrypt session opened.",
                Math.Round(timer.Elapsed.TotalMilliseconds, 1));
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            result = new(false, exception.Message, Math.Round(timer.Elapsed.TotalMilliseconds, 1));
        }
        _last[device.Id] = (DateTimeOffset.UtcNow, result);
        return result;
    }
}
