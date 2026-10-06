using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Prognode.Host.Services;

/// <summary>
/// Pilot-only child of START_PROGNODE.cmd. A local elevated helper places a
/// PID-specific request in the administrator-owned ProgramData/config folder.
/// Core only shuts itself down; the trusted local launch supervisor restarts it
/// with the ORIGINAL data root, binary, service identity and certificate pin.
/// This is never an HTTP endpoint and it is disabled for normal services.
/// </summary>
public sealed class LanAutoRestartHostedService(
    IHostApplicationLifetime lifetime, ILogger<LanAutoRestartHostedService> log) : BackgroundService
{
    private readonly DateTimeOffset _startedUtc = DateTimeOffset.UtcNow;
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!OperatingSystem.IsWindows() ||
            Environment.GetEnvironmentVariable("PROGNODE_CORE_SUPERVISED") != "1") return;

        var requestPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "PROGNODE", "config", "lan-restart-request.json");
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (File.Exists(requestPath))
                {
                    using var doc = JsonDocument.Parse(File.ReadAllText(requestPath));
                    var root = doc.RootElement;
                    if (root.TryGetProperty("processId", out var pid) &&
                        pid.ValueKind == JsonValueKind.Number &&
                        pid.GetInt32() == Environment.ProcessId &&
                        root.TryGetProperty("requestedAtUtc", out var time) &&
                        time.TryGetDateTimeOffset(out var requested) &&
                        requested >= _startedUtc &&
                        requested <= DateTimeOffset.UtcNow.AddSeconds(10) &&
                        DateTimeOffset.UtcNow - requested < TimeSpan.FromMinutes(3))
                    {
                        log.LogInformation("Locally approved LAN setup requires Core restart; stopping cleanly under the local supervisor.");
                        lifetime.StopApplication();
                        return;
                    }
                }
            }
            catch (IOException ex) { log.LogWarning(ex, "LAN restart request could not be read"); }
            catch (UnauthorizedAccessException ex) { log.LogWarning(ex, "LAN restart request ACL blocked access"); }
            catch (JsonException ex) { log.LogWarning(ex, "Invalid LAN restart marker; refusing restart"); }
            await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
        }
    }
}
