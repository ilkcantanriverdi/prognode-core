namespace Prognode.Contracts.Tags;

public enum TagDataType
{
    Bool,
    Word,
    UInt16,
    Int16,
    UInt32,
    Int32,
    Float32
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
