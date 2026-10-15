using System.Buffers.Binary;
using System.Collections.Concurrent;
using Prognode.Contracts.Devices;
using Prognode.Contracts.Tags;
using Prognode.Protocols.Abstractions;

namespace Prognode.Protocols.Modbus;

/// <summary>Reads Modbus Tags over TCP or, with a <see cref="ModbusRtuRegisterClient"/>, over RS-485.</summary>
public sealed class ModbusTagReader(
    IModbusRegisterClient client,
    string protocol = ModbusTagReader.TcpProtocol) : ITagReader
{
    public const string TcpProtocol = "Modbus TCP";
    public const string RtuProtocol = "Modbus RTU";

    private const int DefaultTimeoutMs = 2000;
    // Only merge strictly contiguous addresses (review Y2): reading an undefined gap address makes
    // the PLC answer exception 0x02 for the whole block and every Tag in it.
    private const int MaxGapRegisters = 0;
    private const int MaxGapBits = 0;

    /// <summary>A Tag the PLC rejected is read on its own for this long, so one bad address does
    /// not fail its block and force a Tag-by-Tag re-read on every poll.</summary>
    private static readonly TimeSpan RejectedQuarantine = TimeSpan.FromSeconds(60);
    private readonly ConcurrentDictionary<Guid, DateTimeOffset> _rejectedUntil = new();

    public bool CanHandle(DeviceDefinition device) =>
        string.Equals(
            device.Protocol,
            protocol,
            StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<TagValueSnapshot>> ReadAsync(
        DeviceDefinition device,
        IReadOnlyList<TagDefinition> tags,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(device.Host) ||
            (device.Port is null && protocol == TcpProtocol) ||
            device.UnitId is null)
        {
            throw new InvalidOperationException(
                $"Device '{device.Name}' has incomplete {protocol} settings.");
        }

        var enabled = tags
            .Where(x => x.Enabled)
            .ToArray();

        if (enabled.Length == 0)
            return Array.Empty<TagValueSnapshot>();

        var planned = enabled
            .Select(tag =>
                new PlannedTag(
                    tag,
                    ModbusTagAddressParser.Parse(
                        tag.Address,
                        tag.DataType)))
            .OrderBy(x => x.Address.FunctionCode)
            .ThenBy(x => x.Address.ZeroBasedOffset)
            .ToArray();

        var now = DateTimeOffset.UtcNow;
        var quarantined = planned
            .Where(x => _rejectedUntil.TryGetValue(x.Tag.Id, out var until) && until > now)
            .ToArray();
        var blocks = PlanBlocks(planned.Except(quarantined).ToArray())
            .Concat(quarantined.Select(x => new ReadBlock(x.Address.Area, x.Address.FunctionCode,
                x.Address.ZeroBasedOffset, x.Address.Width, [x])))
            .ToArray();
        var values = new List<TagValueSnapshot>(enabled.Length);

        foreach (var block in blocks)
        {
            try
            {
                values.AddRange(await ReadBlockAsync(device, block, cancellationToken));
            }
            catch (ModbusExceptionResponseException) when (block.Tags.Count > 1)
            {
                // The PLC rejected the block (e.g. 0x02 for one undefined address). Re-read Tag by
                // Tag so only the offending addresses go BAD (review Y2).
                foreach (var item in block.Tags)
                {
                    var single = new ReadBlock(block.Area, block.FunctionCode,
                        item.Address.ZeroBasedOffset, item.Address.Width, [item]);
                    try
                    {
                        values.AddRange(await ReadBlockAsync(device, single, cancellationToken));
                    }
                    catch (ModbusExceptionResponseException ex)
                    {
                        _rejectedUntil[item.Tag.Id] = DateTimeOffset.UtcNow + RejectedQuarantine;
                        values.Add(BadSnapshot(device, item.Tag, ex.Message));
                    }
                }
            }
            catch (ModbusExceptionResponseException ex)
            {
                _rejectedUntil[block.Tags[0].Tag.Id] = DateTimeOffset.UtcNow + RejectedQuarantine;
                values.Add(BadSnapshot(device, block.Tags[0].Tag, ex.Message));
            }
        }

        return values;
    }

    private async Task<IReadOnlyList<TagValueSnapshot>> ReadBlockAsync(
        DeviceDefinition device,
        ReadBlock block,
        CancellationToken cancellationToken)
    {
        var host = device.Host!;
        var port = device.Port ?? 0;
        var unitId = device.UnitId!.Value;
        var result = new List<TagValueSnapshot>(block.Tags.Count);
        foreach (var item in block.Tags) _rejectedUntil.TryRemove(item.Tag.Id, out _);

        if (block.Area is ModbusArea.Coil or ModbusArea.DiscreteInput)
        {
            var bits = block.Area == ModbusArea.Coil
                ? await client.ReadCoilsAsync(host, port, unitId, block.Start, block.Quantity,
                    DefaultTimeoutMs, cancellationToken)
                : await client.ReadDiscreteInputsAsync(host, port, unitId, block.Start, block.Quantity,
                    DefaultTimeoutMs, cancellationToken);

            foreach (var item in block.Tags)
            {
                var relative = item.Address.ZeroBasedOffset - block.Start;
                result.Add(Snapshot(device, item.Tag, bits[relative] ? 1.0 : 0.0));
            }

            return result;
        }

        var registers = block.Area == ModbusArea.InputRegister
            ? await client.ReadInputRegistersAsync(host, port, unitId, block.Start, block.Quantity,
                DefaultTimeoutMs, cancellationToken)
            : await client.ReadHoldingRegistersAsync(host, port, unitId, block.Start, block.Quantity,
                DefaultTimeoutMs, cancellationToken);

        foreach (var item in block.Tags)
        {
            var relative = item.Address.ZeroBasedOffset - block.Start;
            result.Add(Snapshot(device, item.Tag, Decode(registers, relative, item.Tag)));
        }

        return result;
    }

    private static TagValueSnapshot BadSnapshot(DeviceDefinition device, TagDefinition tag, string error) =>
        new(tag.Id, device.Id, null, null, TagQuality.Bad, DateTimeOffset.UtcNow, "Modbus TCP", error);

    private static TagValueSnapshot Snapshot(
        DeviceDefinition device,
        TagDefinition tag,
        double raw)
    {
        var value = ConvertToEngineeringValue(raw, tag);
        return new TagValueSnapshot(
            TagId: tag.Id,
            DeviceId: device.Id,
            RawValue: raw,
            Value: value,
            Quality: TagQuality.Good,
            Timestamp: DateTimeOffset.UtcNow,
            Source: "Modbus TCP",
            Error: null);
    }

    private static double ConvertToEngineeringValue(
        double raw,
        TagDefinition tag) =>
        EngineeringValue.From(raw, tag);

    private static IReadOnlyList<ReadBlock> PlanBlocks(
        IReadOnlyList<PlannedTag> tags)
    {
        var result = new List<ReadBlock>();

        foreach (var group in tags.GroupBy(x => new { x.Address.Area, x.Address.FunctionCode }))
        {
            var ordered = group.OrderBy(x => x.Address.ZeroBasedOffset).ToArray();
            if (ordered.Length == 0)
                continue;

            var currentTags = new List<PlannedTag>();
            var currentStart = -1;
            var currentEndExclusive = -1;
            var maxGap = group.Key.Area is ModbusArea.Coil or ModbusArea.DiscreteInput
                ? MaxGapBits
                : MaxGapRegisters;
            var maxQuantity = group.Key.Area is ModbusArea.Coil or ModbusArea.DiscreteInput
                ? 2000
                : 125;

            foreach (var tag in ordered)
            {
                var start = tag.Address.ZeroBasedOffset;
                var endExclusive = start + tag.Address.Width;

                if (currentTags.Count == 0)
                {
                    currentStart = start;
                    currentEndExclusive = endExclusive;
                    currentTags.Add(tag);
                    continue;
                }

                var gap = start - currentEndExclusive;
                var proposedEnd = Math.Max(currentEndExclusive, endExclusive);
                var proposedQuantity = proposedEnd - currentStart;

                if (gap <= maxGap && proposedQuantity <= maxQuantity)
                {
                    currentTags.Add(tag);
                    currentEndExclusive = proposedEnd;
                    continue;
                }

                result.Add(new ReadBlock(
                    group.Key.Area,
                    group.Key.FunctionCode,
                    currentStart,
                    currentEndExclusive - currentStart,
                    currentTags.ToArray()));

                currentTags = [tag];
                currentStart = start;
                currentEndExclusive = endExclusive;
            }

            if (currentTags.Count > 0)
            {
                result.Add(new ReadBlock(
                    group.Key.Area,
                    group.Key.FunctionCode,
                    currentStart,
                    currentEndExclusive - currentStart,
                    currentTags.ToArray()));
            }
        }

        return result
            .OrderBy(x => x.FunctionCode)
            .ThenBy(x => x.Start)
            .ToArray();
    }

    private static double Decode(
        ushort[] registers,
        int index,
        TagDefinition tag)
    {
        return tag.DataType switch
        {
            TagDataType.Bool =>
                DecodeBit(
                    registers[index],
                    tag.BitIndex ?? 0),

            TagDataType.Word =>
                registers[index],

            TagDataType.UInt16 =>
                registers[index],

            TagDataType.Int16 =>
                unchecked((short)registers[index]),

            TagDataType.UInt32 =>
                DecodeUInt32(
                    registers[index],
                    registers[index + 1],
                    tag.ByteOrder),

            TagDataType.Int32 =>
                DecodeInt32(
                    registers[index],
                    registers[index + 1],
                    tag.ByteOrder),

            TagDataType.Float32 =>
                DecodeFloat32(
                    registers[index],
                    registers[index + 1],
                    tag.ByteOrder),

            TagDataType.Int64 =>
                BinaryPrimitives.ReadInt64BigEndian(Reorder(registers.AsSpan(index, 4), tag.ByteOrder)),

            TagDataType.UInt64 =>
                BinaryPrimitives.ReadUInt64BigEndian(Reorder(registers.AsSpan(index, 4), tag.ByteOrder)),

            TagDataType.Float64 =>
                BitConverter.Int64BitsToDouble(
                    BinaryPrimitives.ReadInt64BigEndian(Reorder(registers.AsSpan(index, 4), tag.ByteOrder))),

            _ => throw new NotSupportedException(
                $"Tag datatype '{tag.DataType}' is not supported.")
        };
    }

    private static double DecodeBit(ushort word, int bitIndex)
    {
        if (bitIndex is < 0 or > 15)
            throw new ArgumentOutOfRangeException(nameof(bitIndex));

        return (word & (1 << bitIndex)) != 0 ? 1.0 : 0.0;
    }

    private static uint DecodeUInt32(
        ushort first,
        ushort second,
        ModbusByteOrder order)
    {
        var bytes = Reorder(first, second, order);
        return BinaryPrimitives.ReadUInt32BigEndian(bytes);
    }

    private static int DecodeInt32(
        ushort first,
        ushort second,
        ModbusByteOrder order)
    {
        var bytes = Reorder(first, second, order);
        return BinaryPrimitives.ReadInt32BigEndian(bytes);
    }

    private static float DecodeFloat32(
        ushort first,
        ushort second,
        ModbusByteOrder order)
    {
        var bytes = Reorder(first, second, order);
        var bits = BinaryPrimitives.ReadInt32BigEndian(bytes);
        return BitConverter.Int32BitsToSingle(bits);
    }

    private static byte[] Reorder(
        ushort first,
        ushort second,
        ModbusByteOrder order)
    {
        var a = (byte)(first >> 8);
        var b = (byte)(first & 0xFF);
        var c = (byte)(second >> 8);
        var d = (byte)(second & 0xFF);

        return order switch
        {
            ModbusByteOrder.ABCD => [a, b, c, d],
            ModbusByteOrder.CDAB => [c, d, a, b],
            ModbusByteOrder.BADC => [b, a, d, c],
            ModbusByteOrder.DCBA => [d, c, b, a],
            _ => [a, b, c, d]
        };
    }

    /// <summary>
    /// Bytes of a 64-bit value in big-endian order. ABCD = registers as sent; CDAB = register order
    /// reversed (low word first); BADC = bytes swapped inside each register; DCBA = both. Same
    /// meaning as for 32-bit values.
    /// </summary>
    private static byte[] Reorder(ReadOnlySpan<ushort> words, ModbusByteOrder order)
    {
        var wordSwap = order is ModbusByteOrder.CDAB or ModbusByteOrder.DCBA;
        var byteSwap = order is ModbusByteOrder.BADC or ModbusByteOrder.DCBA;
        var bytes = new byte[words.Length * 2];
        for (var i = 0; i < words.Length; i++)
        {
            var word = words[wordSwap ? words.Length - 1 - i : i];
            bytes[i * 2] = byteSwap ? (byte)word : (byte)(word >> 8);
            bytes[i * 2 + 1] = byteSwap ? (byte)(word >> 8) : (byte)word;
        }
        return bytes;
    }

    private sealed record PlannedTag(
        TagDefinition Tag,
        ParsedModbusTagAddress Address);

    private sealed record ReadBlock(
        ModbusArea Area,
        byte FunctionCode,
        int Start,
        int Quantity,
        IReadOnlyList<PlannedTag> Tags);
}
