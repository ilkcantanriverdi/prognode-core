using System.Diagnostics;
using System.Collections.Concurrent;
using MQTTnet;
using MQTTnet.Protocol;
using Prognode.Contracts.Devices;
using Prognode.Protocols.Abstractions;

namespace Prognode.Protocols.Mqtt;

public sealed class MqttHealthProbe(MqttTagReader reader) : IDeviceHealthProbe
{
    private readonly ConcurrentDictionary<Guid, (DateTimeOffset At, DeviceHealthResult Result)> _last = new();
    public bool CanHandle(DeviceDefinition device) =>
        device.Protocol.Equals("MQTT", StringComparison.OrdinalIgnoreCase);

    public async Task<DeviceHealthResult> CheckAsync(DeviceDefinition device, CancellationToken cancellationToken)
    {
        if (reader.IsConnected(device.Id))
            return new DeviceHealthResult(true, "MQTT subscription connected.", 0);
        if (_last.TryGetValue(device.Id, out var cached) &&
            DateTimeOffset.UtcNow - cached.At < TimeSpan.FromSeconds(10))
            return cached.Result;
        var timer = Stopwatch.StartNew();
        try
        {
            var endpoint = MqttEndpoint.Parse(device.Host, device.Port);
            using var client = new MqttClientFactory().CreateMqttClient();
            var builder = new MqttClientOptionsBuilder()
                .WithTcpServer(endpoint.Host, endpoint.Port)
                .WithClientId($"prognode-probe-{Guid.NewGuid():N}")
                .WithTimeout(TimeSpan.FromSeconds(5));
            if (endpoint.UseTls) builder.WithTlsOptions(options => options.UseTls(true));
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));
            var result = await client.ConnectAsync(builder.Build(), timeout.Token);
            var success = result.ResultCode == MqttClientConnectResultCode.Success;
            if (success) await client.DisconnectAsync();
            var health = new DeviceHealthResult(success,
                success ? "MQTT CONNACK OK; topic authorization is checked by Tag polling." :
                    $"MQTT CONNACK: {result.ResultCode}", Math.Round(timer.Elapsed.TotalMilliseconds, 1));
            _last[device.Id] = (DateTimeOffset.UtcNow, health);
            return health;
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var health = new DeviceHealthResult(false, exception.Message,
                Math.Round(timer.Elapsed.TotalMilliseconds, 1));
            _last[device.Id] = (DateTimeOffset.UtcNow, health);
            return health;
        }
    }
}
