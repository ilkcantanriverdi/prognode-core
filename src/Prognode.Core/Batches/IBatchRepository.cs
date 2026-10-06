using Prognode.Contracts.Batches;

namespace Prognode.Core.Batches;

public interface IBatchRepository
{
    Task<BatchRun?> GetCurrentAsync(CancellationToken cancellationToken = default);
    Task<BatchRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<BatchRun>> GetRecentAsync(int limit, CancellationToken cancellationToken = default);
    Task AddAsync(BatchRun batch, CancellationToken cancellationToken = default);
    Task UpdateAsync(BatchRun batch, CancellationToken cancellationToken = default);
}
