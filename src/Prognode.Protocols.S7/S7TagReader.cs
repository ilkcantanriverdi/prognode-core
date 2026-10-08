using System.Buffers.Binary;
using System.Collections.Concurrent;
using Prognode.Contracts.Devices;
using Prognode.Contracts.Tags;
using Prognode.Protocols.Abstractions;
namespace Prognode.Protocols.S7;

/// <summary>
/// Reads S7 Tags on the device's persistent session. Tags in the same DB that are close together
/// are read as one byte range (fewer requests than one ReadVar per Tag); a range the PLC rejects
/// is re-read Tag by Tag so only the offending address goes BAD (review Y2, Y3).
/// </summary>
public sealed class S7TagReader(S7SessionPool pool) : ITagReader
{
    /// <summary>Unused bytes allowed between two Tags of one range.</summary>
    internal const int MaxGapBytes = 16;

    /// <summary>A Tag the PLC rejected is read on its own for this long, so one bad address does
    /// not force the whole range to fail and be re-read Tag by Tag on every poll.</summary>
    internal static readonly TimeSpan RejectedQuarantine = TimeSpan.FromSeconds(60);
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _rejectedUntil = new();

    public S7TagReader() : this(new S7SessionPool()) { }

    public bool CanHandle(DeviceDefinition device) => device.Protocol.Equals("Siemens S7 TCP",StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<TagValueSnapshot>> ReadAsync(DeviceDefinition device,IReadOnlyList<TagDefinition> tags,CancellationToken cancellationToken)
    {
        var enabled = tags.Where(t=>t.Enabled).ToArray();
        if (enabled.Length==0) return Array.Empty<TagValueSnapshot>();
        if (string.IsNullOrWhiteSpace(device.Host)) throw new IOException("Siemens PLC IP missing.");

        var result = new List<TagValueSnapshot>(enabled.Length);
        var planned = new List<(TagDefinition Tag, S7Address Address)>(enabled.Length);
        foreach (var tag in enabled)
        {
            try { planned.Add((tag, S7Address.Parse(tag.Address, tag.DataType))); }
            catch (ArgumentException ex) { result.Add(Bad(device, tag, ex.Message)); }
        }
        if (planned.Count == 0) return result;

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Math.Max(5000, planned.Count * 100 + 3000));

        await pool.UseAsync(device, async (client, ct) =>
        {
            var now = DateTimeOffset.UtcNow;
            var quarantined = planned.Where(x => _rejectedUntil.TryGetValue(x.Tag.Id, out var until) && until > now).ToArray();
            var ranges = PlanRanges(planned.Except(quarantined), client.MaxReadBytes)
                .Concat(quarantined.Select(x => { var r = new ReadRange(x.Address.Db, x.Address.ByteOffset) { End = x.Address.ByteOffset + x.Address.Length }; r.Tags.Add(x); return r; }));
            foreach (var range in ranges)
            {
                try
                {
                    var data = await client.ReadBytesAsync(range.Db, range.Start, range.Length, ct);
                    foreach (var item in range.Tags)
                    {
                        _rejectedUntil.TryRemove(item.Tag.Id, out _);
                        result.Add(Good(device, item.Tag, Decode(item.Tag, item.Address, data, item.Address.ByteOffset - range.Start)));
                    }
                }
                catch (S7ItemRejectedException) when (range.Tags.Count > 1)
                {
                    foreach (var item in range.Tags)
                    {
                        try
                        {
                            var data = await client.ReadBytesAsync(item.Address.Db, item.Address.ByteOffset, item.Address.Length, ct);
                            _rejectedUntil.TryRemove(item.Tag.Id, out _);
                            result.Add(Good(device, item.Tag, Decode(item.Tag, item.Address, data, 0)));
                        }
                        catch (S7ItemRejectedException ex) { Reject(item.Tag.Id); result.Add(Bad(device, item.Tag, ex.Message)); }
                    }
                }
                catch (S7ItemRejectedException ex) { Reject(range.Tags[0].Tag.Id); result.Add(Bad(device, range.Tags[0].Tag, ex.Message)); }
            }
            return true;
        }, deadline.Token);

        return result;
    }

    private void Reject(Guid tagId) => _rejectedUntil[tagId] = DateTimeOffset.UtcNow + RejectedQuarantine;

    internal static IReadOnlyList<ReadRange> PlanRanges(
        IEnumerable<(TagDefinition Tag, S7Address Address)> tags, int maxBytes)
    {
        var ranges = new List<ReadRange>();
        foreach (var db in tags.GroupBy(x => x.Address.Db))
        {
            ReadRange? current = null;
            foreach (var item in db.OrderBy(x => x.Address.ByteOffset))
            {
                var start = item.Address.ByteOffset;
                var end = start + item.Address.Length;
                if (current is not null &&
                    start <= current.End + MaxGapBytes &&
                    Math.Max(end, current.End) - current.Start <= maxBytes)
                {
                    current.Tags.Add(item);
                    current.End = Math.Max(current.End, end);
                    continue;
                }
                current = new ReadRange(db.Key, start) { End = end };
                current.Tags.Add(item);
                ranges.Add(current);
            }
        }
        return ranges;
    }

    private static double Decode(TagDefinition tag, S7Address address, byte[] data, int offset)
    {
        var span = data.AsSpan(offset);
        return tag.DataType switch {
            TagDataType.Bool => (span[0] >> address.Bit & 1) != 0 ? 1 : 0,
            TagDataType.Word or TagDataType.UInt16 => BinaryPrimitives.ReadUInt16BigEndian(span),
            TagDataType.Int16 => BinaryPrimitives.ReadInt16BigEndian(span),
            TagDataType.UInt32 => BinaryPrimitives.ReadUInt32BigEndian(span),
            TagDataType.Int32 => BinaryPrimitives.ReadInt32BigEndian(span),
            TagDataType.Float32 => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(span)),
            _ => throw new NotSupportedException("Unsupported S7 type") };
    }

    private static TagValueSnapshot Good(DeviceDefinition device, TagDefinition tag, double raw) =>
        new(tag.Id, device.Id, raw, EngineeringValue.From(raw, tag), TagQuality.Good, DateTimeOffset.UtcNow, "Siemens S7 TCP", null);

    private static TagValueSnapshot Bad(DeviceDefinition device, TagDefinition tag, string error) =>
        new(tag.Id, device.Id, null, null, TagQuality.Bad, DateTimeOffset.UtcNow, "Siemens S7 TCP", error);

    public static (int Rack,int Slot) DecodeRackSlot(int encoded) => (encoded/32,encoded%32);

    internal sealed class ReadRange(int db, int start)
    {
        public int Db { get; } = db;
        public int Start { get; } = start;
        public int End { get; set; }
        public int Length => End - Start;
        public List<(TagDefinition Tag, S7Address Address)> Tags { get; } = [];
    }
}
