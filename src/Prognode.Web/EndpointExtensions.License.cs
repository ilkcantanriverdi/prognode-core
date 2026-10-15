using Prognode.Contracts.Devices;
using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;
using Prognode.Alarm;
using Prognode.Contracts.Alarms;
using Prognode.Core;
using Prognode.Core.Devices;
using Prognode.Core.Connectivity;
using Prognode.Core.Diagnostics;
using Prognode.Core.Protocols;
using Prognode.Core.Tags;
using Prognode.Core.Batches;
using Prognode.Historian;
using Prognode.Licensing;
using Prognode.Notifications;
using Prognode.Protocols.Modbus;
using Prognode.Protocols.S7;
using Prognode.Protocols.Abstractions;
using Prognode.RemoteAccess;
using Prognode.Contracts.RemoteAccess;
using Prognode.Contracts.Licensing;
using Prognode.Trends;
using Prognode.Web.Models;

namespace Prognode.Web;

public static partial class EndpointExtensions
{
    private static void MapLicenseEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/health",
            (SystemStatusService status) =>
                Results.Ok(status.GetSnapshot()));

        endpoints.MapGet(
            "/api/license",
            (HttpContext context, LicenseService license, LocalAccessSessionService sessions) =>
            {
                var current = license.Current;
                var authenticated = sessions.Validate(context.Request.Headers["X-PROGNODE-Session"].ToString());
                return authenticated
                    ? Results.Ok(current)
                    : Results.Ok(SignedOutLicenseSummary(current));
            });

        endpoints.MapGet(
            "/api/cloud-license/status",
            (CoreCloudLicenseClient cloud, CoreCloudLicenseStateStore stateStore) =>
                Results.Ok(stateStore.Load(cloud.IsConfigured)));

        endpoints.MapGet(
            "/api/license/usage",
            async (HttpContext context, LicenseService license, LocalAccessSessionService sessions, TagService tags, CancellationToken ct) =>
            {
                var authenticated = sessions.Validate(context.Request.Headers["X-PROGNODE-Session"].ToString());
                if (!authenticated)
                {
                    // Signed-out browser clients may know that a license is installed, but commercial
                    // capacity/usage metadata stays hidden until the licensed account signs in.
                    return Results.Ok(new
                    {
                        visible = false,
                        maxTags = (int?)null,
                        usedTags = (int?)null,
                        remainingTags = (int?)null,
                        unlimited = false,
                        legacyUnlimited = false,
                        overCapacity = false,
                        capacityStatus = "SIGN_IN_REQUIRED"
                    });
                }

                var current = license.Current;
                var usedTags = await tags.CountAsync(ct);
                var limit = current.Entitlements.MonitoredSignals;
                var remaining = limit.IsUnlimited
                    ? (int?)null
                    : Math.Max(0, limit.Value!.Value - usedTags);

                var overCapacity = !limit.IsUnlimited && usedTags > limit.Value!.Value;
                return Results.Ok(new
                {
                    visible = true,
                    maxTags = limit.IsUnlimited ? (int?)null : limit.Value,
                    usedTags,
                    remainingTags = remaining,
                    unlimited = limit.IsUnlimited,
                    legacyUnlimited = current.LegacyUnlimitedTagCapacity,
                    overCapacity,
                    capacityStatus = limit.IsUnlimited
                        ? "UNLIMITED"
                        : overCapacity ? "OVER_CAPACITY" : "WITHIN_CAPACITY"
                });
            });

        // --- Activation (allowed while signed out: a license cannot be used until activated) -------
        endpoints.MapGet(
            "/api/license/activation",
            (FileBackedLicenseProvider provider, CoreCloudLicenseStateStore cloudState, TrustedClock clock) =>
            {
                var activation = provider.GetActivation();
                if (activation is null)
                    return Results.Ok(new { licenseInstalled = false, activated = false, clockRollbackDetected = clock.RollbackDetected });
                var cloud = cloudState.Load(configured: true);
                return Results.Ok(new
                {
                    licenseInstalled = true,
                    activated = activation.Activated,
                    reason = activation.Reason,
                    activatedAtUtc = activation.Activated ? activation.Certificate?.ActivatedAtUtc : null,
                    lastError = activation.Activated ? null : cloud.LastError,
                    clockRollbackDetected = clock.RollbackDetected,
                });
            });

        endpoints.MapGet(
            "/api/license/activation/request",
            (FileBackedLicenseProvider provider, ServerAccessService server) =>
            {
                try
                {
                    var request = provider.CreateActivationRequest(server.Identity.DisplayName, CoreVersionInfo.Current);
                    return Results.File(LicenseActivationService.SerializeRequest(request), "application/json",
                        $"prognode-activation-request-{request.ServerId:N}.pgnreq");
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new { message = ex.Message });
                }
            });

        endpoints.MapPost(
            "/api/license/activation/import",
            async (HttpRequest request, FileBackedLicenseProvider provider, CoreCloudLicenseStateStore cloudState, TrustedClock clock, CancellationToken ct) =>
            {
                try
                {
                    var form = await request.ReadFormAsync(ct);
                    var file = form.Files.GetFile("activation") ?? form.Files.FirstOrDefault();
                    if (file is null || file.Length == 0)
                        return Results.BadRequest(new { message = "Choose a .pgnact activation file." });
                    if (file.Length > 64 * 1024)
                        return Results.BadRequest(new { message = "The activation file is too large." });
                    await using var stream = new MemoryStream();
                    await file.CopyToAsync(stream, ct);
                    var certificate = provider.ImportActivation(stream.ToArray());
                    clock.ObserveSigned(certificate.IssuedAtUtc);
                    var previous = cloudState.Load(configured: true);
                    cloudState.Save(previous with { LastError = null });
                    return Results.Ok(new { activated = true, activatedAtUtc = certificate.ActivatedAtUtc });
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new { message = ex.Message });
                }
            });

        endpoints.MapPost(
            "/api/license/import",
            async (
                HttpRequest request,
                LicenseImportService importer,
                LicenseService license,
                CoreCloudLicenseStateStore cloudLicenseState,
                CancellationToken ct) =>
            {
                try
                {
                    var form = await request.ReadFormAsync(ct);
                    var file = form.Files.GetFile("license") ?? form.Files.FirstOrDefault();
                    if (file is null || file.Length == 0)
                        return Results.BadRequest(new { message = "Choose a .pgnlicense file." });

                    var previousCloud = cloudLicenseState.Load(configured: true);
                    await using var stream = file.OpenReadStream();
                    await importer.ImportAsync(stream, ct);
                    var current = license.Current;

                    // A persisted revoke must NOT be bypassable by re-importing the same signed file
                    // while offline. Preserve state for the same licenseId and force the next heartbeat.
                    // A genuinely different licenseId starts a fresh activation.
                    if (!string.IsNullOrWhiteSpace(previousCloud.LicenseId) &&
                        string.Equals(previousCloud.LicenseId, current.LicenseId, StringComparison.OrdinalIgnoreCase))
                    {
                        cloudLicenseState.Save(previousCloud with
                        {
                            LastSyncedAtUtc = null,
                            LastError = null
                        });
                    }
                    else
                    {
                        cloudLicenseState.Clear();
                    }

                    // Import is allowed while signed out, but do not return expiry/capacity/entitlement
                    // metadata. Return only the account hint needed to continue with offline sign-in.
                    return Results.Ok(new
                    {
                        imported = true,
                        licenseInstalled = true, // ImportAsync returned successfully: verified signed file is persisted.
                        assignedUserName = current.AssignedUserName,
                        assignedUserEmail = current.AssignedUserEmail,
                        signInRequired = true
                    });
                }
                catch (Exception ex) when (ex is InvalidOperationException or JsonException)
                {
                    return Results.BadRequest(new { message = ex.Message });
                }
            });

        endpoints.MapGet(
            "/api/access/status",
            (HttpContext context, LocalAccessSessionService sessions) =>
            {
                var token = context.Request.Headers["X-PROGNODE-Session"].ToString();
                return Results.Ok(sessions.GetStatus(token));
            });

        endpoints.MapPost(
            "/api/access/login",
            (HttpContext context, LocalAccessLoginRequest request, LocalAccessSessionService sessions) =>
            {
                try
                {
                    return Results.Ok(sessions.Login(request, context.Connection.RemoteIpAddress?.ToString()));
                }
                catch (LoginThrottledException ex)
                {
                    context.Response.Headers.RetryAfter = ((int)Math.Ceiling(ex.RetryAfter.TotalSeconds)).ToString();
                    return Results.Json(new { message = ex.Message }, statusCode: StatusCodes.Status429TooManyRequests);
                }
                catch (InvalidOperationException ex)
                {
                    return Results.BadRequest(new { message = ex.Message });
                }
            });

        endpoints.MapPost(
            "/api/access/logout",
            (HttpContext context, LocalAccessSessionService sessions, QrPairingService qr) =>
            {
                var token = context.Request.Headers["X-PROGNODE-Session"].ToString();
                qr.Cancel(token);
                sessions.Logout(token);
                return Results.Ok(new { signedOut = true });
            });
    }
}
