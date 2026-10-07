using System.Buffers.Binary;
using Prognode.Contracts.Devices;
using Prognode.Contracts.Tags;
using Prognode.Protocols.Abstractions;

namespace Prognode.Protocols.Modbus;

public sealed class ModbusTagReader(
    ModbusTcpRegisterClient client) : ITagReader
{
    private const int DefaultTimeoutMs = 2000;
    private const int MaxGapRegisters = 8;
    private const int MaxGapBits = 32;

    public bool CanHandle(DeviceDefinition device) =>
        string.Equals(
            device.Protocol,
            "Modbus TCP",
            StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<TagValueSnapshot>> ReadAsync(
        DeviceDefinition device,
        IReadOnlyList<TagDefinition> tags,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(device.Host) ||
            device.Port is null ||
            device.UnitId is null)
        {
            throw new InvalidOperationException(
                $"Device '{device.Name}' has incomplete Modbus TCP settings.");
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

        var blocks = PlanBlocks(planned);
        var values = new List<TagValueSnapshot>(enabled.Length);

        foreach (var block in blocks)
        {
            if (block.Area is ModbusArea.Coil or ModbusArea.DiscreteInput)
            {
                var bits = block.Area == ModbusArea.Coil
                    ? await client.ReadCoilsAsync(
                        device.Host,
                        device.Port.Value,
                        device.UnitId.Value,
                        block.Start,
                        block.Quantity,
                        DefaultTimeoutMs,
                        cancellationToken)
                    : await client.ReadDiscreteInputsAsync(
                        device.Host,
                        device.Port.Value,
                        device.UnitId.Value,
                        block.Start,
                        block.Quantity,
                        DefaultTimeoutMs,
                        cancellationToken);

                foreach (var item in block.Tags)
                {
                    var relative = item.Address.ZeroBasedOffset - block.Start;
                    var raw = bits[relative] ? 1.0 : 0.0;
                    values.Add(Snapshot(device, item.Tag, raw));
                }

                continue;
            }

            var registers = block.Area == ModbusArea.InputRegister
                ? await client.ReadInputRegistersAsync(
                    device.Host,
                    device.Port.Value,
                    device.UnitId.Value,
                    block.Start,
                    block.Quantity,
                    DefaultTimeoutMs,
                    cancellationToken)
                : await client.ReadHoldingRegistersAsync(
                    device.Host,
                    device.Port.Value,
                    device.UnitId.Value,
                    block.Start,
                    block.Quantity,
                    DefaultTimeoutMs,
                    cancellationToken);

            foreach (var item in block.Tags)
            {
                var relative = item.Address.ZeroBasedOffset - block.Start;
                var raw = Decode(registers, relative, item.Tag);
                values.Add(Snapshot(device, item.Tag, raw));
            }
        }

        return values;
    }

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
