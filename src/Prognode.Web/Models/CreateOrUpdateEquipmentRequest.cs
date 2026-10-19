using Prognode.Contracts.Equipment;

namespace Prognode.Web.Models;

public sealed record CreateOrUpdateEquipmentRequest(
    string? Name,
    Guid? DeviceId,
    IReadOnlyList<EquipmentItem>? Items
);
