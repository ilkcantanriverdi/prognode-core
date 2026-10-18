using Prognode.Contracts.Equipment;
using Prognode.Core.Devices;
using Prognode.Core.Tags;

namespace Prognode.Trends;

public sealed class EquipmentService(
    IEquipmentRepository repository,
    ITagRepository tags,
    IDeviceRepository devices)
{
    public const int MaxItems = 8;
    public static readonly string[] Views = ["VALUE", "CHART"];

    public Task<IReadOnlyList<EquipmentDefinition>> GetAllAsync(CancellationToken ct = default) =>
        repository.GetAllAsync(ct);

    public async Task<EquipmentDefinition> CreateAsync(string? name, Guid? deviceId,
        IReadOnlyList<EquipmentItem>? items, CancellationToken ct = default)
    {
        var item = await BuildAsync(Guid.NewGuid(), name, deviceId, items, DateTimeOffset.UtcNow, ct);
        await repository.AddAsync(item, ct);
        return item;
    }

    public async Task<EquipmentDefinition> UpdateAsync(Guid id, string? name, Guid? deviceId,
        IReadOnlyList<EquipmentItem>? items, CancellationToken ct = default)
    {
        var existing = await repository.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException("Equipment was not found.");
        var item = await BuildAsync(id, name, deviceId, items, existing.CreatedAt, ct);
        await repository.UpdateAsync(item, ct);
        return item;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        if (await repository.GetByIdAsync(id, ct) is null) return false;
        await repository.DeleteAsync(id, ct); // Its Tags stay; only the card goes.
        return true;
    }

    private async Task<EquipmentDefinition> BuildAsync(Guid id, string? name, Guid? deviceId,
        IReadOnlyList<EquipmentItem>? items, DateTimeOffset createdAt, CancellationToken ct)
    {
        var cleanName = (name ?? "").Trim();
        if (cleanName.Length < 1) throw new ArgumentException("Equipment name is required.");
        if (cleanName.Length > 100) throw new ArgumentException("Equipment name cannot exceed 100 characters.");

        if (deviceId == Guid.Empty) deviceId = null;
        if (deviceId is Guid device && await devices.GetByIdAsync(device, ct) is null)
            throw new ArgumentException("The selected device was not found.");

        var clean = (items ?? []).Where(x => x.TagId != Guid.Empty).DistinctBy(x => x.TagId)
            .Select(x => new EquipmentItem(x.TagId, (x.View ?? "").Trim().ToUpperInvariant()))
            .ToArray();
        if (clean.Length is < 1 or > MaxItems)
            throw new ArgumentException($"Equipment must show between 1 and {MaxItems} Tags.");
        if (clean.Any(x => !Views.Contains(x.View)))
            throw new ArgumentException("Each Tag is shown as VALUE or CHART.");
        foreach (var x in clean)
            _ = await tags.GetByIdAsync(x.TagId, ct) ?? throw new ArgumentException("One or more selected Tags were not found.");

        return new EquipmentDefinition(id, cleanName, deviceId, clean, createdAt, DateTimeOffset.UtcNow);
    }
}
