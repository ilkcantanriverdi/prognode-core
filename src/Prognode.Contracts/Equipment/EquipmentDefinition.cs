namespace Prognode.Contracts.Equipment;

/// <summary>
/// A machine or line the customer names (Line 2 · Press) and the Tags shown on its card, in order.
/// Optional: Tags work the same without equipment.
/// </summary>
public sealed record EquipmentDefinition(
    Guid Id,
    string Name,
    Guid? DeviceId,
    IReadOnlyList<EquipmentItem> Items,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

/// <summary>One Tag on the card. View: VALUE (number, unit) or CHART (value with its recent history).</summary>
public sealed record EquipmentItem(
    Guid TagId,
    string View
);
