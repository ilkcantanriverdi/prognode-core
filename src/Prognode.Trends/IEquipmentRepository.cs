using Prognode.Contracts.Equipment;

namespace Prognode.Trends;

public interface IEquipmentRepository
{
    Task<IReadOnlyList<EquipmentDefinition>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<EquipmentDefinition?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(EquipmentDefinition equipment, CancellationToken cancellationToken = default);
    Task UpdateAsync(EquipmentDefinition equipment, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
