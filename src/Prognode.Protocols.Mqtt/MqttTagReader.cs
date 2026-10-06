using System.Buffers;
using System.Collections.Concurrent;
using MQTTnet;
using MQTTnet.Protocol;
using Prognode.Contracts.Devices;
using Prognode.Contracts.Tags;
using Prognode.Protocols.Abstractions;

namespace Prognode.Protocols.Mqtt;

// A single long-lived subscription per configured broker device. The Core poller
// samples the most recently received message; it never publishes to the broker.
public sealed class MqttTagReader : ITagReader, IDisposable
{
    private readonly ConcurrentDictionary<Guid, Subscription> _subscriptions = new();

    public bool CanHandle(DeviceDefinition device) =>
        device.Protocol.Equals("MQTT", StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<TagValueSnapshot>> ReadAsync(DeviceDefinition device,
        IReadOnlyList<TagDefinition> tags, CancellationToken cancellationToken)
    {
        var enabled = tags.Where(tag => tag.Enabled).ToArray();
        if (enabled.Length == 0) return [];

        var endpoint = MqttEndpoint.Parse(device.Host, device.Port);
        var topics = enabled.Select(tag => tag.Address).ToHashSet(StringComparer.Ordinal);
        if (!_subscriptions.TryGetValue(device.Id, out var subscription) ||
            subscription.Endpoint != endpoint || !subscription.Topics.SetEquals(topics))
        {
            var replacement = new Subscription(device.Id, endpoint, topics);
            _subscriptions.AddOrUpdate(device.Id, replacement, (_, prior) =>
            {
                prior.Dispose();
                return replacement;
            });
            subscription = replacement;
        }

        await subscription.EnsureConnectedAsync(cancellationToken);
        var now = DateTimeOffset.UtcNow;
        var results = new List<TagValueSnapshot>(enabled.Length);
        foreach (var tag in enabled)
        {
            if (subscription.Latest.TryGetValue(tag.Address, out var received))
                results.Add(MqttPayloadMapper.Map(device.Id, tag, received.Payload,
                    received.Retained, received.At, now, device.PollIntervalMs));
            else
                results.Add(new TagValueSnapshot(tag.Id, device.Id, null, null,
                    TagQuality.Uncertain, now, "MQTT", "Waiting for first topic message."));
        }
        return results;
    }

    public void PruneExcept(IReadOnlySet<Guid> activeDeviceIds)
    {
        foreach (var (id, _) in _subscriptions)
            if (!activeDeviceIds.Contains(id) && _subscriptions.TryRemove(id, out var removed))
                removed.Dispose();
    }

    public void Remove(Guid deviceId)
    {
        if (_subscriptions.TryRemove(deviceId, out var removed)) removed.Dispose();
    }

    public bool IsConnected(Guid deviceId) =>
        _subscriptions.TryGetValue(deviceId, out var subscription) && subscription.IsConnected;

    public void Dispose()
    {
        foreach (var (_, subscription) in _subscriptions)
            subscription.Dispose();
        _subscriptions.Clear();
    }

    private sealed record Received(byte[] Payload, bool Retained, DateTimeOffset At);

    private sealed class Subscription : IDisposable
    {
        private readonly IMqttClient _client = new MqttClientFactory().CreateMqttClient();
        private readonly SemaphoreSlim _gate = new(1, 1);
        private volatile bool _subscribed;

        public Subscription(Guid deviceId, MqttEndpoint endpoint, HashSet<string> topics)
        {
            DeviceId = deviceId;
            Endpoint = endpoint;
            Topics = topics;
            _client.ApplicationMessageReceivedAsync += args =>
            {
                var message = args.ApplicationMessage;
                if (Topics.Contains(message.Topic))
                    Latest[message.Topic] = new Received(message.Payload.Length > 256
                            ? new byte[257] : message.Payload.ToArray(),
                        message.Retain, DateTimeOffset.UtcNow);
                return Task.CompletedTask;
            };
            _client.DisconnectedAsync += _ =>
            {
                _subscribed = false;
                Latest.Clear();
                return Task.CompletedTask;
            };
        }

        public Guid DeviceId { get; }
        public MqttEndpoint Endpoint { get; }
        public HashSet<string> Topics { get; }
        public ConcurrentDictionary<string, Received> Latest { get; } = new(StringComparer.Ordinal);
        public bool IsConnected => _client.IsConnected && _subscribed;

        public async Task EnsureConnectedAsync(CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken);
            try
            {
                if (!_client.IsConnected)
                {
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(5));
                    var builder = new MqttClientOptionsBuilder()
                        .WithClientId($"prognode-{DeviceId:N}")
                        .WithTcpServer(Endpoint.Host, Endpoint.Port)
                        .WithCleanSession(true)
                        .WithTimeout(TimeSpan.FromSeconds(5));
                    if (Endpoint.UseTls) builder.WithTlsOptions(options => options.UseTls(true));
                    var connected = await _client.ConnectAsync(builder.Build(), timeout.Token);
                    if (connected.ResultCode != MqttClientConnectResultCode.Success)
                        throw new IOException($"MQTT broker rejected connection: {connected.ResultCode}.");
                    _subscribed = false;
                }

                if (!_subscribed)
                {
                    var factory = new MqttClientFactory();
                    var builder = factory.CreateSubscribeOptionsBuilder();
                    foreach (var topic in Topics)
                        builder.WithTopicFilter(topic);
                    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeout.CancelAfter(TimeSpan.FromSeconds(5));
                    var result = await _client.SubscribeAsync(builder.Build(), timeout.Token);
                    if (result.Items.Count != Topics.Count || result.Items.Any(item =>
                            item.ResultCode is not (MqttClientSubscribeResultCode.GrantedQoS0 or
                                MqttClientSubscribeResultCode.GrantedQoS1 or
                                MqttClientSubscribeResultCode.GrantedQoS2)))
                        throw new IOException("MQTT broker rejected one or more topic subscriptions.");
                    _subscribed = true;
                }
            }
            finally { _gate.Release(); }
        }

        public void Dispose()
        {
            _client.Dispose();
            _gate.Dispose();
        }
    }
}
