using System.Text.Json;
using Prognode.Contracts;
using Prognode.Contracts.Alarms;
using Prognode.Contracts.Notifications;

namespace Prognode.Client.Windows;

/// <summary>
/// Tray application: polls PROGNODE Core every few seconds on the plant network, shows a Windows
/// notification for each new alarm (with ACK when Core allows this PC) and feeds the client window.
/// Notifications do not depend on the window: they keep coming while it is closed.
/// </summary>
public sealed class ClientContext : ApplicationContext
{
    private readonly NotifyIcon _tray;
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 4000 };
    private readonly System.Windows.Forms.Timer _updateTimer = new() { Interval = 12 * 60 * 60 * 1000 };
    private const string ReleasesUrl = "https://account.prognode.io/api/releases/latest";
    private const string DownloadsUrl = "https://account.prognode.io/downloads";
    private const string CordaraUrl = "https://www.cordaralabs.com";
    private ToolStripMenuItem? _updateItem;
    private string? _announcedVersion;
    private (string Version, Uri Url, string Sha256)? _update;
    private bool _updating;
    private readonly SynchronizationContext _ui;
    private readonly Toasts _toasts;
    private ClientWindow? _window;
    private PairingRecord? _pairing;
    private CoreConnection? _core;
    private DeviceAccess? _access;
    private IReadOnlyList<AlarmRuntimeSnapshot> _alarms = [];
    private CoreCandidate? _candidate;
    private Guid? _select;
    private bool _busy;
    private int _tick;
    private bool _online = true;
    private string _connection = "Connecting…";
    private DateTimeOffset? _lastContact;

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
        _updateItem = new ToolStripMenuItem("Update available", null, async (_, _) => await InstallUpdateAsync()) { Visible = false };
        _tray.ContextMenuStrip.Items.Add(_updateItem);
        _tray.BalloonTipClicked += async (_, _) => { if (_announcedVersion is not null && !_updating) await InstallUpdateAsync(); };
        _tray.ContextMenuStrip.Items.Add(new ToolStripSeparator());
        _tray.ContextMenuStrip.Items.Add("Exit", null, (_, _) => ExitThread());
        // A single click opens the window, like the PROGNODE icon on the Core PC.
        _tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) OpenWindow(null); };
        _timer.Tick += async (_, _) => await PollAsync();

        _pairing = ClientStore.Load();
        if (_pairing is null) OpenWindow(null);
        else
        {
            Connect();
            if (!background) OpenWindow(null);
            _ = PollAsync(initial: true);
            _timer.Start();
        }
        _updateTimer.Tick += async (_, _) => await CheckForUpdateAsync();
        _updateTimer.Start();
        _ = CheckForUpdateAsync();
    }

    /// <summary>Another start of PROGNODE Client (Start menu, desktop) asks this one to show its window.</summary>
    public void RequestShow() => _ui.Post(_ => OpenWindow(null), null);

    private void Connect()
    {
        _core?.Dispose();
        _core = new CoreConnection(_pairing!.Host, _pairing.Port, _pairing.CertificateSha256, _pairing.Token);
        _tray.Text = $"PROGNODE · {Truncate(_pairing.DisplayName, 50)}";
    }

    private void OpenWindow(Guid? occurrence)
    {
        if (_window is null || _window.IsDisposed)
        {
            _window = new ClientWindow();
            _window.Command += OnCommand;
            // Without a pairing there is nothing to watch in the tray.
            _window.Hidden += () => { if (_pairing is null) ExitThread(); };
        }
        _window.ShowAndActivate();
        if (occurrence is { } id) { _select = id; _window.Post(new { type = "select", occurrenceId = id }); }
        if (_pairing is not null) _ = PollAsync();
    }

    private void PushState() => _window?.Post(new
    {
        type = "state",
        paired = _pairing is not null,
        version = ProductVersion.Current,
        pcName = _pairing?.ClientName ?? Environment.MachineName,
        autostart = ClientStore.StartsWithWindows,
        server = _pairing is null ? null : new { name = _pairing.DisplayName, host = _pairing.Host, port = _pairing.Port },
        connection = new { online = _online, message = _connection, lastContactUtc = _lastContact },
        access = _access is null ? null : new { _access.CanViewAlarms, _access.CanAcknowledge, deviceAck = _access.DeviceAck },
        alarms = _alarms,
    });

    private void Toast(string text, string? kind = null, string? reenable = null) =>
        _window?.Post(new { type = "toast", text, kind, reenable });

    private void OnCommand(JsonElement message)
    {
        string? Text(string name) => message.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
        switch (Text("cmd"))
        {
            case "ready":
                PushState();
                if (_select is { } id) _window?.Post(new { type = "select", occurrenceId = id });
                if (_pairing is null) _ = DiscoverAsync();
                break;
            case "refresh": _ = PollAsync(initial: true); break;
            case "ack": if (Guid.TryParse(Text("id"), out var ackId)) _ = AckAsync(ackId); break;
            case "history":
                var offset = message.TryGetProperty("offset", out var o) && o.TryGetInt32(out var n) ? Math.Max(0, n) : 0;
                _ = HistoryAsync(offset);
                break;
            case "check": _ = CheckAsync(); break;
            case "reconnect": _ = FindCoreAgainAsync(); break;
            case "testNotification": _ = TestNotificationAsync(); break;
            case "autostart":
                ClientStore.StartsWithWindows = message.TryGetProperty("value", out var on) && on.ValueKind == JsonValueKind.True;
                PushState();
                break;
            case "disconnect": Disconnect(); break;
            case "discover": _ = DiscoverAsync(); break;
            case "inspect":
                var port = message.TryGetProperty("port", out var p) && p.TryGetInt32(out var pn) ? pn : 5443;
                _ = InspectAsync(Text("host") ?? "", port, Guid.TryParse(Text("serverId"), out var sid) ? sid : null);
                break;
            // The page cannot open links itself; only this fixed address is opened in the browser.
            case "openCordara": System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(CordaraUrl) { UseShellExecute = true }); break;
            case "pair": _ = PairAsync(Text("code") ?? "", Text("name") ?? ""); break;
        }
    }

    // ---------- pairing ----------

    private async Task DiscoverAsync()
    {
        try
        {
            var found = await CoreConnection.DiscoverAsync();
            _window?.Post(new
            {
                type = "discovered",
                items = found.Select(x => new { serverId = x.ServerId, displayName = x.DisplayName, address = x.Address.ToString(), securePort = x.SecurePort }),
            });
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or IOException)
        {
            _window?.Post(new { type = "discovered", items = Array.Empty<object>() });
        }
    }

    private async Task InspectAsync(string host, int port, Guid? expectedServerId)
    {
        _candidate = null;
        try
        {
            _candidate = await CoreConnection.InspectAsync(host.Trim(), port, expectedServerId);
            _window?.Post(new { type = "candidate", _candidate.DisplayName, _candidate.CheckCode });
        }
        catch (Exception ex)
        {
            _window?.Post(new { type = "candidate", error = $"Could not reach PROGNODE Core securely at {host}:{port}. {ex.Message}" });
        }
    }

    private async Task PairAsync(string code, string name)
    {
        if (_candidate is null) { _window?.Post(new { type = "paired", error = "Find your Core and compare the check code first." }); return; }
        if (code.Length != 6 || !code.All(char.IsDigit)) { _window?.Post(new { type = "paired", error = "Enter the 6-digit pairing code from the Core screen." }); return; }
        var pcName = string.IsNullOrWhiteSpace(name) ? Environment.MachineName : name.Trim();
        try
        {
            using var core = new CoreConnection(_candidate.Host, _candidate.Port, _candidate.CertificateSha256);
            var (clientId, token) = await core.PairAsync(_candidate.ServerId, pcName, code, ClientStore.DevicePublicKey());
            _pairing = new PairingRecord(_candidate.ServerId, _candidate.DisplayName, _candidate.Host, _candidate.Port,
                _candidate.CertificateSha256, clientId, token, ClientName: pcName);
            ClientStore.Save(_pairing);
            if (!ClientStore.StartsWithWindows) ClientStore.StartsWithWindows = true;
            _candidate = null;
            Connect();
            _window?.Post(new { type = "paired" });
            await PollAsync(initial: true);
            _timer.Start();
        }
        catch (Exception ex)
        {
            _window?.Post(new { type = "paired", error = ex.Message });
        }
    }

    // ---------- monitoring ----------

    private async Task PollAsync(bool initial = false)
    {
        if (_busy || _core is null || _pairing is null) return;
        _busy = true;
        try
        {
            if (initial || _access is null || _tick++ % 15 == 0) _access = await _core.GetAccessAsync();
            _alarms = await _core.GetActiveAlarmsAsync();

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
            _lastContact = DateTimeOffset.UtcNow;
            SetOnline(true, $"Connected · {_pairing.Host}");
        }
        catch (UnauthorizedAccessException)
        {
            _timer.Stop();
            ClientStore.Clear();
            _pairing = null;
            _access = null;
            _alarms = [];
            _tray.ShowBalloonTip(8000, "PROGNODE", "This PC was removed from PROGNODE Core. Pair it again to receive alarms.", ToolTipIcon.Warning);
            OpenWindow(null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException or JsonException)
        {
            SetOnline(false, "PROGNODE Core is not reachable on the plant network. Retrying…");
        }
        finally
        {
            _busy = false;
            PushState();
        }
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
            var needsAck = e.OccurrenceId is { } occ && _alarms.FirstOrDefault(a => a.OccurrenceId == occ)?.RequiresAcknowledgement != false;
            ShowNotification(title, body, e.OccurrenceId, critical, _access?.DeviceAck == true && e.OccurrenceId is not null && needsAck);
            shown++;
        }
    }

    private void ShowNotification(string title, string body, Guid? occurrenceId, bool critical, bool canAck)
    {
        if (_toasts.Available) _toasts.Show(title, body, occurrenceId, critical, canAck);
        else _tray.ShowBalloonTip(8000, Truncate(title, 63), body, critical ? ToolTipIcon.Error : ToolTipIcon.Warning);
    }

    private async Task AckAsync(Guid occurrenceId)
    {
        if (_core is null) return;
        try
        {
            await _core.AcknowledgeAsync(occurrenceId);
            _toasts.RemoveFor(occurrenceId);
            if (_window is { Visible: true }) Toast("Core confirmed the acknowledgement.", "ok");
            else _tray.ShowBalloonTip(3000, "PROGNODE", "Core confirmed the acknowledgement.", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            if (_window is { Visible: true }) Toast($"ACK not applied: {ex.Message}", "error");
            else _tray.ShowBalloonTip(6000, "ACK not applied", ex.Message, ToolTipIcon.Warning);
        }
        await PollAsync();
    }

    private async Task HistoryAsync(int offset)
    {
        if (_core is null) return;
        try
        {
            var (items, total) = await _core.GetHistoryAsync(offset, 50);
            _window?.Post(new { type = "history", items, total });
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or UnauthorizedAccessException)
        {
            var message = ex is HttpRequestException { StatusCode: System.Net.HttpStatusCode.NotFound }
                ? "This PROGNODE Core does not offer alarm history to PCs yet. Update PROGNODE Core."
                : "Alarm history could not be loaded. Check the connection and try again.";
            _window?.Post(new { type = "history", items = Array.Empty<object>(), total = 0, error = message });
        }
    }

    private void SetOnline(bool online, string message)
    {
        if (online != _online)
        {
            _online = online;
            if (!online) _tray.ShowBalloonTip(5000, "PROGNODE", "Connection to PROGNODE Core lost. Alarms resume when it is back.", ToolTipIcon.Warning);
        }
        _connection = message;
    }

    // ---------- connection tools ----------

    private async Task CheckAsync()
    {
        if (_core is null || _pairing is null) return;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            _access = await _core.GetAccessAsync();
            _lastContact = DateTimeOffset.UtcNow;
            SetOnline(true, $"Connected · {_pairing.Host}");
            Toast($"PROGNODE Core answered in {watch.ElapsedMilliseconds} ms. The connection is secure (certificate matches).", "ok", "checkBtn");
        }
        catch (UnauthorizedAccessException)
        {
            Toast("PROGNODE Core no longer accepts this PC. Pair it again.", "error", "checkBtn");
        }
        catch (Exception ex)
        {
            SetOnline(false, "PROGNODE Core is not reachable on the plant network. Retrying…");
            Toast($"No answer from {_pairing.Host}:{_pairing.Port}. {ex.Message} If Core moved to another IP address, use Find Core again.", "error", "checkBtn");
        }
        PushState();
    }

    /// <summary>Finds the paired Core on the network again (e.g. after its IP address changed); the certificate must still match.</summary>
    private async Task FindCoreAgainAsync()
    {
        if (_pairing is null) return;
        try
        {
            var match = (await CoreConnection.DiscoverAsync()).FirstOrDefault(x => x.ServerId == _pairing.ServerId);
            if (match is null)
            {
                Toast("Your PROGNODE Core did not answer on this network. Check that the Core PC is on and on the same network.", "error", "reconnectBtn");
                return;
            }
            var address = match.Address.ToString();
            if (address != _pairing.Host || match.SecurePort != _pairing.Port)
            {
                var candidate = await CoreConnection.InspectAsync(address, match.SecurePort, _pairing.ServerId);
                if (!string.Equals(candidate.CertificateSha256, _pairing.CertificateSha256, StringComparison.OrdinalIgnoreCase))
                {
                    Toast($"A Core with this identity answered at {address}, but its certificate changed. For safety, disconnect and pair this PC again.", "error", "reconnectBtn");
                    return;
                }
                _pairing = _pairing with { Host = address, Port = match.SecurePort };
                ClientStore.Save(_pairing);
                Connect();
            }
            await PollAsync(initial: true);
            Toast($"Connected to {_pairing.DisplayName} at {address}.", "ok", "reconnectBtn");
        }
        catch (Exception ex)
        {
            Toast($"Search failed: {ex.Message}", "error", "reconnectBtn");
        }
    }

    private async Task TestNotificationAsync()
    {
        Toast("A test notification appears in 5 seconds. You can close this window to check that it still arrives.", "ok");
        await Task.Delay(5000);
        ShowNotification("PROGNODE test notification", "Alarm notifications from PROGNODE Core reach this PC.", null, false, false);
    }

    private void Disconnect()
    {
        _timer.Stop();
        ClientStore.Clear();
        ClientStore.StartsWithWindows = false;
        _core?.Dispose();
        _core = null;
        _pairing = null;
        _access = null;
        _alarms = [];
        _tray.Text = "PROGNODE";
        PushState();
        _ = DiscoverAsync();
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

    // ---------- updates ----------

    /// <summary>Tells the user once per version that a newer PROGNODE Client is available; never installs by itself.</summary>
    private async Task CheckForUpdateAsync()
    {
        if (ProductVersion.IsDevelopmentBuild(ProductVersion.Current)) return;
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            using var document = JsonDocument.Parse(await http.GetStringAsync(ReleasesUrl));
            if (!document.RootElement.TryGetProperty("client", out var client) || client.ValueKind != JsonValueKind.Object ||
                !client.TryGetProperty("version", out var version) || version.GetString() is not { } latest) return;
            if (ProductVersion.Compare(latest, ProductVersion.Current) is not > 0 || latest == _announcedVersion) return;
            _announcedVersion = latest;
            _update = Uri.TryCreate(client.TryGetProperty("url", out var u) ? u.GetString() : null, UriKind.Absolute, out var url) &&
                url.Scheme == Uri.UriSchemeHttps && url.Host.EndsWith(".public.blob.vercel-storage.com", StringComparison.OrdinalIgnoreCase) &&
                client.TryGetProperty("sha256", out var s) && s.GetString() is { Length: 64 } sha
                ? (latest, url, sha.ToLowerInvariant()) : null;
            if (_updateItem is not null) { _updateItem.Text = $"Install update v{latest}"; _updateItem.Visible = true; }
            _tray.ShowBalloonTip(10000, "PROGNODE Client update", $"Version {latest} is available. Click to install it; alarms continue right after.", ToolTipIcon.Info);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            // No internet on the plant network is normal; alarms keep working.
        }
    }

    /// <summary>
    /// Downloads the announced installer, checks its SHA-256 against the release feed, installs it silently
    /// for this Windows user and starts the new version. Without a verified download it opens the Downloads page.
    /// </summary>
    private async Task InstallUpdateAsync()
    {
        if (_updating) return;
        if (_update is not { } update) { OpenDownloads(); return; }
        _updating = true;
        if (_updateItem is not null) _updateItem.Enabled = false;
        var file = Path.Combine(Path.GetTempPath(), $"PROGNODE-Client-Setup-{update.Version}.exe");
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            var bytes = await http.GetByteArrayAsync(update.Url);
            if (!string.Equals(Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes)), update.Sha256, StringComparison.Ordinal))
                throw new InvalidDataException("The download does not match the published SHA-256.");
            await File.WriteAllBytesAsync(file, bytes);
            var exe = Environment.ProcessPath!;
            // Setup closes this app, installs, then the new version starts in the background (tray).
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe",
                $"/c \"\"{file}\" /VERYSILENT /SUPPRESSMSGBOXES /NORESTART && start \"\" \"{exe}\" --background\"")
            { UseShellExecute = false, CreateNoWindow = true });
            ExitThread();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidDataException or IOException)
        {
            try { File.Delete(file); } catch (IOException) { }
            _tray.ShowBalloonTip(8000, "Update not installed", ex.Message, ToolTipIcon.Warning);
            _updating = false;
            if (_updateItem is not null) _updateItem.Enabled = true;
        }
    }

    private static void OpenDownloads() =>
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(DownloadsUrl) { UseShellExecute = true });

    protected override void ExitThreadCore()
    {
        _timer.Stop();
        _updateTimer.Stop();
        _tray.Visible = false;
        _tray.Dispose();
        _toasts.Dispose();
        _core?.Dispose();
        _window?.Dispose();
        base.ExitThreadCore();
    }
}
