using Prognode.Contracts.Historian;

namespace Prognode.Historian;

public interface IHistorianConfigurationRepository
{
    Task<IReadOnlyList<HistorianRecordingConfiguration>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task<HistorianRecordingConfiguration?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<HistorianRecordingConfiguration?> GetByTagIdAsync(
        Guid tagId,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        HistorianRecordingConfiguration configuration,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        HistorianRecordingConfiguration configuration,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default);
}
