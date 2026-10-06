using Prognode.Contracts.Tags;

namespace Prognode.Core.Tags;

public interface ITagRepository
{
    Task<IReadOnlyList<TagDefinition>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TagDefinition>> GetByDeviceIdAsync(
        Guid deviceId,
        CancellationToken cancellationToken = default);

    Task<TagDefinition?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        TagDefinition tag,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        TagDefinition tag,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<int> CountAsync(
        CancellationToken cancellationToken = default);
}
