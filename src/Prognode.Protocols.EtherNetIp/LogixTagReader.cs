using System.Buffers.Binary;
using Prognode.Contracts.Devices;
using Prognode.Contracts.Tags;
using Prognode.Protocols.Abstractions;

namespace Prognode.Protocols.EtherNetIp;

/// <summary>
/// Reads Logix controller tags over EtherNet/IP. Tags are read by name, up to 24 per Multiple Service
/// Packet; each reply carries its own status, so a missing tag makes only its own Tag Bad. The value
/// is decoded by the data type the controller reports; a Tag whose data type does not fit it is Bad
/// with the controller's type named. UnitId holds the CPU slot (255 = direct, no backplane route).
/// </summary>
public sealed class LogixTagReader(EnipClient client) : ITagReader
{
    public const string Protocol = "EtherNet/IP";
    private const int TimeoutMs = 3000;

    public LogixTagReader() : this(new EnipClient()) { }

    public bool CanHandle(DeviceDefinition device) => device.Protocol.Equals(Protocol, StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<TagValueSnapshot>> ReadAsync(DeviceDefinition device, IReadOnlyList<TagDefinition> tags, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(device.Host) || device.Port is null)
            throw new InvalidOperationException($"Device '{device.Name}' has incomplete EtherNet/IP settings.");
        var slot = device.UnitId ?? 0;
        var result = new List<TagValueSnapshot>();
        var planned = new List<(TagDefinition Tag, LogixTagPath Path)>();
        foreach (var tag in tags.Where(t => t.Enabled))
        {
            try { planned.Add((tag, LogixTagPath.Parse(tag.Address, tag.DataType))); }
            catch (ArgumentException ex) { result.Add(Bad(device, tag, ex.Message)); }
        }
        foreach (var batch in PlanBatches(planned))
        {
            CipReadReply[] replies;
            try
            {
                replies = await client.ReadTagsAsync(device.Host!, device.Port!.Value, slot, batch.Select(x => x.Path.Path).ToArray(), TimeoutMs, cancellationToken);
            }
            catch (CipException ex)
            {
                // A route or packet-level refusal: the controller answered, but none of these Tags could be read.
                result.AddRange(batch.Select(x => Bad(device, x.Tag, ex.Message)));
                continue;
            }
            for (var i = 0; i < batch.Count; i++)
            {
                var (tag, path) = batch[i];
                var reply = replies[i];
                if (reply.Status != 0)
                {
                    result.Add(Bad(device, tag, $"{path.Name}: " + EnipClient.Refused(reply.Status, reply.Extended).Message));
                    continue;
                }
                try { result.Add(Good(device, tag, Decode(tag.DataType, path, reply))); }
                catch (ArgumentException ex) { result.Add(Bad(device, tag, ex.Message)); }
            }
        }
        return result;
    }

    /// <summary>Groups reads so each Multiple Service Packet stays within one unconnected message.</summary>
    internal static List<List<(TagDefinition Tag, LogixTagPath Path)>> PlanBatches(IEnumerable<(TagDefinition Tag, LogixTagPath Path)> tags)
    {
        var batches = new List<List<(TagDefinition, LogixTagPath)>>();
        List<(TagDefinition, LogixTagPath)>? current = null;
        var size = 0;
        foreach (var item in tags)
        {
            var cost = 2 + 4 + item.Path.Path.Length + 2; // offset + Read Tag header + path + element count
            if (current is null || current.Count >= EnipClient.MaxReadsPerPacket || size + cost > EnipClient.MaxRequestBytes)
            {
                current = [];
                batches.Add(current);
                size = 2 + 6 + 2;                              // Multiple Service header + count
            }
            current.Add(item);
            size += cost;
        }
        return batches;
    }

    /// <summary>CIP elementary data type codes.</summary>
    internal static string TypeName(ushort code) => code switch
    {
        0xC1 => "BOOL", 0xC2 => "SINT", 0xC3 => "INT", 0xC4 => "DINT", 0xC5 => "LINT",
        0xC6 => "USINT", 0xC7 => "UINT", 0xC8 => "UDINT", 0xC9 => "ULINT", 0xCA => "REAL", 0xCB => "LREAL",
        0xD1 => "BYTE", 0xD2 => "WORD", 0xD3 => "DWORD", 0xD4 => "LWORD", 0x02A0 => "a structure",
        _ => $"type 0x{code:X4}",
    };

    internal static double Decode(TagDataType type, LogixTagPath path, CipReadReply reply)
    {
        var code = reply.TypeCode;
        var d = reply.Data;
        var size = code switch
        {
            0xC1 or 0xC2 or 0xC6 or 0xD1 => 1,
            0xC3 or 0xC7 or 0xD2 => 2,
            0xC4 or 0xC8 or 0xCA or 0xD3 => 4,
            0xC5 or 0xC9 or 0xCB or 0xD4 => 8,
            _ => throw new ArgumentException($"{path.Name} is {TypeName(code)}; read one of its members (e.g. {path.Name}.Member) instead."),
        };
        if (d.Length < size) throw new ArgumentException($"{path.Name}: the controller sent too little data for {TypeName(code)}.");
        var span = d.AsSpan(0, size);
        var signed = code is 0xC2 or 0xC3 or 0xC4 or 0xC5;
        var real = code is 0xCA or 0xCB;
        long integer = code switch
        {
            0xC1 => span[0] != 0 ? 1 : 0,
            0xC2 => (sbyte)span[0],
            0xC6 or 0xD1 => span[0],
            0xC3 => BinaryPrimitives.ReadInt16LittleEndian(span),
            0xC7 or 0xD2 => BinaryPrimitives.ReadUInt16LittleEndian(span),
            0xC4 => BinaryPrimitives.ReadInt32LittleEndian(span),
            0xC8 or 0xD3 => BinaryPrimitives.ReadUInt32LittleEndian(span),
            0xC5 or 0xC9 or 0xD4 => BinaryPrimitives.ReadInt64LittleEndian(span),
            _ => 0,
        };
        var unsigned64 = code is 0xC9 or 0xD4;

        if (path.Bit is { } bit)
        {
            if (code == 0xC1 || real) throw new ArgumentException($"{path.Name} is {TypeName(code)}; a .bit address needs an integer tag (SINT, INT, DINT, LINT).");
            if (bit >= size * 8) throw new ArgumentException($"{path.Name} is {TypeName(code)} with {size * 8} bits; bit {bit} does not exist.");
            return ((ulong)integer >> bit & 1) != 0 ? 1 : 0;
        }

        bool fits = type switch
        {
            TagDataType.Bool => code == 0xC1,
            TagDataType.Int16 => code is 0xC2 or 0xC3,
            TagDataType.UInt16 => code is 0xC6 or 0xC7 or 0xD1 or 0xD2,
            TagDataType.Word => code is 0xC2 or 0xC3 or 0xC6 or 0xC7 or 0xD1 or 0xD2,
            TagDataType.Int32 => code is 0xC2 or 0xC3 or 0xC4 or 0xC6 or 0xC7,
            TagDataType.UInt32 => code is 0xC6 or 0xC7 or 0xC8 or 0xD1 or 0xD2 or 0xD3,
            TagDataType.Float32 => code == 0xCA,
            TagDataType.Int64 => signed || code is 0xC6 or 0xC7 or 0xC8,
            TagDataType.UInt64 => code is 0xC6 or 0xC7 or 0xC8 or 0xC9 or 0xD1 or 0xD2 or 0xD3 or 0xD4,
            TagDataType.Float64 => real,
            _ => false,
        };
        if (!fits)
            throw new ArgumentException($"{path.Name} is {TypeName(code)} in the controller; set the Tag data type to {Suggest(code)}.");
        if (code == 0xCA) return BinaryPrimitives.ReadSingleLittleEndian(span);
        if (code == 0xCB) return BinaryPrimitives.ReadDoubleLittleEndian(span);
        if (type == TagDataType.Word) return (ushort)integer;
        return unsigned64 ? (ulong)integer : integer;
    }

    private static string Suggest(ushort code) => code switch
    {
        0xC1 => "BOOL", 0xC2 or 0xC3 => "INT16", 0xC6 or 0xC7 or 0xD1 or 0xD2 => "UINT16", 0xC4 => "INT32",
        0xC8 or 0xD3 => "UINT32", 0xC5 => "INT64", 0xC9 or 0xD4 => "UINT64", 0xCA => "FLOAT32 / REAL", 0xCB => "FLOAT64 / LREAL",
        _ => "a matching type",
    };

    private static TagValueSnapshot Good(DeviceDefinition device, TagDefinition tag, double raw) =>
        new(tag.Id, device.Id, raw, EngineeringValue.From(raw, tag), TagQuality.Good, DateTimeOffset.UtcNow, Protocol, null);

    private static TagValueSnapshot Bad(DeviceDefinition device, TagDefinition tag, string error) =>
        new(tag.Id, device.Id, null, null, TagQuality.Bad, DateTimeOffset.UtcNow, Protocol, error);
}

/// <summary>Checks Logix tag names; the same tag may be read as a value and as single bits.</summary>
public sealed class LogixTagValidator : ITagDefinitionValidator
{
    public bool CanHandle(DeviceDefinition device) => device.Protocol.Equals(LogixTagReader.Protocol, StringComparison.OrdinalIgnoreCase);

    public void Validate(TagDefinition candidate, IReadOnlyList<TagDefinition> existingTags)
    {
        if (candidate.BitIndex is not null) throw new ArgumentException("For EtherNet/IP put the bit in the address (Status.5). Leave BitIndex empty.");
        LogixTagPath.Parse(candidate.Address, candidate.DataType);
    }
}

/// <summary>A recent good poll counts; otherwise the device is asked for its identity (ListIdentity).</summary>
public sealed class LogixHealthProbe(EnipClient client) : IDeviceHealthProbe
{
    public bool CanHandle(DeviceDefinition device) => device.Protocol.Equals(LogixTagReader.Protocol, StringComparison.OrdinalIgnoreCase);

    public async Task<DeviceHealthResult> CheckAsync(DeviceDefinition device, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(device.Host) || device.Port is null)
            return new(false, "Device TCP configuration is incomplete.", 0);
        if (client.HasRecentSuccess(device.Host, device.Port.Value, TimeSpan.FromSeconds(30)))
            return new(true, "Polling connection is healthy.", 0);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var name = await client.ListIdentityAsync(device.Host, device.Port.Value, 1500, cancellationToken);
            return new(true, string.IsNullOrEmpty(name) ? "EtherNet/IP device answered." : $"{name} answered.", Math.Round(timer.Elapsed.TotalMilliseconds, 1));
        }
        catch (Exception ex) when (ex is IOException or System.Net.Sockets.SocketException or OperationCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return new(false, ex is OperationCanceledException ? "No answer within 1500 ms." : ex.Message, Math.Round(timer.Elapsed.TotalMilliseconds, 1));
        }
    }
}
