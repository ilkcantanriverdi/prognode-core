using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Windows.Forms;

namespace Prognode.Agent.Windows;

// Deliberate physical-presence gate: a browser or inbound network client cannot
// start UAC. The user must right-click the locally running Windows tray Agent.
internal static class LanAccessSetup
{
    internal sealed record Pending(string RequestId, DateTimeOffset ExpiresAtUtc,
        string Action, int InterfaceIndex, string InterfaceName, string Profile,
        string SelectedHost, string ScopeType, string[] RemoteAddresses, int Port,
        string ConfigPath, bool RequiresPublicConsent, string RuleName, string? Warning);

    // ASP.NET's Results.Ok(null) can return HTTP 200 with an EMPTY body.
    // Never feed empty, null or non-JSON responses to System.Text.Json.
    // The Core HF4.1.2 endpoint instead returns JSON 404 for no pending request;
    // this Agent also tolerates older, still-running Core versions during upgrades.
    private static async Task<Pending?> ReadPendingAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (string.IsNullOrWhiteSpace(body) || string.Equals(body.Trim(), "null", StringComparison.OrdinalIgnoreCase))
            return null;
        try
        {
            return JsonSerializer.Deserialize<Pending>(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                "The Core returned an invalid LAN setup response. Start the matching HF4.1.2 Core and Agent, " +
                "then press Prepare LAN access again.", ex);
        }
    }

    private static void ShowPrepareAgain() =>
        MessageBox.Show(
            "No active LAN setup request is available in this Core. Open Settings → Mobile Access, " +
            "confirm the selected Public LAN if applicable, and press Prepare LAN access again. " +
            "Complete the Agent action before the three-minute request expires. " +
            "If it persists, confirm the Agent targets the same local Core (127.0.0.1:5080).",
            "PROGNODE LAN Access", MessageBoxButtons.OK, MessageBoxIcon.Information);

    public static async Task RunAsync(HttpClient localCore)
    {
        try
        {
            if (!localCore.BaseAddress!.IsLoopback)
                throw new InvalidOperationException("The setup helper only works against a loopback Core URL.");
            using var pendingResponse = await localCore.GetAsync("/api/mobile-access/agent/pending");
            if (pendingResponse.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.NoContent)
            {
                ShowPrepareAgain();
                return;
            }
            if (!pendingResponse.IsSuccessStatusCode)
                throw new InvalidOperationException($"Core rejected the pending LAN request (HTTP {(int)pendingResponse.StatusCode}). " +
                    "Check that the Agent and Core are running from the same HF4.1.2 package and the Agent uses localhost:5080.");
            var pending = await ReadPendingAsync(pendingResponse);
            if (pending is null || pending.ExpiresAtUtc <= DateTimeOffset.UtcNow)
            {
                ShowPrepareAgain();
                return;
            }
            var preview = $"Action: {pending.Action}\nWindows adapter: {pending.InterfaceName} " +
                $"(index {pending.InterfaceIndex})\nProfile: {pending.Profile}\n" +
                $"Local IP: {pending.SelectedHost}\nTCP: {pending.Port}\nAllowed sources: " +
                string.Join(", ", pending.RemoteAddresses) + "\n\n" +
                (pending.Profile == "Public" ?
                    "CAUTION: This is a Windows PUBLIC interface. Only approve a trusted plant LAN.\n\n" : "") +
                "Continue to the Windows administrator (UAC) confirmation?";
            if (MessageBox.Show(preview, "PROGNODE — Confirm LAN firewall setup", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

            var script = Path.Combine(AppContext.BaseDirectory, "LAN_ACCESS_ELEVATED.ps1");
            if (!File.Exists(script))
                throw new FileNotFoundException("The installed PROGNODE LAN helper is missing.", script);
            var response = await localCore.PostAsJsonAsync("/api/mobile-access/agent/claim",
                new { pending.RequestId });
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException("The administrator session or three-minute request expired. Prepare again.");
            var claimed = await ReadPendingAsync(response);
            if (claimed is null || claimed.RequestId != pending.RequestId ||
                claimed.ExpiresAtUtc <= DateTimeOffset.UtcNow)
                throw new InvalidOperationException("The local setup request has changed. Prepare again.");

            var psi = new ProcessStartInfo("powershell.exe")
            {
                UseShellExecute = true, Verb = "runas", WorkingDirectory = AppContext.BaseDirectory
            };
            // Each argument is typed/allowlisted server-side and validated a second
            // time by the elevated PowerShell helper against live Windows NIC state.
            foreach (var parameter in new[] {
                "-NoProfile", "-ExecutionPolicy", "Bypass", "-NoExit", "-File", script,
                "-Action", claimed.Action, "-InterfaceIndex", claimed.InterfaceIndex.ToString(),
                "-SelectedHost", claimed.SelectedHost, "-Profile", claimed.Profile,
                "-ScopeType", claimed.ScopeType, "-RemoteAddresses", string.Join(",", claimed.RemoteAddresses),
                "-ConfigPath", claimed.ConfigPath, "-RequestId", claimed.RequestId
            }) psi.ArgumentList.Add(parameter);
            Process.Start(psi);
            MessageBox.Show("Complete the UAC confirmation and wait for the helper result. " +
                "The installed Core service or the supervised source pilot restarts automatically after a successful repair. If using direct Visual Studio debugging, restart that debug process once. Then press Verify and test from your phone.",
                "PROGNODE LAN Access", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Win32Exception error) when (error.NativeErrorCode == 1223)
        {
            MessageBox.Show("Administrator consent was cancelled. Firewall settings were not changed.",
                "PROGNODE LAN Access", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception error)
        {
            MessageBox.Show(error.Message, "PROGNODE LAN Access", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
