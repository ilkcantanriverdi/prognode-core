using System.Text.Json;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Prognode.Client.Windows;

/// <summary>
/// The PROGNODE Client window: the page in Assets/ui (same look as PROGNODE Core) hosted in WebView2.
/// The page only renders what <see cref="Post"/> sends and raises <see cref="Command"/> for user actions;
/// it can load nothing but its own files. Closing the window keeps the client in the tray.
/// </summary>
public sealed class ClientWindow : Form
{
    private const string Origin = "https://app.prognode.invalid/";
    private static readonly Color Background = Color.FromArgb(8, 18, 29);
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Background };
    private bool _ready;

    /// <summary>A command from the page, e.g. {"cmd":"ack","id":"…"}. "ready" means the page wants the full state.</summary>
    public event Action<JsonElement>? Command;
    /// <summary>The user closed the window (it is hidden, not destroyed).</summary>
    public event Action? Hidden;

    public ClientWindow()
    {
        Text = "PROGNODE";
        BackColor = Background;
        MinimumSize = new Size(900, 560);
        Size = new Size(1120, 700);
        StartPosition = FormStartPosition.CenterScreen;
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "prognode.ico");
        if (File.Exists(iconPath)) Icon = new Icon(iconPath);
        Controls.Add(_web);
        Load += async (_, _) => await InitializeAsync();
        FormClosing += (_, e) =>
        {
            if (e.CloseReason != CloseReason.UserClosing) return;
            e.Cancel = true;
            Hide();
            Hidden?.Invoke();
        };
    }

    private async Task InitializeAsync()
    {
        try
        {
            var data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PROGNODE", "Client", "WebView2");
            var environment = await CoreWebView2Environment.CreateAsync(null, data);
            await _web.EnsureCoreWebView2Async(environment);
            var core = _web.CoreWebView2;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.IsZoomControlEnabled = false;
            core.Settings.IsGeneralAutofillEnabled = false;
            core.Settings.IsPasswordAutosaveEnabled = false;
            core.SetVirtualHostNameToFolderMapping("app.prognode.invalid",
                Path.Combine(AppContext.BaseDirectory, "Assets"), CoreWebView2HostResourceAccessKind.Deny);
            core.NavigationStarting += (_, e) => { if (!e.Uri.StartsWith(Origin, StringComparison.OrdinalIgnoreCase)) e.Cancel = true; };
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.WebMessageReceived += OnMessage;
            core.Navigate(Origin + "ui/index.html");
        }
        catch (Exception ex) when (ex is WebView2RuntimeNotFoundException or System.Runtime.InteropServices.COMException or InvalidOperationException)
        {
            Controls.Remove(_web);
            var link = new LinkLabel
            {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, ForeColor = Color.FromArgb(238, 245, 250),
                LinkColor = Color.FromArgb(37, 200, 217), Font = new Font("Segoe UI", 11f),
                Text = "This window needs the Microsoft Edge WebView2 Runtime, which this PC does not have.\n" +
                       "Alarm notifications keep working in the meantime.\n\nDownload WebView2 Runtime",
            };
            link.LinkArea = new LinkArea(link.Text.LastIndexOf("Download", StringComparison.Ordinal), "Download WebView2 Runtime".Length);
            link.LinkClicked += (_, _) => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
                "https://developer.microsoft.com/microsoft-edge/webview2/") { UseShellExecute = true });
            Controls.Add(link);
        }
    }

    private void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!e.Source.StartsWith(Origin, StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var message = document.RootElement.Clone();
            if (message.TryGetProperty("cmd", out var cmd) && cmd.GetString() == "ready") _ready = true;
            Command?.Invoke(message);
        }
        catch (JsonException) { }
    }

    /// <summary>Sends a message to the page. Before the page is ready nothing is sent; the page asks for the state when it loads.</summary>
    public void Post(object message)
    {
        if (!_ready || _web.CoreWebView2 is null || IsDisposed) return;
        _web.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(message, CoreConnection.Json));
    }

    public void ShowAndActivate()
    {
        Show();
        if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
        Activate();
    }
}
