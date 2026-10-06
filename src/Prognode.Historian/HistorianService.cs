using Prognode.Contracts.Historian;
using Prognode.Core.Tags;

namespace Prognode.Historian;

public sealed class HistorianService(
    IHistorianRepository repository,
    IHistorianConfigurationRepository configurations,
    ITagRepository tags)
{
    private static readonly int[] AllowedIntervals = [10, 30, 60, 300];
    private static readonly int[] AllowedRetention = [30, 90, 365, 1825];

    public Task<HistorianStats> GetStatsAsync(
        CancellationToken cancellationToken = default) =>
        repository.GetStatsAsync(cancellationToken);

    // Fast enrollment lookup for frequent Trend Studio queries. Do not scan sample stats.
    public async Task<IReadOnlySet<Guid>> GetRecordingTagIdsAsync(
        CancellationToken cancellationToken = default)
    {
        var all = await configurations.GetAllAsync(cancellationToken);
        return all.Where(item => item.Enabled).Select(item => item.TagId).ToHashSet();
    }

    public async Task<IReadOnlyList<HistorianTagStatus>> GetConfigurationsAsync(
        CancellationToken cancellationToken = default)
    {
        var all = await configurations.GetAllAsync(cancellationToken);
        return await repository.GetTagStatusesAsync(all, cancellationToken);
    }

    public async Task<HistorianRecordingConfiguration> CreateConfigurationAsync(
        Guid tagId,
        int sampleIntervalSeconds,
        int retentionDays,
        CancellationToken cancellationToken = default)
    {
        _ = await tags.GetByIdAsync(tagId, cancellationToken)
            ?? throw new ArgumentException("Tag was not found.");

        if (await configurations.GetByTagIdAsync(tagId, cancellationToken) is not null)
            throw new ArgumentException("This Tag is already in Historian.");

        Validate(sampleIntervalSeconds, retentionDays);
        var now = DateTimeOffset.UtcNow;

        var item = new HistorianRecordingConfiguration(
            Guid.NewGuid(), tagId, sampleIntervalSeconds, retentionDays,
            true, now, now);

        await configurations.AddAsync(item, cancellationToken);
        return item;
    }

    public async Task<HistorianRecordingConfiguration> UpdateConfigurationAsync(
        Guid id,
        Guid tagId,
        int sampleIntervalSeconds,
        int retentionDays,
        CancellationToken cancellationToken = default)
    {
        var existing = await configurations.GetByIdAsync(id, cancellationToken)
            ?? throw new KeyNotFoundException("Historian configuration was not found.");

        _ = await tags.GetByIdAsync(tagId, cancellationToken)
            ?? throw new ArgumentException("Tag was not found.");

        var byTag = await configurations.GetByTagIdAsync(tagId, cancellationToken);
        if (byTag is not null && byTag.Id != id)
            throw new ArgumentException("This Tag is already in Historian.");

        Validate(sampleIntervalSeconds, retentionDays);

        var updated = existing with
        {
            TagId = tagId,
            SampleIntervalSeconds = sampleIntervalSeconds,
            RetentionDays = retentionDays,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        await configurations.UpdateAsync(updated, cancellationToken);
        return updated;
    }

    public async Task<bool> RemoveConfigurationAsync(
        Guid id,
        bool deleteData,
        CancellationToken cancellationToken = default)
    {
        var existing = await configurations.GetByIdAsync(id, cancellationToken);
        if (existing is null)
            return false;

        await configurations.DeleteAsync(id, cancellationToken);

        if (deleteData)
            await repository.DeleteTagDataAsync(existing.TagId, cancellationToken);

        return true;
    }

    public Task<HistorianSeriesResult> QuerySeriesAsync(
        Guid tagId,
        DateTimeOffset from,
        DateTimeOffset to,
        int maxPoints = 5000,
        CancellationToken cancellationToken = default)
    {
        if (to <= from)
            throw new ArgumentException("End time must be after start time.");

        return repository.QuerySeriesAsync(
            tagId, from, to, Math.Clamp(maxPoints, 100, 5000), cancellationToken);
    }

    public Task<IReadOnlyList<HistorianSample>> QueryRawAsync(
        IReadOnlyList<Guid> tagIds,
        DateTimeOffset from,
        DateTimeOffset to,
        int maxRows = 250_000,
        CancellationToken cancellationToken = default)
    {
        if (to <= from)
            throw new ArgumentException("End time must be after start time.");

        return repository.QueryRawAsync(
            tagIds, from, to, Math.Clamp(maxRows, 1, 250_000), cancellationToken);
    }

    public IAsyncEnumerable<HistorianSample> StreamRawAsync(
        IReadOnlyList<Guid> tagIds,
        DateTimeOffset from,
        DateTimeOffset to,
        CancellationToken cancellationToken = default)
    {
        if (to <= from)
            throw new ArgumentException("End time must be after start time.");
        return repository.StreamRawAsync(tagIds, from, to, cancellationToken);
    }


    public Task<IReadOnlyList<HistorianSample>> QueryByBatchAsync(
        Guid batchId,
        IReadOnlyList<Guid> tagIds,
        int maxRows = 250_000,
        CancellationToken cancellationToken = default) =>
        repository.QueryByBatchAsync(
            batchId,
            tagIds,
            Math.Clamp(maxRows, 1, 250_000),
            cancellationToken);

    private static void Validate(int interval, int retention)
    {
        if (!AllowedIntervals.Contains(interval))
            throw new ArgumentException("Sample interval must be 10, 30, 60 or 300 seconds.");

        if (!AllowedRetention.Contains(retention))
            throw new ArgumentException("Retention must be 30, 90, 365 or 1825 days.");
    }
}
