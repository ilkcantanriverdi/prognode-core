using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace Prognode.Agent.Windows;

/// <summary>
/// Finds a browser window that already shows PROGNODE and brings it to the front, so a click on a
/// notification does not open one more tab. A browser window's title is the title of its active
/// tab ("PROGNODE" or "(3) PROGNODE" followed by the browser name).
/// </summary>
internal static class BrowserWindows
{
    // "PROGNODE", "(3) PROGNODE", optionally "and 2 more pages", then only the browser's own
    // suffix (profile name, browser name). "PROGNODE — Industrial Monitoring…" (the website) or
    // "PROGNODE Account" do not match.
    private static readonly Regex PrognodeTitle = new(
        @"^(\(\d+\)\s+)?PROGNODE( and \d+ more pages?)?(( [-—–] [^-—–]{1,30}){0,2} [-—–] [^-—–]*(Chrome|Edge|Firefox|Brave|Opera|Vivaldi|Chromium|Arc))?$",
        RegexOptions.CultureInvariant);

    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
    {
        "msedge", "chrome", "firefox", "brave", "opera", "opera_gx", "vivaldi", "chromium", "arc"
    };

    public static bool TryActivatePrognode()
    {
        var window = FindPrognodeWindow();
        if (window == IntPtr.Zero)
            return false;

        if (IsIconic(window))
            ShowWindow(window, SW_RESTORE);

        SetForegroundWindow(window);
        return true;
    }

    private static IntPtr FindPrognodeWindow()
    {
        var found = IntPtr.Zero;
        EnumWindows((window, _) =>
        {
            if (!IsWindowVisible(window))
                return true;

            var length = GetWindowTextLength(window);
            if (length <= 0)
                return true;

            var title = new StringBuilder(length + 1);
            GetWindowText(window, title, title.Capacity);
            if (!PrognodeTitle.IsMatch(title.ToString()))
                return true;

            GetWindowThreadProcessId(window, out var processId);
            try
            {
                using var process = Process.GetProcessById((int)processId);
                if (!Browsers.Contains(process.ProcessName))
                    return true;
            }
            catch
            {
                return true;
            }

            found = window;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    private const int SW_RESTORE = 9;

    private delegate bool EnumWindowsProc(IntPtr window, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr parameter);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(IntPtr window);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr window, StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
}
