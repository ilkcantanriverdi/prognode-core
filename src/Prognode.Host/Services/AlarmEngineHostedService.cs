using Prognode.Alarm;
using Prognode.Contracts.Alarms;
using Prognode.Contracts.Tags;
using Prognode.Core.Devices;
using Prognode.Core.Tags;
using Prognode.Licensing;

namespace Prognode.Host.Services;

public sealed class AlarmEngineHostedService(
    IAlarmDefinitionRepository repository,
    ITagRepository tags,
    IDeviceRepository devices,
    AlarmEngine engine,
    LicenseService license,
    ILogger<AlarmEngineHostedService> logger) : BackgroundService
{
    private IReadOnlyList<AlarmDefinition> _definitions = [];

    private IReadOnlyDictionary<Guid, TagDefinition> _tagsById =
        new Dictionary<Guid, TagDefinition>();

    private IReadOnlyDictionary<Guid, string> _deviceNamesById =
        new Dictionary<Guid, string>();

    private DateTimeOffset _nextRefresh =
        DateTimeOffset.MinValue;

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (!license.HasModule("ALARM"))
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
                    continue;
                }

                var now = DateTimeOffset.UtcNow;

                if (now >= _nextRefresh)
                {
                    _definitions =
                        await repository.GetAllAsync(
                            stoppingToken);

                    var allTags =
                        await tags.GetAllAsync(
                            stoppingToken);

                    var allDevices =
                        await devices.GetAllAsync(
                            stoppingToken);

                    _tagsById =
                        allTags.ToDictionary(
                            x => x.Id);

                    _deviceNamesById =
                        allDevices.ToDictionary(
                            x => x.Id,
                            x => x.Name);

                    _nextRefresh =
                        now.AddSeconds(1);
                }

                await engine.EvaluateAsync(
                    _definitions,
                    _tagsById,
                    _deviceNamesById,
                    stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Alarm engine cycle failed.");
            }

            try
            {
                await Task.Delay(
                    250,
                    stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }
}
