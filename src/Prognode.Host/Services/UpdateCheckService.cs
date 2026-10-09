using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Prognode.Contracts;
using Prognode.Licensing;

namespace Prognode.Host.Services;

/// <summary>
/// Asks PROGNODE Account which Core version is current, shortly after start and then every six hours.
/// Installing is started by a signed-in user (the monitoring service restarts for about a minute, so the
/// plant chooses the moment): the installer is downloaded, its SHA-256 checked against the release
/// feed from the trusted PROGNODE host, and run silently; it replaces the service and keeps all data.
/// </summary>
public sealed class UpdateCheckService(CoreCloudLicenseOptions options, ILogger<UpdateCheckService> logger) : BackgroundService
{
    public sealed record Status(
        string CurrentVersion, string? LatestVersion, bool UpdateAvailable, string? DownloadsUrl, DateTimeOffset? CheckedAtUtc,
        string State = "IDLE", int? Progress = null, string? Message = null);

    private sealed record Installer(string Version, Uri Url, string Sha256);

    private static readonly TimeSpan Interval = TimeSpan.FromHours(6);
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly HttpClient _download = new() { Timeout = TimeSpan.FromMinutes(15) };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Status _status = new(ProductVersion.Current, null, false, null, null);
    private Installer? _installer;
    private int _installing;

    public Status Current => _status;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (ProductVersion.IsDevelopmentBuild(ProductVersion.Current)) return;
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        while (!stoppingToken.IsCancellationRequested)
        {
            await CheckAsync(stoppingToken);
            await Task.Delay(Interval, stoppingToken);
        }
    }

    /// <summary>Runs one check now; manual checks are limited to one per minute.</summary>
    public async Task<Status> CheckAsync(CancellationToken cancellationToken)
    {
        if (ProductVersion.IsDevelopmentBuild(ProductVersion.Current) || _installing == 1) return _status;
        if (_status.CheckedAtUtc is { } last && DateTimeOffset.UtcNow - last < TimeSpan.FromMinutes(1)) return _status;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            foreach (var baseUrl in options.CandidateBaseUrls)
            {
                try
                {
                    using var response = await _http.GetAsync(new Uri(new Uri(baseUrl + "/"), "api/releases/latest"), cancellationToken);
                    if (!response.IsSuccessStatusCode) continue;
                    using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                    var root = document.RootElement;
                    var core = root.TryGetProperty("core", out var c) && c.ValueKind == JsonValueKind.Object ? c : (JsonElement?)null;
                    var latest = Text(core, "version");
                    var page = root.TryGetProperty("downloadsUrl", out var url) && url.ValueKind == JsonValueKind.String ? url.GetString() : null;
                    // The link shown in Core must point at PROGNODE, whatever the response says.
                    if (!CoreCloudLicenseOptions.IsAllowedBaseUrl(page)) page = $"{baseUrl}/downloads";
                    var newer = latest is not null && ProductVersion.Compare(latest, ProductVersion.Current) > 0;
                    _installer = newer && Uri.TryCreate(Text(core, "url"), UriKind.Absolute, out var setup) && IsAllowedDownload(setup) &&
                        Text(core, "sha256") is { Length: 64 } sha && sha.All(Uri.IsHexDigit)
                        ? new Installer(latest!, setup, sha.ToLowerInvariant()) : null;
                    _status = new Status(ProductVersion.Current, latest, newer, page, DateTimeOffset.UtcNow);
                    if (newer) logger.LogInformation("PROGNODE Core {Latest} is available (running {Current}).", latest, ProductVersion.Current);
                    return _status;
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
                {
                    logger.LogDebug(ex, "Update check against {Host} failed.", baseUrl);
                }
            }
            // Offline sites simply keep the last known answer.
            _status = _status with { CheckedAtUtc = DateTimeOffset.UtcNow };
            return _status;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Downloads, verifies and silently runs the announced installer. Returns at once; poll <see cref="Current"/>.</summary>
    public Status StartInstall()
    {
        if (!OperatingSystem.IsWindows()) return _status with { State = "FAILED", Message = "Updates install on Windows only." };
        if (_installer is not { } installer || !_status.UpdateAvailable)
            return _status with { State = "FAILED", Message = "No verified update is available. Check again or download it from PROGNODE Account." };
        if (Interlocked.Exchange(ref _installing, 1) == 1) return _status;
        _status = _status with { State = "DOWNLOADING", Progress = 0, Message = null };
        _ = Task.Run(() => InstallAsync(installer));
        return _status;
    }

    private async Task InstallAsync(Installer installer)
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PROGNODE", "updates");
        var file = Path.Combine(folder, $"PROGNODE-Core-Setup-{installer.Version}.exe");
        try
        {
            Directory.CreateDirectory(folder);
            foreach (var old in Directory.GetFiles(folder, "PROGNODE-Core-Setup-*.exe")) File.Delete(old);
            using (var response = await _download.GetAsync(installer.Url, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength;
                await using var source = await response.Content.ReadAsStreamAsync();
                await using var target = File.Create(file);
                using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new byte[81920];
                long read = 0;
                int n;
                while ((n = await source.ReadAsync(buffer)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, n));
                    hash.AppendData(buffer, 0, n);
                    read += n;
                    if (total > 0) _status = _status with { Progress = (int)(read * 100 / total.Value) };
                }
                if (!string.Equals(Convert.ToHexStringLower(hash.GetHashAndReset()), installer.Sha256, StringComparison.Ordinal))
                    throw new InvalidDataException("The downloaded installer does not match the published SHA-256.");
            }

            _status = _status with { State = "INSTALLING", Progress = 100, Message = "PROGNODE is updating and restarts in about a minute. This page reconnects by itself." };
            logger.LogWarning("Installing PROGNODE Core {Version}; the service restarts.", installer.Version);
            // The installer stops this service, replaces the files and starts the new version; data stays in ProgramData.
            Process.Start(new ProcessStartInfo(file,
                $"/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=\"{Path.Combine(folder, "update.log")}\"")
            { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = folder });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "PROGNODE Core update {Version} failed before installation.", installer.Version);
            try { File.Delete(file); } catch (IOException) { }
            _status = _status with { State = "FAILED", Progress = null, Message = $"The update could not be installed: {ex.Message}" };
            Interlocked.Exchange(ref _installing, 0);
        }
    }

    /// <summary>Installers come from the PROGNODE release store (Vercel Blob) or a PROGNODE host, always over HTTPS.</summary>
    private static bool IsAllowedDownload(Uri url)
    {
#if DEBUG
        if (url.IsLoopback) return true;
#endif
        return url.Scheme == Uri.UriSchemeHttps &&
            (url.Host.EndsWith(".public.blob.vercel-storage.com", StringComparison.OrdinalIgnoreCase) ||
             CoreCloudLicenseOptions.IsAllowedBaseUrl($"https://{url.Host}"));
    }

    private static string? Text(JsonElement? element, string name) =>
        element is { } e && e.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public override void Dispose()
    {
        _http.Dispose();
        _download.Dispose();
        base.Dispose();
    }
}
