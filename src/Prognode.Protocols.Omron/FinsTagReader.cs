using System.Collections.Concurrent;
using Prognode.Contracts.Devices;
using Prognode.Contracts.Tags;
using Prognode.Protocols.Abstractions;

namespace Prognode.Protocols.Omron;

/// <summary>
/// Reads Omron FINS Tags. Tags of one memory area that are close together are read as one batch.
/// A batch the PLC refuses is re-read Tag by Tag so only the offending address goes Bad, and that
/// Tag is then read on its own for a while. 32/64-bit values are low word first, each word
/// big-endian (CX-Programmer / Sysmac Studio layout).
/// </summary>
public sealed class FinsTagReader(FinsClient client) : ITagReader
{
    public const string Protocol = "Omron FINS";
    internal const int MaxGapWords = 16;
    private const int TimeoutMs = 3000;
    private static readonly TimeSpan RejectedQuarantine = TimeSpan.FromSeconds(60);
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _rejectedUntil = new();

    public FinsTagReader() : this(new FinsClient()) { }

    public bool CanHandle(DeviceDefinition device) => device.Protocol.Equals(Protocol, StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<TagValueSnapshot>> ReadAsync(DeviceDefinition device, IReadOnlyList<TagDefinition> tags, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(device.Host) || device.Port is null)
            throw new InvalidOperationException($"Device '{device.Name}' has incomplete Omron FINS settings.");
        var result = new List<TagValueSnapshot>();
        var planned = new List<(TagDefinition Tag, FinsAddress Address)>();
        foreach (var tag in tags.Where(t => t.Enabled))
        {
            try { planned.Add((tag, FinsAddress.Parse(tag.Address, tag.DataType))); }
            catch (ArgumentException ex) { result.Add(Bad(device, tag, ex.Message)); }
        }
        if (planned.Count == 0) return result;

        var now = DateTimeOffset.UtcNow;
        var quarantined = planned.Where(x => _rejectedUntil.TryGetValue(x.Tag.Id, out var until) && until > now).ToArray();
        var batches = PlanBatches(planned.Except(quarantined)).Concat(quarantined.Select(Single));
        foreach (var batch in batches)
        {
            try
            {
                await ReadBatchAsync(device, batch, result, cancellationToken);
            }
            catch (FinsEndCodeException) when (batch.Tags.Count > 1)
            {
                foreach (var item in batch.Tags)
                {
                    try { await ReadBatchAsync(device, Single(item), result, cancellationToken); }
                    catch (FinsEndCodeException ex)
                    {
                        _rejectedUntil[item.Tag.Id] = DateTimeOffset.UtcNow + RejectedQuarantine;
                        result.Add(Bad(device, item.Tag, ex.Message));
                    }
                }
            }
            catch (FinsEndCodeException ex)
            {
                _rejectedUntil[batch.Tags[0].Tag.Id] = DateTimeOffset.UtcNow + RejectedQuarantine;
                result.Add(Bad(device, batch.Tags[0].Tag, ex.Message));
            }
        }
        return result;
    }

    private static Batch Single((TagDefinition Tag, FinsAddress Address) item) =>
        new(item.Address.Code, item.Address.IsFlag, item.Address.WireWord, Points(item.Address)) { Tags = { item } };

    private static int Points(FinsAddress address) => address.IsFlag ? 1 : address.Words;

    private async Task ReadBatchAsync(DeviceDefinition device, Batch batch, List<TagValueSnapshot> result, CancellationToken ct)
    {
        if (batch.IsFlag)
        {
            var flags = await client.ReadFlagsAsync(device.Host!, device.Port!.Value, batch.Code, batch.Start, batch.Count, TimeoutMs, ct);
            foreach (var item in batch.Tags)
            {
                _rejectedUntil.TryRemove(item.Tag.Id, out _);
                result.Add(Good(device, item.Tag, flags[item.Address.WireWord - batch.Start] ? 1 : 0));
            }
            return;
        }
        var words = await client.ReadWordsAsync(device.Host!, device.Port!.Value, batch.Code, batch.Start, batch.Count, TimeoutMs, ct);
        foreach (var item in batch.Tags)
        {
            _rejectedUntil.TryRemove(item.Tag.Id, out _);
            result.Add(Good(device, item.Tag, Decode(item.Tag.DataType, item.Address, words.AsSpan(item.Address.WireWord - batch.Start, item.Address.Words))));
        }
    }

    internal static double Decode(TagDataType type, FinsAddress address, ReadOnlySpan<ushort> w) => type switch
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
        _ => throw new NotSupportedException($"Unsupported Omron data type {type}."),
    };

    private static ulong Long(ReadOnlySpan<ushort> w) => w[0] | (ulong)w[1] << 16 | (ulong)w[2] << 32 | (ulong)w[3] << 48;

    internal static IReadOnlyList<Batch> PlanBatches(IEnumerable<(TagDefinition Tag, FinsAddress Address)> tags)
    {
        var batches = new List<Batch>();
        foreach (var group in tags.GroupBy(x => (x.Address.Code, x.Address.IsFlag)))
        {
            Batch? current = null;
            foreach (var item in group.OrderBy(x => x.Address.WireWord))
            {
                var start = item.Address.WireWord;
                var end = start + Points(item.Address);
                if (current is not null && start <= current.Start + current.Count + MaxGapWords &&
                    Math.Max(end, current.Start + current.Count) - current.Start <= FinsClient.MaxWords)
                {
                    current.Count = Math.Max(end, current.Start + current.Count) - current.Start;
                    current.Tags.Add(item);
                    continue;
                }
                current = Single(item);
                batches.Add(current);
            }
        }
        return batches;
    }

    private static TagValueSnapshot Good(DeviceDefinition device, TagDefinition tag, double raw) =>
        new(tag.Id, device.Id, raw, EngineeringValue.From(raw, tag), TagQuality.Good, DateTimeOffset.UtcNow, Protocol, null);

    private static TagValueSnapshot Bad(DeviceDefinition device, TagDefinition tag, string error) =>
        new(tag.Id, device.Id, null, null, TagQuality.Bad, DateTimeOffset.UtcNow, Protocol, error);

    internal sealed class Batch(byte code, bool isFlag, int start, int count)
    {
        public byte Code { get; } = code;
        public bool IsFlag { get; } = isFlag;
        public int Start { get; } = start;
        public int Count { get; set; } = count;
        public List<(TagDefinition Tag, FinsAddress Address)> Tags { get; } = [];
    }
}

/// <summary>Checks Omron Tag addresses; overlapping values are refused (bits of one word may share it).</summary>
public sealed class FinsTagValidator : ITagDefinitionValidator
{
    public bool CanHandle(DeviceDefinition device) => device.Protocol.Equals(FinsTagReader.Protocol, StringComparison.OrdinalIgnoreCase);

    public void Validate(TagDefinition candidate, IReadOnlyList<TagDefinition> existingTags)
    {
        if (candidate.BitIndex is not null) throw new ArgumentException("For Omron put the bit in the address (D100.05). Leave BitIndex empty.");
        var a = FinsAddress.Parse(candidate.Address, candidate.DataType);
        foreach (var old in existingTags.Where(t => t.DeviceId == candidate.DeviceId && t.Id != candidate.Id))
        {
            FinsAddress b;
            try { b = FinsAddress.Parse(old.Address, old.DataType); } catch (ArgumentException) { continue; }
            if (a.Code != b.Code || a.IsFlag != b.IsFlag) continue;
            var aEnd = a.WireWord + Math.Max(a.Words, 1) - 1;
            var bEnd = b.WireWord + Math.Max(b.Words, 1) - 1;
            if (a.WireWord > bEnd || b.WireWord > aEnd) continue;
            if (a.Bit is not null || b.Bit is not null) { if (a.Bit != b.Bit || a.WireWord != b.WireWord) continue; }
            throw new ArgumentException($"Overlapping Omron address: {old.Name} ({old.Address}).");
        }
    }
}

/// <summary>A recent good poll counts; otherwise D0 is read once (a refusal still proves the PLC answers).</summary>
public sealed class FinsHealthProbe(FinsClient client) : IDeviceHealthProbe
{
    public bool CanHandle(DeviceDefinition device) => device.Protocol.Equals(FinsTagReader.Protocol, StringComparison.OrdinalIgnoreCase);

    public async Task<DeviceHealthResult> CheckAsync(DeviceDefinition device, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(device.Host) || device.Port is null)
            return new(false, "Device TCP configuration is incomplete.", 0);
        if (client.HasRecentSuccess(device.Host, device.Port.Value, TimeSpan.FromSeconds(30)))
            return new(true, "Polling connection is healthy.", 0);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            await client.ReadWordsAsync(device.Host, device.Port.Value, 0x82, 0, 1, 1500, cancellationToken);
            return new(true, "Omron PLC answered.", Math.Round(timer.Elapsed.TotalMilliseconds, 1));
        }
        catch (FinsEndCodeException)
        {
            return new(true, "Omron PLC answered.", Math.Round(timer.Elapsed.TotalMilliseconds, 1));
        }
        catch (IOException ex)
        {
            return new(false, ex.Message, Math.Round(timer.Elapsed.TotalMilliseconds, 1));
        }
    }
}
