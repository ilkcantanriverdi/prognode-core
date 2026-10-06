using Prognode.Contracts.Alarms;

namespace Prognode.Alarm;

public interface IAlarmEventRepository
{
    Task AddAsync(
        AlarmEventRecord item,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AlarmEventRecord>> GetRecentAsync(
        int limit,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AlarmOccurrenceRecord>> GetRecentOccurrencesAsync(
        int limit,
        CancellationToken cancellationToken = default);

    Task<long> CountOccurrencesAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AlarmOccurrenceRecord>> GetOccurrencePageAsync(
        int offset, int limit, string? search = null, string? priority = null,
        CancellationToken cancellationToken = default);

    Task<long> CountMatchingOccurrencesAsync(string? search, string? priority,
        CancellationToken cancellationToken = default);

    Task<int> DeleteOccurrencesAsync(IReadOnlyCollection<Guid> occurrenceIds,
        CancellationToken cancellationToken = default);

    Task LinkOccurrenceToBatchAsync(
        string alarmKey,
        DateTimeOffset activeAt,
        Guid batchId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AlarmOccurrenceRecord>> GetOccurrencesByBatchAsync(
        Guid batchId,
        int limit,
        CancellationToken cancellationToken = default);
}
