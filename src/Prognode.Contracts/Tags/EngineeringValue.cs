namespace Prognode.Contracts.Tags;

/// <summary>
/// Single source of truth for converting a protocol raw value into the engineering value
/// stored by Core. Every protocol reader must use this so that the same Tag definition
/// produces the same value regardless of the connector.
/// </summary>
/// <remarks>
/// DecimalPlaces is an implied-decimal divisor only for integer register types
/// (UInt16/Int16/UInt32/Int32). For Float32 it is a display precision setting and must not
/// change the value. Bool and Word are returned unchanged (no divisor, scale or offset).
/// </remarks>
public static class EngineeringValue
{
    public static double From(double raw, TagDefinition tag) =>
        From(raw, tag.DataType, tag.DecimalPlaces, tag.Scale, tag.Offset);

    public static double From(double raw, TagDataType dataType, int decimalPlaces, double scale, double offset)
    {
        if (dataType is TagDataType.Bool or TagDataType.Word)
            return raw;

        var value = UsesImpliedDecimals(dataType)
            ? raw / Math.Pow(10, decimalPlaces)
            : raw;

        return value * scale + offset;
    }

    public static bool UsesImpliedDecimals(TagDataType dataType) =>
        dataType is TagDataType.UInt16 or TagDataType.Int16 or TagDataType.UInt32 or TagDataType.Int32;
}
