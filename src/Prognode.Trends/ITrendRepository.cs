using Prognode.Contracts.Trends;

namespace Prognode.Trends;

public interface ITrendRepository
{
    Task<IReadOnlyList<TrendDefinition>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task<TrendDefinition?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        TrendDefinition trend,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        TrendDefinition trend,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
