using Prognode.Contracts.Tags;
using Prognode.Core.Devices;
using Prognode.Core.Tags;
using Prognode.Protocols.Abstractions;
using Prognode.Protocols.Mqtt;
using Prognode.Protocols.OpcUa;

namespace Prognode.Host.Services;

public sealed class TagPollingHostedService(
    IDeviceRepository devices,
    ITagRepository tags,
    CurrentTagValueStore currentValues,
    IEnumerable<ITagReader> readers,
    MqttTagReader mqttReader,
    OpcUaTagReader opcUaReader,
    ILogger<TagPollingHostedService> logger) : BackgroundService
{
    private readonly Dictionary<Guid, DateTimeOffset> _nextDue = [];
    private readonly Dictionary<Guid, Task> _opcInFlight = [];

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollDueDevicesAsync(stoppingToken);
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
                    "Unhandled tag polling loop error.");
            }

            try
            {
                await Task.Delay(100, stoppingToken);
            }
            catch (OperationCanceledException)
                when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
        }
    }

    private async Task PollDueDevicesAsync(
        CancellationToken cancellationToken)
    {
        var allDevices =
            await devices.GetAllAsync(cancellationToken);
        mqttReader.PruneExcept(allDevices.Select(device => device.Id).ToHashSet());
        opcUaReader.PruneExcept(allDevices.Select(device => device.Id).ToHashSet());
        foreach (var id in _opcInFlight.Keys.Except(allDevices.Select(device => device.Id)).ToArray())
            _opcInFlight.Remove(id);

        var now = DateTimeOffset.UtcNow;

        foreach (var device in allDevices)
        {
            if (_nextDue.TryGetValue(
                device.Id,
                out var next) &&
                next > now)
            {
                continue;
            }

            _nextDue[device.Id] =
                now.AddMilliseconds(
                    Math.Max(100, device.PollIntervalMs));

            var deviceTags =
                await tags.GetByDeviceIdAsync(
                    device.Id,
                    cancellationToken);

            if (deviceTags.Count == 0)
            {
                mqttReader.Remove(device.Id);
                opcUaReader.Remove(device.Id);
                continue;
            }

            var reader =
                readers.FirstOrDefault(
                    x => x.CanHandle(device));

            if (reader is null)
            {
                SetBad(
                    deviceTags,
                    device.Id,
                    "No protocol reader is available.");
                continue;
            }

            // A first secure OPC UA handshake can take a minute on an S7-1200.
            // Keep other protocol devices polling while that handshake is pending.
            if (ReferenceEquals(reader, opcUaReader))
            {
                if (!_opcInFlight.TryGetValue(device.Id, out var pending) || pending.IsCompleted)
                    _opcInFlight[device.Id] = ReadDeviceAsync(reader, device, deviceTags, cancellationToken);
            }
            else
                await ReadDeviceAsync(reader, device, deviceTags, cancellationToken);
        }
    }

    private async Task ReadDeviceAsync(ITagReader reader,
        Prognode.Contracts.Devices.DeviceDefinition device,
        IReadOnlyList<TagDefinition> deviceTags, CancellationToken cancellationToken)
    {
        try
        {
            var values = await reader.ReadAsync(device, deviceTags, cancellationToken);
            currentValues.SetMany(values);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Tag poll failed for device {DeviceName}.", device.Name);
            SetBad(deviceTags, device.Id, ex.Message);
        }
    }

    private void SetBad(
        IReadOnlyList<TagDefinition> definitions,
        Guid deviceId,
        string error)
    {
        var now = DateTimeOffset.UtcNow;

        currentValues.SetMany(
            definitions.Select(
                tag =>
                    new TagValueSnapshot(
                        TagId: tag.Id,
                        DeviceId: deviceId,
                        RawValue: null,
                        Value: null,
                        Quality: TagQuality.Bad,
                        Timestamp: now,
                        Source: "Runtime",
                        Error: error
                    )));
    }
}
