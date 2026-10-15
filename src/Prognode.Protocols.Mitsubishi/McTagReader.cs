using System.Collections.Concurrent;
using Prognode.Contracts.Devices;
using Prognode.Contracts.Tags;
using Prognode.Protocols.Abstractions;

namespace Prognode.Protocols.Mitsubishi;

/// <summary>
/// Reads Mitsubishi MC Tags. Tags of one device type that are close together are read as one batch
/// (words for word devices, bit units for bit devices). A batch the PLC refuses is re-read Tag by
/// Tag so only the offending address goes Bad, and that Tag is then read on its own for a while.
/// 32-bit values are low word first (Mitsubishi order); 64-bit values likewise.
/// </summary>
public sealed class McTagReader(McClient client) : ITagReader
{
    public const string Protocol = "Mitsubishi MC";
    internal const int MaxGapPoints = 16;
    private const int TimeoutMs = 3000;
    private static readonly TimeSpan RejectedQuarantine = TimeSpan.FromSeconds(60);
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _rejectedUntil = new();

    public McTagReader() : this(new McClient()) { }

    public bool CanHandle(DeviceDefinition device) => device.Protocol.Equals(Protocol, StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<TagValueSnapshot>> ReadAsync(DeviceDefinition device, IReadOnlyList<TagDefinition> tags, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(device.Host) || device.Port is null)
            throw new InvalidOperationException($"Device '{device.Name}' has incomplete Mitsubishi MC settings.");
        var series = (McSeries)(device.UnitId ?? 0);
        var result = new List<TagValueSnapshot>();
        var planned = new List<(TagDefinition Tag, McAddress Address)>();
        foreach (var tag in tags.Where(t => t.Enabled))
        {
            try { planned.Add((tag, McAddress.Parse(tag.Address, tag.DataType, series))); }
            catch (ArgumentException ex) { result.Add(Bad(device, tag, ex.Message)); }
        }
        if (planned.Count == 0) return result;

        var now = DateTimeOffset.UtcNow;
        var quarantined = planned.Where(x => _rejectedUntil.TryGetValue(x.Tag.Id, out var until) && until > now).ToArray();
        var batches = PlanBatches(planned.Except(quarantined))
            .Concat(quarantined.Select(x => new Batch(x.Address.Device, x.Address.Number, x.Address.Words) { Tags = { x } }));
        foreach (var batch in batches)
        {
            try
            {
                await ReadBatchAsync(device, batch, result, cancellationToken);
            }
            catch (McEndCodeException) when (batch.Tags.Count > 1)
            {
                foreach (var item in batch.Tags)
                {
                    try { await ReadBatchAsync(device, new Batch(item.Address.Device, item.Address.Number, item.Address.Words) { Tags = { item } }, result, cancellationToken); }
                    catch (McEndCodeException ex)
                    {
                        _rejectedUntil[item.Tag.Id] = DateTimeOffset.UtcNow + RejectedQuarantine;
                        result.Add(Bad(device, item.Tag, ex.Message));
                    }
                }
            }
            catch (McEndCodeException ex)
            {
                _rejectedUntil[batch.Tags[0].Tag.Id] = DateTimeOffset.UtcNow + RejectedQuarantine;
                result.Add(Bad(device, batch.Tags[0].Tag, ex.Message));
            }
        }
        return result;
    }

    private async Task ReadBatchAsync(DeviceDefinition device, Batch batch, List<TagValueSnapshot> result, CancellationToken ct)
    {
        if (batch.Device.IsBit)
        {
            var bits = await client.ReadBitsAsync(device.Host!, device.Port!.Value, batch.Device.Code, batch.Start, batch.Count, TimeoutMs, ct);
            foreach (var item in batch.Tags)
            {
                _rejectedUntil.TryRemove(item.Tag.Id, out _);
                result.Add(Good(device, item.Tag, bits[item.Address.Number - batch.Start] ? 1 : 0));
            }
            return;
        }
        var words = await client.ReadWordsAsync(device.Host!, device.Port!.Value, batch.Device.Code, batch.Start, batch.Count, TimeoutMs, ct);
        foreach (var item in batch.Tags)
        {
            _rejectedUntil.TryRemove(item.Tag.Id, out _);
            result.Add(Good(device, item.Tag, Decode(item.Tag.DataType, item.Address, words.AsSpan(item.Address.Number - batch.Start, item.Address.Words))));
        }
    }

    internal static double Decode(TagDataType type, McAddress address, ReadOnlySpan<ushort> w) => type switch
    {
        TagDataType.Bool => (w[0] >> address.Bit!.Value & 1) != 0 ? 1 : 0,
        TagDataType.Word or TagDataType.UInt16 => w[0],
        TagDataType.Int16 => unchecked((short)w[0]),
        TagDataType.UInt32 => (uint)(w[0] | w[1] << 16),
        TagDataType.Int32 => (int)(w[0] | (uint)w[1] << 16),
        TagDataType.Float32 => BitConverter.Int32BitsToSingle((int)(w[0] | (uint)w[1] << 16)),
        TagDataType.UInt64 => (double)Long(w),
        TagDataType.Int64 => (long)Long(w),
        TagDataType.Float64 => BitConverter.Int64BitsToDouble((long)Long(w)),
        _ => throw new NotSupportedException($"Unsupported Mitsubishi data type {type}."),
    };

    private static ulong Long(ReadOnlySpan<ushort> w) => w[0] | (ulong)w[1] << 16 | (ulong)w[2] << 32 | (ulong)w[3] << 48;

    internal static IReadOnlyList<Batch> PlanBatches(IEnumerable<(TagDefinition Tag, McAddress Address)> tags)
    {
        var batches = new List<Batch>();
        foreach (var group in tags.GroupBy(x => x.Address.Device.Code))
        {
            Batch? current = null;
            var max = group.First().Address.Device.IsBit ? McClient.MaxBitPoints : McClient.MaxWordPoints;
            foreach (var item in group.OrderBy(x => x.Address.Number))
            {
                var end = item.Address.Number + item.Address.Words;
                if (current is not null && item.Address.Number <= current.Start + current.Count + MaxGapPoints &&
                    Math.Max(end, current.Start + current.Count) - current.Start <= max)
                {
                    current.Count = Math.Max(end, current.Start + current.Count) - current.Start;
                    current.Tags.Add(item);
                    continue;
                }
                current = new Batch(item.Address.Device, item.Address.Number, item.Address.Words) { Tags = { item } };
                batches.Add(current);
            }
        }
        return batches;
    }

    private static TagValueSnapshot Good(DeviceDefinition device, TagDefinition tag, double raw) =>
        new(tag.Id, device.Id, raw, EngineeringValue.From(raw, tag), TagQuality.Good, DateTimeOffset.UtcNow, Protocol, null);

    private static TagValueSnapshot Bad(DeviceDefinition device, TagDefinition tag, string error) =>
        new(tag.Id, device.Id, null, null, TagQuality.Bad, DateTimeOffset.UtcNow, Protocol, error);

    internal sealed class Batch(McDevice device, int start, int count)
    {
        public McDevice Device { get; } = device;
        public int Start { get; } = start;
        public int Count { get; set; } = count;
        public List<(TagDefinition Tag, McAddress Address)> Tags { get; } = [];
    }
}

/// <summary>Checks Mitsubishi Tag addresses against the device's PLC series; overlapping values are refused.</summary>
public sealed class McTagValidator : ITagDefinitionValidator
{
    public bool CanHandle(DeviceDefinition device) => device.Protocol.Equals(McTagReader.Protocol, StringComparison.OrdinalIgnoreCase);

    public void Validate(TagDefinition candidate, IReadOnlyList<TagDefinition> existingTags)
    {
        if (candidate.BitIndex is not null) throw new ArgumentException("For Mitsubishi put the bit in the address (D100.A). Leave BitIndex empty.");
        var a = McAddress.Parse(candidate.Address, candidate.DataType);
        foreach (var old in existingTags.Where(t => t.DeviceId == candidate.DeviceId && t.Id != candidate.Id))
        {
            McAddress b;
            try { b = McAddress.Parse(old.Address, old.DataType); } catch (ArgumentException) { continue; }
            if (a.Device.Code != b.Device.Code || a.Number > b.Number + b.Words - 1 || b.Number > a.Number + a.Words - 1) continue;
            if (a.Bit is not null || b.Bit is not null) { if (a.Bit != b.Bit || a.Number != b.Number) continue; }
            throw new ArgumentException($"Overlapping Mitsubishi address: {old.Name} ({old.Address}).");
        }
    }
}

/// <summary>A recent good poll counts; otherwise SM400 (always ON) is read once.</summary>
public sealed class McHealthProbe(McClient client) : IDeviceHealthProbe
{
    public bool CanHandle(DeviceDefinition device) => device.Protocol.Equals(McTagReader.Protocol, StringComparison.OrdinalIgnoreCase);

    public async Task<DeviceHealthResult> CheckAsync(DeviceDefinition device, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(device.Host) || device.Port is null)
            return new(false, "Device TCP configuration is incomplete.", 0);
        if (client.HasRecentSuccess(device.Host, device.Port.Value, TimeSpan.FromSeconds(30)))
            return new(true, "Polling connection is healthy.", 0);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await client.ReadBitsAsync(device.Host, device.Port.Value, 0x91, 400, 1, 1500, cancellationToken);
            return new(true, "Mitsubishi PLC answered.", Math.Round(timer.Elapsed.TotalMilliseconds, 1));
        }
        catch (McEndCodeException)
        {
            return new(true, "Mitsubishi PLC answered.", Math.Round(timer.Elapsed.TotalMilliseconds, 1));
        }
        catch (IOException ex)
        {
            return new(false, ex.Message, Math.Round(timer.Elapsed.TotalMilliseconds, 1));
        }
    }
}
