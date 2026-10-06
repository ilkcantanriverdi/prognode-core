using Prognode.Contracts.Historian;
using Prognode.Contracts.Tags;
using Prognode.Core.Tags;
using Prognode.Historian;
using Prognode.Licensing;
using Prognode.Core.Batches;

namespace Prognode.Host.Services;

public sealed class HistorianSamplingHostedService(
    IHistorianConfigurationRepository configurations,
    CurrentTagValueStore values,
    IHistorianRepository historian,
    LicenseService license,
    BatchService batches,
    ILogger<HistorianSamplingHostedService> logger) : BackgroundService
{
    private IReadOnlyList<HistorianRecordingConfiguration> _configs = [];
    private readonly Dictionary<Guid, DateTimeOffset> _nextDue = [];
    private DateTimeOffset _nextRefresh = DateTimeOffset.MinValue;
    private DateTimeOffset _nextCleanup = DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!license.HasModule("HISTORIAN"))
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                    continue;
                }

                var now = DateTimeOffset.UtcNow;
                if (now >= _nextRefresh)
                {
                    _configs = await configurations.GetAllAsync(stoppingToken);
                    var validIds = _configs.Select(x => x.Id).ToHashSet();
                    foreach (var stale in _nextDue.Keys.Where(x => !validIds.Contains(x)).ToArray())
                        _nextDue.Remove(stale);
                    _nextRefresh = now.AddSeconds(2);
                }

                var sampleBatch = new List<HistorianSample>();
                Guid? currentBatchId = null;
                var batchResolved = false;

                foreach (var config in _configs.Where(x => x.Enabled))
                {
                    if (_nextDue.TryGetValue(config.Id, out var due) && due > now)
                        continue;

                    _nextDue[config.Id] = now.AddSeconds(config.SampleIntervalSeconds);
                    var snapshot = values.Get(config.TagId);

                    if (snapshot is not { Quality: TagQuality.Good, Value: not null })
                        continue;

                    if (!batchResolved)
                    {
                        currentBatchId = (await batches.GetCurrentAsync(stoppingToken))?.Id;
                        batchResolved = true;
                    }

                    sampleBatch.Add(HistorianSamplePolicy.Create(snapshot, now, currentBatchId)!);
                }

                if (sampleBatch.Count > 0)
                    await historian.WriteBatchAsync(sampleBatch, stoppingToken);

                if (now >= _nextCleanup)
                {
                    foreach (var config in _configs.Where(x => x.Enabled))
                    {
                        await historian.DeleteBeforeAsync(
                            config.TagId,
                            now.AddDays(-config.RetentionDays),
                            stoppingToken);
                    }
                    _nextCleanup = now.AddHours(1);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Historian sampling cycle failed.");
            }

            try { await Task.Delay(500, stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
