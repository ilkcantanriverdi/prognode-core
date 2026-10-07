using System.Collections.Concurrent;
using System.Globalization;
using Opc.Ua;
using Opc.Ua.Client;
using Prognode.Contracts.Devices;
using Prognode.Contracts.Tags;
using Prognode.Protocols.Abstractions;

namespace Prognode.Protocols.OpcUa;

// OPC UA is read-only in the first Core connector: no Write, Call or Browse API.
public sealed class OpcUaTagReader(OpcUaConnectionFactory connections) : ITagReader, IDisposable
{
    private readonly ConcurrentDictionary<Guid, DeviceSession> _sessions = new();

    public bool CanHandle(DeviceDefinition device) =>
        device.Protocol.Equals("OPC UA", StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<TagValueSnapshot>> ReadAsync(DeviceDefinition device,
        IReadOnlyList<TagDefinition> tags, CancellationToken cancellationToken)
    {
        var enabled = tags.Where(tag => tag.Enabled).ToArray();
        if (enabled.Length == 0) return [];
        var endpoint = OpcUaEndpoint.Parse(device.Host);
        if (!_sessions.TryGetValue(device.Id, out var holder) || holder.Endpoint != endpoint)
        {
            var replacement = new DeviceSession(endpoint);
            _sessions.AddOrUpdate(device.Id, replacement, (_, previous) =>
            {
                previous.Dispose();
                return replacement;
            });
            holder = replacement;
        }

        await holder.Gate.WaitAsync(cancellationToken);
        try
        {
            if (holder.Session is null || !holder.Session.Connected)
            {
                holder.Session?.Dispose();
                holder.Session = await connections.OpenAsync(endpoint, cancellationToken);
            }

            var nodes = new ReadValueIdCollection();
            foreach (var tag in enabled)
                nodes.Add(new ReadValueId {
                    NodeId = OpcUaNodeAddress.Resolve(tag.Address, holder.Session.NamespaceUris),
                    AttributeId = Attributes.Value });

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                var response = await holder.Session.ReadAsync(null, 0,
                    TimestampsToReturn.Both, nodes, timeout.Token);
                if (response.Results.Count != enabled.Length)
                    throw new IOException("OPC UA Read returned an unexpected number of results.");
                var now = DateTimeOffset.UtcNow;
                return enabled.Select((tag, index) =>
                    Map(device.Id, tag, response.Results[index], now)).ToArray();
            }
            catch
            {
                holder.Session.Dispose();
                holder.Session = null;
                throw;
            }
        }
        finally { holder.Gate.Release(); }
    }

    public bool IsConnected(Guid deviceId) =>
        _sessions.TryGetValue(deviceId, out var holder) && holder.Session?.Connected == true;

    public void Remove(Guid deviceId)
    {
        if (_sessions.TryRemove(deviceId, out var holder)) holder.Dispose();
    }

    public void PruneExcept(IReadOnlySet<Guid> activeDeviceIds)
    {
        foreach (var (id, _) in _sessions)
            if (!activeDeviceIds.Contains(id)) Remove(id);
    }

    public void Dispose()
    {
        foreach (var (_, holder) in _sessions) holder.Dispose();
        _sessions.Clear();
    }

    private static TagValueSnapshot Map(Guid deviceId, TagDefinition tag, DataValue result,
        DateTimeOffset now)
    {
        var timestamp = result.SourceTimestamp == DateTime.MinValue
            ? now : new DateTimeOffset(DateTime.SpecifyKind(result.SourceTimestamp, DateTimeKind.Utc));
        if (StatusCode.IsBad(result.StatusCode))
            return new(tag.Id, deviceId, null, null, TagQuality.Bad, timestamp, "OPC UA",
                result.StatusCode.ToString());

        var source = result.Value;
        double raw;
        if (tag.DataType == TagDataType.Bool && source is bool boolean)
            raw = boolean ? 1 : 0;
        else if (tag.DataType != TagDataType.Bool && source is
                 byte or sbyte or ushort or short or uint or int or ulong or long or float or double)
            raw = Convert.ToDouble(source, CultureInfo.InvariantCulture);
        else
            return new(tag.Id, deviceId, null, null, TagQuality.ConfigError, timestamp,
                "OPC UA", "Node value type does not match the Tag data type.");

        if (!double.IsFinite(raw) || !InRange(raw, tag.DataType))
            return new(tag.Id, deviceId, null, null, TagQuality.ConfigError, timestamp,
                "OPC UA", "Node value is outside the Tag data type range.");
        var value = EngineeringValue.From(raw, tag);
        return new(tag.Id, deviceId, raw, value,
            StatusCode.IsUncertain(result.StatusCode) ? TagQuality.Uncertain : TagQuality.Good,
            timestamp, "OPC UA");
    }

    private static bool InRange(double value, TagDataType type) => type switch
    {
        TagDataType.Bool => value is 0 or 1,
        TagDataType.Word or TagDataType.UInt16 => Math.Truncate(value) == value && value is >= 0 and <= ushort.MaxValue,
        TagDataType.Int16 => Math.Truncate(value) == value && value is >= short.MinValue and <= short.MaxValue,
        TagDataType.UInt32 => Math.Truncate(value) == value && value is >= 0 and <= uint.MaxValue,
        TagDataType.Int32 => Math.Truncate(value) == value && value is >= int.MinValue and <= int.MaxValue,
        TagDataType.Float32 => Math.Abs(value) <= float.MaxValue,
        _ => false
    };

    private sealed class DeviceSession(string endpoint) : IDisposable
    {
        public string Endpoint { get; } = endpoint;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public ISession? Session { get; set; }
        public void Dispose() { Session?.Dispose(); Gate.Dispose(); }
    }
}
