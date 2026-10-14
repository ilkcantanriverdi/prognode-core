using Prognode.Contracts.Notifications;

namespace Prognode.Notifications;

public sealed class AgentPresenceService
{
    private readonly object _gate = new();
    private DateTimeOffset? _lastSeenUtc;
    private string? _machineName;
    private string? _version;
    private string? _notificationMode;
    private long _openAlarmsRequest;

    /// <summary>
    /// A Windows notification was clicked while PROGNODE is already open in a browser:
    /// the open page sees the new number in /api/agent/status and shows Alarms.
    /// </summary>
    public long RequestOpenAlarms()
    {
        lock (_gate) return ++_openAlarmsRequest;
    }

    public void Report(
        string? machineName,
        string? version,
        string? notificationMode)
    {
        lock (_gate)
        {
            _lastSeenUtc = DateTimeOffset.UtcNow;
            _machineName = string.IsNullOrWhiteSpace(machineName)
                ? null
                : machineName.Trim();
            _version = string.IsNullOrWhiteSpace(version)
                ? null
                : version.Trim();
            _notificationMode = string.IsNullOrWhiteSpace(notificationMode)
                ? null
                : notificationMode.Trim();
        }
    }

    public AgentStatusSnapshot GetStatus()
    {
        lock (_gate)
        {
            var online = _lastSeenUtc is not null &&
                DateTimeOffset.UtcNow - _lastSeenUtc.Value < TimeSpan.FromSeconds(5);

            return new AgentStatusSnapshot(
                IsOnline: online,
                LastSeenUtc: _lastSeenUtc,
                MachineName: _machineName,
                Version: _version,
                NotificationMode: _notificationMode,
                OpenAlarmsRequest: _openAlarmsRequest);
        }
    }
}
