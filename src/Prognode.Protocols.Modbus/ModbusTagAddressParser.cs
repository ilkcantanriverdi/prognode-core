using Prognode.Contracts.Tags;

namespace Prognode.Protocols.Modbus;

public enum ModbusArea
{
    Coil,
    DiscreteInput,
    InputRegister,
    HoldingRegister
}

public sealed record ParsedModbusTagAddress(
    ModbusArea Area,
    int ZeroBasedOffset,
    int Width,
    byte FunctionCode)
{
    // Compatibility aliases for code/tests written against the RC6.1 Holding Register parser.
    public int RegisterOffset => ZeroBasedOffset;
    public int RegisterCount => Width;
    public bool IsBitArea => Area is ModbusArea.Coil or ModbusArea.DiscreteInput;
    public int MaxReadQuantity => IsBitArea ? 2000 : 125;
}

public static class ModbusTagAddressParser
{
    public static ParsedModbusTagAddress Parse(string address, TagDataType dataType)
    {
        if (!int.TryParse((address ?? string.Empty).Trim(), out var numeric))
            throw new ArgumentException($"Invalid Modbus address '{address}'.");

        var (area, firstReference, functionCode) = numeric switch
        {
            >= 1 and <= 9999 => (ModbusArea.Coil, 1, (byte)0x01),
            >= 10001 and <= 19999 => (ModbusArea.DiscreteInput, 10001, (byte)0x02),
            >= 30001 and <= 39999 => (ModbusArea.InputRegister, 30001, (byte)0x04),
            >= 40001 and <= 49999 => (ModbusArea.HoldingRegister, 40001, (byte)0x03),
            _ => throw new ArgumentException(
                $"Address {numeric} is outside supported Modbus ranges 00001..09999, 10001..19999, 30001..39999 and 40001..49999.")
        };

        var bitArea = area is ModbusArea.Coil or ModbusArea.DiscreteInput;
        if (bitArea && dataType != TagDataType.Bool)
        {
            throw new ArgumentException(
                $"{area} addresses support BOOL datatype only.");
        }

        if (area == ModbusArea.InputRegister && dataType == TagDataType.Bool)
        {
            throw new ArgumentException(
                "Input Register addresses support WORD / UInt16 / Int16 / UInt32 / Int32 / Float32, not BOOL bit access.");
        }

        var width = dataType switch
        {
            TagDataType.UInt32 or TagDataType.Int32 or TagDataType.Float32 => 2,
            _ => 1
        };

        if (bitArea)
            width = 1;

        var zeroBasedOffset = numeric - firstReference;
        if (zeroBasedOffset + width > 9999)
        {
            throw new ArgumentException(
                $"Address {numeric} plus datatype width exceeds the selected Modbus data area.");
        }

        return new ParsedModbusTagAddress(
            area,
            zeroBasedOffset,
            width,
            functionCode);
    }
}
