using Prognode.Contracts.Historian;

namespace Prognode.Historian;

public interface IHistorianRepository
{
    Task WriteBatchAsync(
        IReadOnlyList<HistorianSample> samples,
        CancellationToken cancellationToken = default);

    Task<HistorianSeriesResult> QuerySeriesAsync(
        Guid tagId,
        DateTimeOffset from,
        DateTimeOffset to,
        int maxPoints,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HistorianSample>> QueryRawAsync(
        IReadOnlyList<Guid> tagIds,
        DateTimeOffset from,
        DateTimeOffset to,
        int maxRows,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<HistorianSample> StreamRawAsync(
        IReadOnlyList<Guid> tagIds,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HistorianSample>> QueryByBatchAsync(
        Guid batchId,
        IReadOnlyList<Guid> tagIds,
        int maxRows,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<HistorianTagStatus>> GetTagStatusesAsync(
        IReadOnlyList<HistorianRecordingConfiguration> configurations,
        CancellationToken cancellationToken = default);

    Task<HistorianStats> GetStatsAsync(
        CancellationToken cancellationToken = default);

    Task DeleteBeforeAsync(
        Guid tagId,
        DateTimeOffset cutoff,
        CancellationToken cancellationToken = default);

    Task DeleteTagDataAsync(
        Guid tagId,
        CancellationToken cancellationToken = default);
}
