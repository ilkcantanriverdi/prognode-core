using Prognode.Contracts.Tags;

namespace Prognode.Web.Models;

public sealed record CreateOrUpdateTagRequest(
    Guid DeviceId,
    string? Name,
    string? Address,
    TagDataType DataType,
    int? BitIndex,
    ModbusByteOrder ByteOrder,
    string? Unit,
    double Offset,
    int DecimalPlaces
);
