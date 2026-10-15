namespace Prognode.Contracts.Tags;

public enum TagDataType
{
    Bool,
    Word,
    UInt16,
    Int16,
    UInt32,
    Int32,
    Float32,
    // 64-bit values (energy counters, double-precision readings). Stored as double, so integers
    // above 2^53 lose their last digits.
    Int64,
    UInt64,
    Float64
}

public enum ModbusByteOrder
{
    ABCD,
    CDAB,
    BADC,
    DCBA
}

public sealed record TagDefinition(
    Guid Id,
    Guid DeviceId,
    string Name,
    string Address,
    TagDataType DataType,
    int? BitIndex,
    ModbusByteOrder ByteOrder,
    string Unit,
    double Scale,
    double Offset,
    int DecimalPlaces,
    bool Enabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);
