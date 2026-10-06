using Prognode.Contracts.Devices;

namespace Prognode.Core.Devices;

public interface IDeviceRepository
{
    Task<IReadOnlyList<DeviceDefinition>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task<DeviceDefinition?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        DeviceDefinition device,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        DeviceDefinition device,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<int> CountAsync(
        CancellationToken cancellationToken = default);
}
