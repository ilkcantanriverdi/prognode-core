namespace Prognode.Contracts.Tags;

/// <summary>Whether a number read from a device fits the Tag's data type.</summary>
public static class TagDataRange
{
    public static bool Fits(double value, TagDataType type) => type switch
    {
        TagDataType.Bool => value is 0 or 1,
        TagDataType.Word or TagDataType.UInt16 => IsInteger(value) && value is >= 0 and <= ushort.MaxValue,
        TagDataType.Int16 => IsInteger(value) && value is >= short.MinValue and <= short.MaxValue,
        TagDataType.UInt32 => IsInteger(value) && value is >= 0 and <= uint.MaxValue,
        TagDataType.Int32 => IsInteger(value) && value is >= int.MinValue and <= int.MaxValue,
        TagDataType.Int64 => IsInteger(value) && value is >= long.MinValue and <= long.MaxValue,
        TagDataType.UInt64 => IsInteger(value) && value is >= 0 and <= ulong.MaxValue,
        TagDataType.Float32 => Math.Abs(value) <= float.MaxValue,
        TagDataType.Float64 => double.IsFinite(value),
        _ => false
    };

    public static bool IsNumeric(TagDataType type) =>
        type is not (TagDataType.Bool or TagDataType.Word);

    private static bool IsInteger(double value) => Math.Truncate(value) == value;
}
