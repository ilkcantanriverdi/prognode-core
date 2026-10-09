using Prognode.Contracts;
using Prognode.Core.Connectivity;
using Prognode.Licensing;

namespace Prognode.Host.Services;

/// <summary>
/// Connects this Core to a PROGNODE account without handling license files: Core shows a code and
/// opens account.prognode.io/link; after the user starts the free trial (or picks one of their
/// licenses) Core receives the signed license, verifies and installs it exactly like an import, and
/// CoreCloudLicenseHostedService activates it on its next pass.
/// </summary>
public sealed class CoreLinkService(
    CoreCloudLicenseClient cloud,
    LicenseImportService importer,
    CoreCloudLicenseStateStore cloudState,
    ServerAccessService serverAccess,
    IMachineFingerprintProvider machine,
    ILogger<CoreLinkService> logger)
{
    public sealed record Status(string State, string? UserCode, string? VerificationUri, DateTimeOffset? ExpiresAtUtc, string? Message);

    private readonly object _gate = new();
    private CancellationTokenSource? _polling;
    private Status _status = new("IDLE", null, null, null, null);

    public Status Current { get { lock (_gate) return _status; } }

    public async Task<Status> StartAsync(CancellationToken cancellationToken)
    {
        if (!cloud.IsConfigured) return Set(new Status("FAILED", null, null, null, "PROGNODE Cloud is not configured on this Core."));
        Cancel();
        try
        {
            var ticket = await cloud.StartLinkAsync(
                serverAccess.Identity.ServerId, serverAccess.Identity.DisplayName, machine.GetFingerprint(), ProductVersion.Current, cancellationToken);
            var cts = new CancellationTokenSource();
            lock (_gate) _polling = cts;
            var started = Set(new Status("WAITING", ticket.UserCode, ticket.VerificationUriComplete, ticket.ExpiresAtUtc, null));
            _ = Task.Run(() => PollAsync(ticket, cts.Token));
            return started;
        }
        catch (Exception ex) when (ex is InvalidOperationException or HttpRequestException)
        {
            logger.LogWarning(ex, "PROGNODE account connect could not start.");
            var message = ex is CloudLicenseRejectedException { Code: "too_many_requests" }
                ? "Too many attempts. Wait a few minutes and try again."
                : "PROGNODE Cloud could not be reached. Check the internet connection of this PC and try again.";
            return Set(new Status("FAILED", null, null, null, message));
        }
    }

    public void Cancel()
    {
        lock (_gate)
        {
            _polling?.Cancel();
            _polling = null;
            if (_status.State == "WAITING") _status = new Status("IDLE", null, null, null, null);
        }
    }

    private async Task PollAsync(CoreLinkTicket ticket, CancellationToken token)
    {
        var interval = TimeSpan.FromSeconds(ticket.IntervalSeconds);
        while (!token.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(interval, token);
                if (DateTimeOffset.UtcNow >= ticket.ExpiresAtUtc)
                {
                    Finish(token, new Status("EXPIRED", null, null, null, "The code expired. Start again to get a new code."));
                    return;
                }
                var poll = await cloud.PollLinkAsync(ticket, token);
                switch (poll.Status)
                {
                    case "pending":
                        continue;
                    case "slow_down":
                        interval += TimeSpan.FromSeconds(2);
                        continue;
                    case "denied":
                        Finish(token, new Status("DENIED", null, null, null, "The request was declined in PROGNODE Account."));
                        return;
                    case "approved" when poll.SignedLicenseDocument is { } document:
                        await InstallAsync(document, token);
                        return;
                    default:
                        Finish(token, new Status("EXPIRED", null, null, null, "The code expired. Start again to get a new code."));
                        return;
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException or TaskCanceledException or System.Text.Json.JsonException)
            {
                // Temporary network trouble: keep polling until the code expires.
                logger.LogDebug(ex, "PROGNODE account connect poll failed; retrying.");
            }
        }
    }

    private async Task InstallAsync(byte[] document, CancellationToken token)
    {
        try
        {
            await using var stream = new MemoryStream(document, writable: false);
            // Same trust path as a manual import: envelope, Ed25519 signature, claims, lifecycle.
            await importer.ImportAsync(stream, token);
            cloudState.Clear();
            Finish(token, new Status("CONNECTED", null, null, null, null));
            logger.LogInformation("PROGNODE license received from PROGNODE Account and installed.");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Text.Json.JsonException)
        {
            logger.LogWarning(ex, "The license received from PROGNODE Account was rejected.");
            Finish(token, new Status("FAILED", null, null, null, $"The license could not be installed: {ex.Message}"));
        }
    }

    private void Finish(CancellationToken token, Status status)
    {
        lock (_gate)
        {
            if (token.IsCancellationRequested) return;
            _status = status;
            _polling = null;
        }
    }

    private Status Set(Status status)
    {
        lock (_gate) _status = status;
        return status;
    }
}
