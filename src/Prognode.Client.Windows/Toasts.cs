using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Prognode.Client.Windows;

/// <summary>Windows notifications with an ACK button. Invocations are marshalled to the UI thread.</summary>
public sealed class Toasts : IDisposable
{
    private readonly AppNotificationManager? _manager;
    private readonly SynchronizationContext _ui;
    private readonly Action<Guid?> _onOpen;
    private readonly Action<Guid> _onAck;

    public Toasts(SynchronizationContext ui, Action<Guid?> onOpen, Action<Guid> onAck)
    {
        _ui = ui;
        _onOpen = onOpen;
        _onAck = onAck;
        try
        {
            if (!AppNotificationManager.IsSupported()) return;
            _manager = AppNotificationManager.Default;
            _manager.NotificationInvoked += OnInvoked;
            var icon = Path.Combine(AppContext.BaseDirectory, "Assets", "prognode.png");
            if (File.Exists(icon)) _manager.Register("PROGNODE", new Uri(icon));
            else _manager.Register();
        }
        catch
        {
            _manager = null;
        }
    }

    public bool Available => _manager is not null;

    public void Show(string title, string body, Guid? occurrenceId, bool critical, bool canAck)
    {
        if (_manager is null) return;
        try
        {
            var builder = new AppNotificationBuilder()
                .AddArgument("action", "open")
                .AddArgument("occurrence", occurrenceId?.ToString("D") ?? "")
                .AddText(title)
                .AddText(body);
            if (critical) builder.SetScenario(AppNotificationScenario.Urgent);
            if (canAck && occurrenceId is { } id)
                builder.AddButton(new AppNotificationButton("ACK").AddArgument("action", "ack").AddArgument("occurrence", id.ToString("D")));
            builder.AddButton(new AppNotificationButton("Open").AddArgument("action", "open").AddArgument("occurrence", occurrenceId?.ToString("D") ?? ""));
            var notification = builder.BuildNotification();
            if (occurrenceId is { } tagId) notification.Tag = tagId.ToString("N")[..16];
            _manager.Show(notification);
        }
        catch
        {
            // A notification failure must never stop alarm monitoring.
        }
    }

    public void RemoveFor(Guid occurrenceId)
    {
        try { _ = _manager?.RemoveByTagAsync(occurrenceId.ToString("N")[..16]); } catch { }
    }

    private void OnInvoked(AppNotificationManager sender, AppNotificationActivatedEventArgs args)
    {
        var action = args.Arguments.TryGetValue("action", out var a) ? a : "open";
        var occurrence = args.Arguments.TryGetValue("occurrence", out var o) && Guid.TryParse(o, out var id) ? id : (Guid?)null;
        _ui.Post(_ =>
        {
            if (action == "ack" && occurrence is { } ackId) _onAck(ackId);
            else _onOpen(occurrence);
        }, null);
    }

    public void Dispose()
    {
        if (_manager is null) return;
        try
        {
            _manager.NotificationInvoked -= OnInvoked;
            _manager.Unregister();
        }
        catch { }
    }
}
