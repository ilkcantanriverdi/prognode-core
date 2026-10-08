using Prognode.Contracts.Tags;
using Prognode.Core.Devices;
using Prognode.Core.Tags;
using Prognode.Licensing;
using Prognode.Protocols.Abstractions;
using Prognode.Protocols.Mqtt;
using Prognode.Protocols.OpcUa;
using Prognode.Protocols.S7;

namespace Prognode.Host.Services;

/// <summary>
/// Polls every device on its own schedule. Each device read runs independently (review Y2): a
/// slow or unreachable device no longer delays the others, and a device whose previous read is
/// still running is skipped instead of queued. When a device produces no fresh values for
/// 3 × its poll interval (at least 10 s), its last Good values are marked STALE so alarms,
/// the signal-quality monitor and the historian never treat old data as current.
/// Without an operational license (missing, expired after grace, not activated on this PC, or
/// revoked) nothing is polled: PLC connections are closed and live values are marked NotConnected.
/// Configuration and recorded history are kept; polling resumes when a valid license is active.
/// </summary>
public sealed class TagPollingHostedService(
    IDeviceRepository devices,
    ITagRepository tags,
    CurrentTagValueStore currentValues,
    IEnumerable<ITagReader> readers,
    MqttTagReader mqttReader,
    OpcUaTagReader opcUaReader,
    S7SessionPool s7Sessions,
    LicenseService license,
    ILogger<TagPollingHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan LicenseCheckInterval = TimeSpan.FromSeconds(2);
    private DateTimeOffset _licenseCheckedAt = DateTimeOffset.MinValue;
    private bool _licensed = true;
    private bool _suspended;
    private static readonly TimeSpan DeviceListRefresh = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan MinimumStaleAfter = TimeSpan.FromSeconds(10);

    private readonly Dictionary<Guid, DateTimeOffset> _nextDue = [];
    private readonly Dictionary<Guid, Task> _inFlight = [];
    private readonly Dictionary<Guid, DateTimeOffset> _lastCompleted = [];
    private readonly object _completedGate = new();
    private IReadOnlyList<Prognode.Contracts.Devices.DeviceDefinition> _devices = [];
    private DateTimeOffset _devicesLoadedAt = DateTimeOffset.MinValue;

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

        try { await Task.WhenAll(_inFlight.Values); } catch { /* shutting down */ }
    }

    private async Task PollDueDevicesAsync(
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        if (!await LicenseAllowsPollingAsync(now))
            return;

        if (now - _devicesLoadedAt >= DeviceListRefresh)
        {
            _devices = await devices.GetAllAsync(cancellationToken);
            _devicesLoadedAt = now;
            var ids = _devices.Select(device => device.Id).ToHashSet();
            mqttReader.PruneExcept(ids);
            opcUaReader.PruneExcept(ids);
            s7Sessions.PruneExcept(ids);
            foreach (var id in _inFlight.Keys.Where(id => !ids.Contains(id)).ToArray())
                _inFlight.Remove(id);
            foreach (var id in _nextDue.Keys.Where(id => !ids.Contains(id)).ToArray())
                _nextDue.Remove(id);
            lock (_completedGate)
                foreach (var id in _lastCompleted.Keys.Where(id => !ids.Contains(id)).ToArray())
                    _lastCompleted.Remove(id);
        }

        foreach (var device in _devices)
        {
            if (_inFlight.TryGetValue(device.Id, out var running) && !running.IsCompleted)
            {
                MarkStaleIfOverdue(device, now);
                continue;
            }

            if (_nextDue.TryGetValue(device.Id, out var next) && next > now)
                continue;

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
                SetQuality(
                    deviceTags,
                    device.Id,
                    TagQuality.Bad,
                    "No protocol reader is available.");
                continue;
            }

            _inFlight[device.Id] = ReadDeviceAsync(reader, device, deviceTags, cancellationToken);
        }
    }

    private async Task<bool> LicenseAllowsPollingAsync(DateTimeOffset now)
    {
        if (now - _licenseCheckedAt >= LicenseCheckInterval)
        {
            _licenseCheckedAt = now;
            _licensed = license.Current.IsValid;
        }
        if (_licensed)
        {
            if (_suspended)
                logger.LogInformation("PROGNODE license is operational again; tag polling resumed.");
            _suspended = false;
            return true;
        }
        if (_suspended)
            return false;

        _suspended = true;
        // Let reads that already started finish first, so they cannot overwrite the marking below.
        try { await Task.WhenAll(_inFlight.Values); } catch { /* individual read errors are already handled */ }
        _inFlight.Clear();
        _nextDue.Clear();
        _devicesLoadedAt = DateTimeOffset.MinValue;
        var none = new HashSet<Guid>();
        mqttReader.PruneExcept(none);
        opcUaReader.PruneExcept(none);
        s7Sessions.PruneExcept(none);
        foreach (var value in currentValues.GetAll())
            currentValues.Set(value with { Quality = TagQuality.NotConnected, Error = "PROGNODE license is not active." });
        logger.LogWarning("PROGNODE license is not operational ({Status}); tag polling stopped. Configuration and history are kept.", license.Current.Status);
        return false;
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
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { return; }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Tag poll failed for device {DeviceName}.", device.Name);
            SetQuality(deviceTags, device.Id, TagQuality.Bad, ex.Message);
        }

        lock (_completedGate)
            _lastCompleted[device.Id] = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// A read that is still running after 3 × poll interval (≥ 10 s) means no fresh data: mark the
    /// device's Good values STALE (once) rather than letting them look current.
    /// </summary>
    private void MarkStaleIfOverdue(Prognode.Contracts.Devices.DeviceDefinition device, DateTimeOffset now)
    {
        var staleAfter = TimeSpan.FromMilliseconds(Math.Max(100, device.PollIntervalMs) * 3);
        if (staleAfter < MinimumStaleAfter) staleAfter = MinimumStaleAfter;

        DateTimeOffset last;
        lock (_completedGate)
            if (!_lastCompleted.TryGetValue(device.Id, out last)) return;
        if (now - last < staleAfter) return;

        var stale = currentValues.GetAll()
            .Where(value => value.DeviceId == device.Id && value.Quality == TagQuality.Good)
            .Select(value => value with
            {
                Quality = TagQuality.Stale,
                Error = $"No fresh value for {(int)(now - last).TotalSeconds} s; the device read has not completed."
            })
            .ToArray();
        if (stale.Length > 0) currentValues.SetMany(stale);
    }

    private void SetQuality(
        IReadOnlyList<TagDefinition> definitions,
        Guid deviceId,
        TagQuality quality,
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
                        Quality: quality,
                        Timestamp: now,
                        Source: "Runtime",
                        Error: error
                    )));
    }
}
