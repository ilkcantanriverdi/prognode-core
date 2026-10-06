using Microsoft.Windows.AppNotifications;
using Microsoft.Windows.AppNotifications.Builder;

namespace Prognode.Agent.Windows;

/// <summary>
/// Native Windows app-notification delivery. App identity is registered as PROGNODE,
/// so the technical assembly/namespace is not exposed in the Windows notification UI.
/// </summary>
public sealed class WindowsNotificationService : IDisposable
{
    private AppNotificationManager? _manager;
    private readonly Action _onActivated;
    private bool _registered;

    public WindowsNotificationService(Action onActivated)
    {
        _onActivated = onActivated;

        try
        {
            if (!AppNotificationManager.IsSupported())
                return;

            _manager = AppNotificationManager.Default;
            _manager.NotificationInvoked += OnNotificationInvoked;

            var iconPath = Path.Combine(
                AppContext.BaseDirectory,
                "Assets",
                "prognode.png");

            if (File.Exists(iconPath))
            {
                _manager.Register(
                    "PROGNODE",
                    new Uri(iconPath));
            }
            else
            {
                _manager.Register();
            }

            _registered = true;
        }
        catch
        {
            if (_manager is not null)
                _manager.NotificationInvoked -= OnNotificationInvoked;

            _manager = null;
            _registered = false;
        }
    }

    public bool IsAvailable => _registered && _manager is not null;

    public bool Show(string message)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(message))
            return false;

        try
        {
            var notification =
                new AppNotificationBuilder()
                    .AddText(message.Trim())
                    .BuildNotification();

            _manager!.Show(notification);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private void OnNotificationInvoked(
        AppNotificationManager sender,
        AppNotificationActivatedEventArgs args)
    {
        try
        {
            _onActivated();
        }
        catch
        {
            // A notification click must never terminate the tray agent.
        }
    }

    public void Dispose()
    {
        if (_manager is null)
            return;

        try
        {
            _manager.NotificationInvoked -= OnNotificationInvoked;

            if (_registered)
                _manager.Unregister();
        }
        catch
        {
            // Best-effort cleanup on process exit.
        }

        _registered = false;
        _manager = null;
    }
}
