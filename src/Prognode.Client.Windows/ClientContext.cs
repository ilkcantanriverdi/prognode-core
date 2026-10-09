using Prognode.Contracts.Notifications;

namespace Prognode.Client.Windows;

/// <summary>
/// Tray application: polls PROGNODE Core every few seconds on the plant network, shows a Windows
/// notification for each new alarm (with ACK when Core allows this PC) and keeps the alarm window current.
/// </summary>
public sealed class ClientContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 4000 };
    private readonly SynchronizationContext _ui;
    private readonly Toasts _toasts;
    private MainForm? _window;
    private PairingRecord? _pairing;
    private CoreConnection? _core;
    private DeviceAccess? _access;
    private bool _busy;
    private int _tick;
    private bool _online = true;

    public ClientContext(bool background)
    {
        _ui = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _toasts = new Toasts(_ui, OpenWindow, async id => await AckAsync(id));
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "prognode.ico");
        _tray = new NotifyIcon
        {
            Icon = File.Exists(iconPath) ? new Icon(iconPath) : SystemIcons.Application,
            Text = "PROGNODE",
            Visible = true,
            ContextMenuStrip = new ContextMenuStrip(),
        };
        _tray.ContextMenuStrip.Items.Add("Open PROGNODE", null, (_, _) => OpenWindow(null));
        _tray.ContextMenuStrip.Items.Add(new ToolStripSeparator());
        _tray.ContextMenuStrip.Items.Add("Exit", null, (_, _) => ExitThread());
        _tray.DoubleClick += (_, _) => OpenWindow(null);
        _timer.Tick += async (_, _) => await PollAsync();

        _pairing = ClientStore.Load();
        if (_pairing is null && !Pair()) { ExitThreadCore(); return; }
        Connect();
        if (!background) OpenWindow(null);
        _ = PollAsync(initial: true);
        _timer.Start();
    }

    private bool Pair()
    {
        using var form = new ConnectForm();
        if (form.ShowDialog() != DialogResult.OK || form.Result is null) return false;
        _pairing = form.Result;
        ClientStore.Save(_pairing);
        if (!ClientStore.StartsWithWindows) ClientStore.StartsWithWindows = true;
        return true;
    }

    private void Connect()
    {
        _core?.Dispose();
        _core = new CoreConnection(_pairing!.Host, _pairing.Port, _pairing.CertificateSha256, _pairing.Token);
        _tray.Text = $"PROGNODE · {Truncate(_pairing.DisplayName, 50)}";
    }

    private void OpenWindow(Guid? occurrence)
    {
        if (_pairing is null) return;
        if (_window is null || _window.IsDisposed)
        {
            _window = new MainForm();
            _window.AckRequested += AckAsync;
            _window.DisconnectRequested += Disconnect;
            _window.ShowServer(_pairing.DisplayName, _pairing.Host);
            _window.ShowAccess(_access);
        }
        _window.Show();
        if (_window.WindowState == FormWindowState.Minimized) _window.WindowState = FormWindowState.Normal;
        _window.Activate();
        _ = PollAsync();
        if (occurrence is { } id) _window.SelectOccurrence(id);
    }

    private async Task PollAsync(bool initial = false)
    {
        if (_busy || _core is null || _pairing is null) return;
        _busy = true;
        try
        {
            if (initial || _tick++ % 15 == 0)
            {
                _access = await _core.GetAccessAsync();
                _window?.ShowAccess(_access);
            }
            var alarms = await _core.GetActiveAlarmsAsync();
            _window?.ShowAlarms(alarms);

            var cursor = _pairing.Cursor;
            var fresh = new List<NotificationEvent>();
            for (var page = 0; page < 5; page++)
            {
                var result = await _core.GetNotificationsAsync(cursor);
                foreach (var item in result.Items.Where(x => x.Id > cursor)) fresh.Add(item);
                cursor = Math.Max(cursor, result.NextCursor);
                if (!result.HasMore || result.Items.Count == 0) break;
            }
            // First connection: start from now, never replay old alarms as notifications.
            var replay = _pairing.Cursor == 0;
            if (cursor != _pairing.Cursor)
            {
                _pairing = _pairing with { Cursor = cursor };
                ClientStore.Save(_pairing);
            }
            if (!replay) Notify(fresh);
            SetOnline(true, $"Connected · {_pairing.Host}");
        }
        catch (UnauthorizedAccessException)
        {
            _timer.Stop();
            ClientStore.Clear();
            _tray.ShowBalloonTip(8000, "PROGNODE", "This PC was removed from PROGNODE Core. Pair it again to receive alarms.", ToolTipIcon.Warning);
            _pairing = null;
            _window?.Close();
            if (Pair()) { Connect(); _timer.Start(); OpenWindow(null); } else ExitThread();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or System.Text.Json.JsonException)
        {
            SetOnline(false, "PROGNODE Core is not reachable on the plant network. Retrying…");
        }
        finally { _busy = false; }
    }

    private void Notify(IEnumerable<NotificationEvent> events)
    {
        var shown = 0;
        foreach (var e in events.OrderBy(x => x.Id))
        {
            if (e.EventType is "ACK" or "CLEAR" or "CLEARED")
            {
                if (e.OccurrenceId is { } done) _toasts.RemoveFor(done);
                continue;
            }
            if (e.EventType is not ("ACTIVE" or "REMINDER") || shown >= 5) continue;
            var critical = string.Equals(e.Severity, "Critical", StringComparison.OrdinalIgnoreCase);
            // Core puts "<device> • <alarm text>" in Message and the priority/ACK state in Title.
            var title = string.IsNullOrWhiteSpace(e.Message) ? e.Title : e.Message;
            var body = e.Title;
            if (_toasts.Available)
                _toasts.Show(title, body, e.OccurrenceId, critical, _access?.DeviceAck == true && e.OccurrenceId is not null);
            else
                _tray.ShowBalloonTip(8000, Truncate(title, 63), body, critical ? ToolTipIcon.Error : ToolTipIcon.Warning);
            shown++;
        }
    }

    private async Task AckAsync(Guid occurrenceId)
    {
        if (_core is null) return;
        try
        {
            await _core.AcknowledgeAsync(occurrenceId);
            _toasts.RemoveFor(occurrenceId);
            _tray.ShowBalloonTip(3000, "PROGNODE", "Core confirmed the acknowledgement.", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            if (_window is { Visible: true }) MessageBox.Show(_window, ex.Message, "ACK not applied", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            else _tray.ShowBalloonTip(6000, "ACK not applied", ex.Message, ToolTipIcon.Warning);
        }
        await PollAsync();
    }

    private void SetOnline(bool online, string message)
    {
        if (online != _online)
        {
            _online = online;
            if (!online) _tray.ShowBalloonTip(5000, "PROGNODE", "Connection to PROGNODE Core lost. Alarms resume when it is back.", ToolTipIcon.Warning);
        }
        _window?.ShowConnection(online, message);
    }

    private void Disconnect()
    {
        _timer.Stop();
        ClientStore.Clear();
        ClientStore.StartsWithWindows = false;
        _window?.Close();
        _window?.Dispose();
        _window = null;
        _pairing = null;
        if (Pair()) { Connect(); _timer.Start(); OpenWindow(null); } else ExitThread();
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    protected override void ExitThreadCore()
    {
        _timer.Stop();
        _tray.Visible = false;
        _tray.Dispose();
        _toasts.Dispose();
        _core?.Dispose();
        base.ExitThreadCore();
    }
}
