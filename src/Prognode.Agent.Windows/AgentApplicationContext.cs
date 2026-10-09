using System.Diagnostics;
using System.Drawing;
using System.Net.Http.Json;
using System.Windows.Forms;
using Prognode.Contracts.Alarms;
using Prognode.Contracts.Notifications;

namespace Prognode.Agent.Windows;

public sealed class AgentApplicationContext : ApplicationContext
{
    private readonly HttpClient _http;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _statusItem;
    private readonly System.Windows.Forms.Timer _timer;
    private readonly WindowsNotificationService _windowsNotifications;
    private readonly string _coreUrl;

    private bool _polling;
    private long _lastNotificationId;
    private sealed record NotificationDeliveryStatus(bool Enabled);

    public AgentApplicationContext()
    {
        _coreUrl =
            Environment.GetEnvironmentVariable(
                "PROGNODE_CORE_URL")
            ?? "http://127.0.0.1:5080";

        _http =
            new HttpClient
            {
                BaseAddress = new Uri(_coreUrl),
                Timeout = TimeSpan.FromSeconds(3)
            };

        var menu = new ContextMenuStrip();

        var openItem = new ToolStripMenuItem("Open PROGNODE");
        openItem.Click += (_, _) => OpenDashboard();

        _statusItem =
            new ToolStripMenuItem("Core: Connecting...")
            {
                Enabled = false
            };

        var testItem = new ToolStripMenuItem("Show Test Notification");
        testItem.Click += (_, _) =>
            ShowNotification(
                "Information",
                "Agent Test",
                "PROGNODE notification delivery is working.");

        var exitItem = new ToolStripMenuItem("Exit PROGNODE Agent");
        exitItem.Click += (_, _) => ExitAgent();

        menu.Items.Add(openItem);
        menu.Items.Add(_statusItem);
        menu.Items.Add(new ToolStripSeparator());
        var lanSetupItem = new ToolStripMenuItem("Complete LAN Access Setup (UAC)");
        lanSetupItem.Click += async (_, _) => await LanAccessSetup.RunAsync(_http);
        menu.Items.Add(lanSetupItem);
        menu.Items.Add(testItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);

        var ownIcon =
            Icon.ExtractAssociatedIcon(
                Application.ExecutablePath)
            ?? SystemIcons.Application;

        _notifyIcon =
            new NotifyIcon
            {
                Text = "PROGNODE",
                Icon = ownIcon,
                Visible = true,
                ContextMenuStrip = menu
            };

        _notifyIcon.DoubleClick += (_, _) => OpenDashboard();
        _notifyIcon.BalloonTipClicked += (_, _) => OpenAlarms();

        _windowsNotifications =
            new WindowsNotificationService(OpenAlarms);

        _timer =
            new System.Windows.Forms.Timer
            {
                Interval = 1000
            };

        _timer.Tick += async (_, _) => await PollAsync();
        _timer.Start();

        ShowNotification(
            "Information",
            "Agent Started",
            "PROGNODE Agent is ready.");
    }

    private async Task PollAsync()
    {
        if (_polling)
            return;

        _polling = true;

        try
        {
            using var health = await _http.GetAsync("/api/health");
            if (!health.IsSuccessStatusCode)
                throw new HttpRequestException();

            _statusItem.Text =
                _windowsNotifications.IsAvailable
                    ? "Core: Connected • Action Center Ready"
                    : "Core: Connected • Tray Fallback";

            using var heartbeat =
                await _http.PostAsJsonAsync(
                    "/api/agent/heartbeat",
                    new
                    {
                        machineName = Environment.MachineName,
                        version = Prognode.Contracts.ProductVersion.Current,
                        notificationMode = _windowsNotifications.IsAvailable
                            ? "Windows Action Center"
                            : "Tray Fallback"
                    });

            var events =
                await _http.GetFromJsonAsync<NotificationEvent[]>(
                    $"/api/notifications?after={_lastNotificationId}")
                ?? [];

            var delivery=await _http.GetFromJsonAsync<NotificationDeliveryStatus>(
                "/api/notifications/delivery");

            if (events.Length > 0)
            {
                if(delivery?.Enabled==true)await DeliverBatchAsync(events);
                _lastNotificationId =
                    Math.Max(
                        _lastNotificationId,
                        events.Max(x => x.Id));
            }
        }
        catch
        {
            _statusItem.Text = "Core: Disconnected";
        }
        finally
        {
            _polling = false;
        }
    }

    private async Task DeliverBatchAsync(
        IReadOnlyList<NotificationEvent> events)
    {
        var ordered =
            events
                .OrderByDescending(x => SeverityRank(x.Severity))
                .ThenBy(x => x.Id)
                .ToArray();

        if (ordered.Length <= 3)
        {
            foreach (var item in ordered)
            {
                ShowNotification(
                    item.Severity,
                    item.Title,
                    item.Message);
            }

            return;
        }

        // Burst policy: show the most important event, then one compact aggregate.
        var first = ordered[0];
        ShowNotification(
            first.Severity,
            first.Title,
            first.Message);

        var activeCount = 0;
        try
        {
            var active =
                await _http.GetFromJsonAsync<AlarmRuntimeSnapshot[]>(
                    "/api/alarms/active");

            activeCount = active?.Length ?? 0;
        }
        catch
        {
            // Aggregation still works if the active-alarm query is temporarily unavailable.
        }

        var aggregate =
            activeCount > 0
                ? $"{activeCount} active alarms waiting"
                : $"{ordered.Length - 1} additional events waiting";

        ShowNotification(
            "Information",
            "Alarm Summary",
            aggregate);
    }

    private void ShowNotification(
        string severity,
        string title,
        string message)
    {
        // Windows shows the registered PROGNODE app name + brand icon itself.
        // The toast content is intentionally reduced to the operator-facing message.
        if (_windowsNotifications.Show(message))
            return;

        // Safe fallback for machines where the Windows App SDK notification runtime
        // is unavailable. No generic warning icon is used.
        _notifyIcon.BalloonTipTitle = "PROGNODE";
        _notifyIcon.BalloonTipText = message;
        _notifyIcon.BalloonTipIcon = ToolTipIcon.None;
        _notifyIcon.ShowBalloonTip(5000);
    }

    private static int SeverityRank(string? severity) =>
        severity?.Trim().ToLowerInvariant() switch
        {
            "critical" => 4,
            "high" => 3,
            "medium" => 2,
            "low" => 1,
            _ => 0
        };

    private void OpenDashboard()
    {
        Process.Start(
            new ProcessStartInfo
            {
                FileName = _coreUrl,
                UseShellExecute = true
            });
    }

    private void OpenAlarms()
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = _coreUrl.TrimEnd('/') + "/#alarms",
            UseShellExecute = true
        });
    }

    private void ExitAgent()
    {
        _timer.Stop();
        _notifyIcon.Visible = false;
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _timer.Dispose();
            _windowsNotifications.Dispose();
            _notifyIcon.Dispose();
            _http.Dispose();
        }

        base.Dispose(disposing);
    }
}
