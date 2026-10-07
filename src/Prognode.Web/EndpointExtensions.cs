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

public static class EndpointExtensions
{
    public sealed record S7TestRequest(string Host,int Port,int Rack,int Slot,int TimeoutMs=2000);
    public sealed record S7DeviceRequest(string Name,string Host,int Port,int Rack,int Slot,int PollIntervalMs);
    public sealed record MqttDeviceRequest(string? Name, string? Host, int Port, int PollIntervalMs);
    public sealed record OpcUaDeviceRequest(string? Name, string? EndpointUrl, int PollIntervalMs);
    public sealed record OpcUaCertificateRequest(string EndpointUrl);
    public sealed record OpcUaTrustRequest(string EndpointUrl, string Sha256);
    public sealed record DeleteAlarmHistoryRequest(Guid[]? OccurrenceIds);
    private static string ResolveMobileAckActor(MobileAckAuditEntry attribution, ServerAccessService server,
        LicenseService? license = null)
    {
        if(!string.IsNullOrWhiteSpace(attribution.ActorDisplayName))
            return attribution.ActorDisplayName.Trim();
        if(attribution.Principal.StartsWith("USER:",StringComparison.OrdinalIgnoreCase))
        {
            var user=attribution.Principal.Substring("USER:".Length).Trim();
            var current=license?.Current;
            if(current is not null && !string.IsNullOrWhiteSpace(current.AssignedUserName) &&
                string.Equals(current.AssignedUserEmail,user,StringComparison.OrdinalIgnoreCase))
                return current.AssignedUserName;
            return string.IsNullOrWhiteSpace(user)?"User":user;
        }
        if(attribution.DeviceId is Guid deviceId)
            return server.GetClient(deviceId)?.Name ?? $"Device {deviceId.ToString("N")[..8]}";
        return attribution.Principal;
    }
    private static IResult? CheckQrAdmin(HttpContext context, LocalAccessSessionService sessions)
    {
        var ip = context.Connection.RemoteIpAddress;
        if (ip is null || !System.Net.IPAddress.IsLoopback(ip))
            return Results.Json(new { code = "LOCAL_ADMIN_ONLY", message = "QR creation is allowed only on the Core computer." }, statusCode: 403);
        var token = context.Request.Headers["X-PROGNODE-Session"].ToString();
        var session = sessions.GetStatus(token);
        if (!session.Authenticated)
            return Results.Json(new { code = "SIGN_IN_REQUIRED", message = "Sign in on the Core computer." }, statusCode: 401);
        if (session.PortalRole is not ("OWNER" or "ORGANIZATION_ADMIN"))
            return Results.Json(new { code = "ADMIN_REQUIRED", message = "A licensed administrator must authorize QR pairing." }, statusCode: 403);
        return null;
    }

    private static PairedClientSnapshot? GetMobileBearer(HttpContext context,ServerAccessService server)
    {
        var auth=context.Request.Headers.Authorization.ToString();
        var raw=auth.StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase)
            ? auth[7..].Trim():string.Empty;
        return server.TryValidateToken(raw,out var paired) ? paired : null;
    }

    public sealed record DeviceAccessUpdateRequest(string? DisplayName,bool CanViewAlarms,
        bool CanAcknowledge,string AckAuthMode);
    public sealed record NotificationDeliveryUpdateRequest(bool Enabled);

    public static IEndpointRouteBuilder MapPrognodeEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/server/identity",
            (ServerAccessService serverAccess) =>
                Results.Ok(serverAccess.Identity));

        // Legacy URL, now restricted to an active LOCAL OWNER/ADMIN session. This route
        // does not give an unauthenticated browser or LAN visitor the pairing code.
        endpoints.MapGet("/api/client/pairing-code",
            async (HttpContext context, ServerAccessService serverAccess,
                LocalAccessSessionService sessions, LanAccessWizardService lan) =>
            {
                var denied = CheckQrAdmin(context, sessions);
                if (denied is not null) return denied;
                var tls = await lan.CheckHttpsAsync(context.RequestAborted);
                if (!tls.Ready)
                    return Results.Json(new {code=tls.DiagnosticCode, message=tls.DiagnosticMessage}, statusCode:409);
                context.Response.Headers.CacheControl = "no-store";
                return Results.Ok(new {
                    code = serverAccess.GetPairingCode(),
                    expiresInSeconds = serverAccess.PairingCodeSecondsRemaining(),
                    server = serverAccess.Identity
                });
            });

        // V0.5.8 SAS comes from the installed Core's ACTUAL HTTPS certificate, not UDP,
        // request parameters or a non-pinned remote identity response.
        endpoints.MapGet("/api/client/manual-pairing",
            async (HttpContext context, ServerAccessService serverAccess,
                LocalAccessSessionService sessions, LanAccessWizardService lan) =>
            {
                var denied = CheckQrAdmin(context, sessions);
                if (denied is not null) return denied;
                var tls = await lan.CheckHttpsAsync(context.RequestAborted);
                if (!tls.Ready)
                    return Results.Json(new {code=tls.DiagnosticCode, message=tls.DiagnosticMessage}, statusCode:409);
                var identity=serverAccess.Identity;
                var pin=identity.CertificateSha256;
                if (identity.SecureApiPort != 5443 || pin?.Length != 64)
                    return Results.Json(new {code="TLS_UNAVAILABLE"}, statusCode:409);
                var sas=ManualPairingControlCode.FromDigest(identity.ServerId, Convert.FromHexString(pin));
                context.Response.Headers.CacheControl="no-store";
                return Results.Ok(new {
                    schemaVersion=1, pairingMethod="MANUAL_SAS_V1", serverId=identity.ServerId,
                    displayName=identity.DisplayName, httpsPort=5443, pairingCode=serverAccess.GetPairingCode(),
                    expiresInSeconds=serverAccess.PairingCodeSecondsRemaining(), certificateCheckCode=sas,
                    requiresIndependentVisualConfirmation=true
                });
            });
        endpoints.MapGet("/api/client/manual-pairing/certificate",
            async (HttpContext context, ServerAccessService serverAccess,
                LocalAccessSessionService sessions, LanAccessWizardService lan) =>
            {
                var denied=CheckQrAdmin(context,sessions);
                if (denied is not null) return denied;
                var tls=await lan.CheckHttpsAsync(context.RequestAborted);
                if (!tls.Ready) return Results.Json(new {code=tls.DiagnosticCode},statusCode:409);
                context.Response.Headers.CacheControl="no-store";
                return Results.Ok(new {serverId=serverAccess.Identity.ServerId,
                    certificateSha256=serverAccess.Identity.CertificateSha256,httpsPort=5443});
            });

        endpoints.MapPost("/api/client/manual-pairing/cancel",
            (HttpContext context, ServerAccessService serverAccess, LocalAccessSessionService sessions) =>
            {
                var denied=CheckQrAdmin(context,sessions);if(denied is not null)return denied;
                serverAccess.CancelManualPairingCode();
                context.Response.Headers.CacheControl="no-store";
                return Results.Ok(new {cancelled=true});
            });

        endpoints.MapPost(
            "/api/client/pair",
            (HttpContext context, PairClientRequest request, ServerAccessService serverAccess) =>
            {
                try
                {
                    if (request.ServerId != serverAccess.Identity.ServerId)
                        return Results.BadRequest(new { message = "Server identity mismatch." });

                    return Results.Ok(serverAccess.Pair(
                        request.ClientName,
                        request.PairingCode,
                        request.Platform,
                        request.DevicePublicKey,
                        context.Connection.RemoteIpAddress?.ToString()));
                }
                catch (ManualPairingException ex)
                {
                    return Results.Json(new {code=ex.Code,message=ex.Message},statusCode:ex.StatusCode);
                }
                catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
                {
                    return Results.BadRequest(new { message = ex.Message });
                }
            });

        // LAN Access wizard: HTTP can PREVIEW a change; only a physically confirmed
        // Windows Agent action may start an elevated, parameter-limited setup helper.
        endpoints.MapGet("/api/mobile-access/network-status",
            async (HttpContext ctx, LocalAccessSessionService sessions, LanAccessWizardService wizard) =>
            {
                var rejected = CheckQrAdmin(ctx, sessions);
                ctx.Response.Headers.CacheControl = "no-store";
                return rejected ?? Results.Ok(await wizard.StatusAsync(ctx.RequestAborted));
            });
        endpoints.MapPost("/api/mobile-access/prepare",
            (HttpContext ctx, LanAccessPrepareRequest request, LocalAccessSessionService sessions,
                LanAccessWizardService wizard) =>
            {
                var rejected = CheckQrAdmin(ctx, sessions);
                if (rejected is not null) return rejected;
                try
                {
                    ctx.Response.Headers.CacheControl = "no-store";
                    return Results.Ok(wizard.Prepare(request,
                        ctx.Request.Headers["X-PROGNODE-Session"].ToString()));
                }
                catch (LanAccessError e)
                {
                    return Results.Json(new { code=e.Code, message=e.Message }, statusCode:409);
                }
            });
        endpoints.MapPost("/api/mobile-access/verify",
            async (HttpContext ctx, LocalAccessSessionService sessions, LanAccessWizardService wizard) =>
            {
                var rejected=CheckQrAdmin(ctx,sessions);
                ctx.Response.Headers.CacheControl = "no-store";
                return rejected ?? Results.Ok(await wizard.StatusAsync(ctx.RequestAborted));
            });
        endpoints.MapPost("/api/mobile-access/phone-probe",
            async (HttpContext ctx, LanProbeRequest request, LocalAccessSessionService sessions,
                LanAccessWizardService wizard) =>
            {
                var rejected=CheckQrAdmin(ctx,sessions);
                if(rejected is not null)return rejected;
                try {return Results.Ok(await wizard.CreateProbeAsync(request.InterfaceIndex, ctx.RequestAborted));}
                catch(LanAccessError e){return Results.Json(new{code=e.Code,message=e.Message},statusCode:409);}
            });
        endpoints.MapGet("/api/mobile-access/phone-probe/{nonce}",
            (HttpContext ctx, string nonce, LanAccessWizardService wizard) =>
                wizard.RecordProbe(nonce,ctx.Connection.RemoteIpAddress,ctx.Connection.LocalIpAddress,ctx.Request.IsHttps,ctx.Connection.LocalPort)
                    ? Results.Ok(new { ok=true, message="PROGNODE mobile LAN connection verified. Return to the Core setup screen." })
                    : Results.Json(new { code="INVALID_OR_EXPIRED_PROBE" },statusCode:404));
        // Local Agent can only read/claim an already prepared 3-minute request.
        // It must display an OS-native confirmation and seek UAC elevation itself.
        endpoints.MapGet("/api/mobile-access/agent/pending",
            (HttpContext ctx, LanAccessWizardService wizard) =>
            {
                ctx.Response.Headers.CacheControl = "no-store";
                if (ctx.Connection.RemoteIpAddress is not { } ip || !System.Net.IPAddress.IsLoopback(ip))
                    return (IResult)Results.StatusCode(403);
                var pending = wizard.AgentPending();
                // Never return Results.Ok(null): some ASP.NET Core versions emit HTTP 200
                // with an empty body, which crashes older Agents' GetFromJsonAsync.
                if (pending is null)
                    return (IResult)Results.Json(new {
                        code = "NO_PENDING_LAN_REQUEST",
                        message = "Prepare LAN access on the local Core and confirm it in the Windows tray within three minutes."
                    }, statusCode: 404);
                return (IResult)Results.Ok(pending);
            });
        endpoints.MapPost("/api/mobile-access/agent/claim",
            (HttpContext ctx, LanAgentClaimRequest request, LanAccessWizardService wizard,
                LocalAccessSessionService sessions) =>
            {
                if(ctx.Connection.RemoteIpAddress is not { } ip || !System.Net.IPAddress.IsLoopback(ip))
                    return Results.StatusCode(403);
                try { return Results.Ok(wizard.Claim(request.RequestId,sessions.Validate)); }
                catch(LanAccessError e){return Results.Json(new {code=e.Code,message=e.Message},statusCode:409);}
            });

        // QR admin APIs intentionally stay loopback-only and require the signed-in
        // licensed OWNER/ORGANIZATION_ADMIN. They must never be added to public pairing routes.
        endpoints.MapGet(
            "/api/client/pairing-qr/network-options",
            (HttpContext ctx, LocalAccessSessionService sessions, QrPairingService qr) =>
            {
                var rejected = CheckQrAdmin(ctx, sessions);
                return rejected ?? Results.Ok(new { addresses = qr.GetNetworkOptions() });
            });

        endpoints.MapPost(
            "/api/client/pairing-qr",
            async (HttpContext ctx, QrCreateRequest request, LocalAccessSessionService sessions, QrPairingService qr,
                LanAccessWizardService wizard, LicenseService license, ILogger<QrPairingService> logger) =>
            {
                var rejected = CheckQrAdmin(ctx, sessions);
                if (rejected is not null) return rejected;
                if (!license.HasModule("ALARM") || !license.IsLifecycleOperational)
                    return Results.Json(new { code = "ALARM_LICENSE_REQUIRED", message = "An operational Alarm license is required for LAN pairing." }, statusCode: 403);
                var tls = await wizard.CheckHttpsAsync(ctx.RequestAborted);
                if (!tls.Ready)
                    return Results.Json(new { code = tls.DiagnosticCode, message = tls.DiagnosticMessage ?? "Pinned HTTPS health check failed." }, statusCode: 409);
                try
                {
                    ctx.Response.Headers.CacheControl = "no-store";
                    var result = qr.Create(ctx.Request.Headers["X-PROGNODE-Session"].ToString(), request.SelectedHost);
                    logger.LogInformation("LAN pairing QR issued for selected local network adapter.");
                    return Results.Ok(result);
                }
                catch (QrPairingProblem problem)
                {
                    return Results.Json(new { code = problem.Code, message = problem.Message }, statusCode: problem.HttpStatus);
                }
            });

        endpoints.MapGet(
            "/api/client/pairing-qr/status",
            (HttpContext ctx, LocalAccessSessionService sessions, QrPairingService qr) =>
            {
                var rejected = CheckQrAdmin(ctx, sessions);
                ctx.Response.Headers.CacheControl = "no-store";
                return rejected ?? Results.Ok(qr.Status(ctx.Request.Headers["X-PROGNODE-Session"].ToString()));
            });

        endpoints.MapDelete(
            "/api/client/pairing-qr",
            (HttpContext ctx, LocalAccessSessionService sessions, QrPairingService qr) =>
            {
                var rejected = CheckQrAdmin(ctx, sessions);
                if (rejected is not null) return rejected;
                qr.Cancel(ctx.Request.Headers["X-PROGNODE-Session"].ToString());
                return Results.Ok(new { status = "REVOKED" });
            });

        // Public only on the *secure* LAN listener; no secret can be sent over HTTP.
        // A successful ticket creates exactly one normal paired client/token.
        endpoints.MapPost(
            "/api/client/pair-qr",
            (HttpContext ctx, QrPairClientRequest request, QrPairingService qr,
                ServerAccessService serverAccess, LocalAccessSessionService sessions,
                LicenseService license, ILogger<QrPairingService> logger) =>
            {
                if (serverAccess.Identity.SecureApiPort is null)
                    return Results.Json(new { code = "HTTPS_NOT_READY", message = "LAN HTTPS is not configured on this Core." }, statusCode: 409);
                if (!ctx.Request.IsHttps ||
                    ctx.Connection.LocalPort != serverAccess.Identity.SecureApiPort)
                    return Results.Json(new { code = "HTTPS_REQUIRED", message = "Use the Core LAN HTTPS endpoint in the scanned QR." }, statusCode: 426);
                if (!license.HasModule("ALARM") || !license.IsLifecycleOperational)
                    return Results.Json(new { code = "ALARM_LICENSE_REQUIRED", message = "LAN pairing requires an operational Alarm license." }, statusCode: 403);
                try
                {
                    var result = qr.Pair(request.ServerId, request.PairingTicket, request.ClientName,
                        request.Platform, request.DevicePublicKey, ctx.Connection.RemoteIpAddress, sessions.Validate);
                    ctx.Response.Headers.CacheControl = "no-store";
                    logger.LogInformation("LAN QR pairing completed for client {ClientId}", result.ClientId);
                    return Results.Ok(result);
                }
                catch (QrPairingProblem problem)
                {
                    logger.LogWarning("LAN QR pairing rejected with {Code} for {RemoteIp}", problem.Code, ctx.Connection.RemoteIpAddress);
                    return Results.Json(new { code = problem.Code, message = problem.Message }, statusCode: problem.HttpStatus);
                }
                catch (ArgumentException)
                {
                    logger.LogWarning("LAN QR pairing rejected: invalid device metadata.");
                    return Results.BadRequest(new { code = "INVALID_CLIENT", message = "Device name or public key is invalid." });
                }
            });

        endpoints.MapGet(
            "/api/client/paired",
            (HttpContext context, ServerAccessService serverAccess) =>
            {
                if (TryGetPairedClientId(context, out var callerClientId))
                {
                    var own = serverAccess.GetClient(callerClientId);
                    return own is null
                        ? Results.Ok(Array.Empty<PairedClientSnapshot>())
                        : Results.Ok(new[] { own });
                }

                return Results.Ok(serverAccess.GetClients());
            });

        endpoints.MapDelete(
            "/api/client/paired/{clientId:guid}",
            (HttpContext context, Guid clientId, ServerAccessService serverAccess) =>
            {
                if (TryGetPairedClientId(context, out var callerClientId) && callerClientId != clientId)
                    return Results.StatusCode(StatusCodes.Status403Forbidden);

                try
                {
                    return serverAccess.RevokeClient(clientId) ? Results.NoContent() : Results.NotFound();
                }
                catch (InvalidOperationException ex)
                {
                    return Results.Conflict(new { message = ex.Message });
                }
            });

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

        // REMOTE ACCESS
        // LAN pairing is not metered by the Remote Access add-on and consumes no seat. Only explicit remote registration
        // reaches PROGNODE Cloud and consumes one server-bound Remote Access client seat.
        endpoints.MapGet(
            "/api/remote-access/status",
            (RemoteAccessService remoteAccess) =>
                Results.Ok(remoteAccess.GetStatus()));

        endpoints.MapGet(
            "/api/remote-access/clients",
            (HttpContext context, RemoteAccessService remoteAccess) =>
            {
                var clients = remoteAccess.GetClients();
                if (TryGetPairedClientId(context, out var callerClientId))
                    return Results.Ok(clients.Where(x => x.LocalClientId == callerClientId));
                return Results.Ok(clients);
            });

        endpoints.MapGet(
            "/api/remote-access/ack-audit",
            (HttpContext context, int? limit, RemoteAckAuditStore audit) =>
            {
                if (!IsLoopbackRequest(context))
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                return Results.Ok(audit.GetRecent(limit ?? 100));
            });

        endpoints.MapPost(
            "/api/remote-access/bind",
            async (HttpContext context, RemoteAccessService remoteAccess, CancellationToken ct) =>
            {
                if (!IsLoopbackRequest(context))
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                return RemoteOperationResult(await remoteAccess.BindServerAsync(ct));
            });

        endpoints.MapPost(
            "/api/remote-access/sync",
            async (HttpContext context, RemoteAccessService remoteAccess, CancellationToken ct) =>
            {
                if (!IsLoopbackRequest(context))
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                return RemoteOperationResult(await remoteAccess.SyncAsync(ct));
            });

        endpoints.MapPost(
            "/api/remote-access/clients/register",
            async (
                HttpContext context,
                RemoteClientRegistrationRequest request,
                RemoteAccessService remoteAccess,
                CancellationToken ct) =>
            {
                Guid? localClientId = null;
                if (context.Items.TryGetValue("PROGNODE_CLIENT_ID", out var paired) && paired is Guid pairedId)
                    localClientId = pairedId;
                else if (request.ClientId is Guid requestedId)
                    localClientId = requestedId;

                if (localClientId is null)
                    return Results.BadRequest(new { message = "A paired client is required before Remote Access can be enabled." });

                return RemoteOperationResult(await remoteAccess.RegisterClientAsync(
                    localClientId.Value,
                    request.DevicePublicKey,
                    request.Platform,
                    ct));
            });

        endpoints.MapDelete(
            "/api/remote-access/clients/{clientId:guid}",
            async (HttpContext context, Guid clientId, RemoteAccessService remoteAccess, CancellationToken ct) =>
            {
                if (TryGetPairedClientId(context, out var callerClientId) && callerClientId != clientId)
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                return RemoteOperationResult(await remoteAccess.RevokeClientAsync(clientId, ct));
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
            (LocalAccessLoginRequest request, LocalAccessSessionService sessions) =>
            {
                try
                {
                    return Results.Ok(sessions.Login(request));
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

        endpoints.MapGet(
            "/api/protocols",
            (ProtocolCatalog catalog) =>
                Results.Ok(catalog.GetAll()));

        endpoints.MapGet(
            "/api/devices",
            async (
                DeviceService devices,
                CancellationToken ct) =>
                Results.Ok(
                    await devices.GetAllAsync(ct)));

        endpoints.MapPost(
            "/api/network/ping",
            async (
                PingHostRequest request,
                NetworkPingService ping,
                CancellationToken ct) =>
            {
                try
                {
                    var timeoutMs =
                        request.TimeoutMs <= 0
                            ? 1200
                            : Math.Clamp(
                                request.TimeoutMs,
                                250,
                                10000);

                    return Results.Ok(
                        await ping.PingAsync(
                            request.Host,
                            timeoutMs,
                            ct));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(
                        new { message = ex.Message });
                }
            });

        endpoints.MapPost(
            "/api/devices/{id:guid}/ping",
            async (
                Guid id,
                DeviceService devices,
                NetworkPingService ping,
                CancellationToken ct) =>
            {
                var device =
                    await devices.GetByIdAsync(
                        id,
                        ct);

                if (device is null)
                    return Results.NotFound();

                if (string.IsNullOrWhiteSpace(
                    device.Host))
                {
                    return Results.BadRequest(
                        new
                        {
                            message =
                                "This device has no Host / IP."
                        });
                }

                return Results.Ok(
                    await ping.PingAsync(
                        device.Host,
                        1200,
                        ct));
            });

        endpoints.MapPost(
            "/api/devices/mock",
            async (
                CreateMockDeviceRequest request,
                DeviceService devices,
                CancellationToken ct) =>
            {
                try
                {
                    var created =
                        await devices.CreateMockAsync(
                            request.Name,
                            ct);

                    return Results.Created(
                        $"/api/devices/{created.Id}",
                        created);
                }
                catch (DeviceCapacityExceededException)
                {
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(
                        new { message = ex.Message });
                }
            });

        endpoints.MapPost(
            "/api/devices/modbus-tcp/test",
            async (
                TestModbusTcpRequest request,
                ModbusTcpProbe probe,
                CancellationToken ct) =>
            {
                var host =
                    (request.Host ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(host))
                    return Results.BadRequest(
                        new { message = "Host / IP is required." });

                if (request.Port is < 1 or > 65535)
                    return Results.BadRequest(
                        new { message = "Port must be between 1 and 65535." });

                if (request.UnitId is < 0 or > 255)
                    return Results.BadRequest(
                        new { message = "Unit ID must be between 0 and 255." });

                var timeoutMs =
                    request.TimeoutMs <= 0
                        ? 2000
                        : Math.Clamp(
                            request.TimeoutMs,
                            250,
                            30000);

                return Results.Ok(
                    await probe.TestAsync(
                        host,
                        request.Port,
                        request.UnitId,
                        timeoutMs,
                        ct));
            });

        endpoints.MapPost(
            "/api/devices/modbus-tcp",
            async (
                CreateModbusTcpDeviceRequest request,
                DeviceService devices,
                CancellationToken ct) =>
            {
                try
                {
                    var created =
                        await devices.CreateModbusTcpAsync(
                            request.Name,
                            request.Host,
                            request.Port,
                            request.UnitId,
                            request.PollIntervalMs,
                            ct);

                    return Results.Created(
                        $"/api/devices/{created.Id}",
                        created);
                }
                catch (DeviceCapacityExceededException)
                {
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(
                        new { message = ex.Message });
                }
            });

        endpoints.MapPost("/api/devices/siemens-s7-tcp/test",async (S7TestRequest req,CancellationToken ct)=>
        {
            if (string.IsNullOrWhiteSpace(req.Host)||req.Port is < 1 or > 65535||req.Rack is < 0 or > 7||req.Slot is < 0 or > 31)
                return Results.BadRequest(new {message="Check host/port/rack/slot."});
            var sw=System.Diagnostics.Stopwatch.StartNew();
            try {await using var plc=new S7Client();await plc.ConnectAsync(req.Host,req.Port,req.Rack,req.Slot,Math.Clamp(req.TimeoutMs,250,30000),ct);
                // Protocol handshake only. Does not claim PLC DB read permission.
                return Results.Ok(new {success=true,message="S7 COTP + SetupCommunication OK. Add a DB tag to test reading.",responseTimeMs=sw.ElapsedMilliseconds});}
            catch(Exception e) when(e is not OperationCanceledException || !ct.IsCancellationRequested)
            {return Results.Ok(new {success=false,message=e.Message,responseTimeMs=sw.ElapsedMilliseconds});}
        });
        endpoints.MapPost("/api/devices/siemens-s7-tcp",async(S7DeviceRequest req,DeviceService devices,CancellationToken ct)=>
        {
            try {var item=await devices.CreateS7TcpAsync(req.Name,req.Host,req.Port,req.Rack,req.Slot,req.PollIntervalMs,ct);
                return Results.Created($"/api/devices/{item.Id}",item);}
            catch(DeviceCapacityExceededException){return Results.StatusCode(403);}
            catch(ArgumentException e){return Results.BadRequest(new {message=e.Message});}
        });

        endpoints.MapPost("/api/devices/mqtt", async (MqttDeviceRequest req, DeviceService devices, CancellationToken ct) =>
        {
            try
            {
                var item = await devices.CreateMqttAsync(req.Name, req.Host, req.Port, req.PollIntervalMs, ct);
                return Results.Created($"/api/devices/{item.Id}", item);
            }
            catch (DeviceCapacityExceededException) { return Results.StatusCode(403); }
            catch (ArgumentException ex) { return Results.BadRequest(new { message = ex.Message }); }
        });

        endpoints.MapPost("/api/devices/opc-ua", async (OpcUaDeviceRequest req, DeviceService devices, CancellationToken ct) =>
        {
            try
            {
                var item = await devices.CreateOpcUaAsync(req.Name, req.EndpointUrl, req.PollIntervalMs, ct);
                return Results.Created($"/api/devices/{item.Id}", item);
            }
            catch (DeviceCapacityExceededException) { return Results.StatusCode(403); }
            catch (ArgumentException ex) { return Results.BadRequest(new { message = ex.Message }); }
        });

        endpoints.MapPost("/api/devices/opc-ua/certificate/inspect",
            async (OpcUaCertificateRequest request, IOpcUaCertificateApproval certificates,
                HttpContext context, LocalAccessSessionService sessions, CancellationToken ct) =>
            {
                var denied = CheckQrAdmin(context, sessions);
                if (denied is not null) return denied;
                context.Response.Headers.CacheControl = "no-store";
                try { return Results.Ok(await certificates.InspectAsync(request.EndpointUrl, ct)); }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                { return Results.Json(new { message = ex.Message }, statusCode: 409); }
            });

        endpoints.MapPost("/api/devices/opc-ua/certificate/trust",
            async (OpcUaTrustRequest request, IOpcUaCertificateApproval certificates,
                HttpContext context, LocalAccessSessionService sessions, CancellationToken ct) =>
            {
                var denied = CheckQrAdmin(context, sessions);
                if (denied is not null) return denied;
                context.Response.Headers.CacheControl = "no-store";
                try { return Results.Ok(await certificates.TrustAsync(request.EndpointUrl, request.Sha256, ct)); }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                { return Results.Json(new { message = ex.Message }, statusCode: 409); }
            });

        endpoints.MapPut(
            "/api/devices/{id:guid}",
            async (
                Guid id,
                UpdateDeviceRequest request,
                DeviceService devices,
                CancellationToken ct) =>
            {
                try
                {
                    return Results.Ok(
                        await devices.UpdateAsync(
                            id,
                            request.Name,
                            request.Host,
                            request.Port,
                            request.UnitId,
                            request.PollIntervalMs,
                            ct));
                }
                catch (KeyNotFoundException)
                {
                    return Results.NotFound();
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(
                        new { message = ex.Message });
                }
            });

        endpoints.MapDelete(
            "/api/devices/{id:guid}",
            async (
                Guid id,
                DeviceService devices,
                CancellationToken ct) =>
                await devices.DeleteAsync(id, ct)
                    ? Results.NoContent()
                    : Results.NotFound());

        endpoints.MapGet(
            "/api/tags",
            async (
                TagService tags,
                CancellationToken ct) =>
                Results.Ok(
                    await tags.GetAllAsync(ct)));

        endpoints.MapGet(
            "/api/tags/values",
            (CurrentTagValueStore values) =>
                Results.Ok(values.GetAll()));

        endpoints.MapPost(
            "/api/tags",
            async (
                CreateOrUpdateTagRequest request,
                TagService tags,
                CancellationToken ct) =>
            {
                try
                {
                    var created =
                        await tags.CreateAsync(
                            request.DeviceId,
                            request.Name,
                            request.Address,
                            request.DataType,
                            request.BitIndex,
                            request.ByteOrder,
                            request.Unit,
                            request.Offset,
                            request.DecimalPlaces,
                            ct);

                    return Results.Created(
                        $"/api/tags/{created.Id}",
                        created);
                }
                catch (TagCapacityExceededException ex)
                {
                    return Results.Json(new
                    {
                        message = ex.Message,
                        code = "TAG_LIMIT_REACHED",
                        usedTags = ex.UsedTags,
                        maxTags = ex.MaxTags
                    }, statusCode: StatusCodes.Status403Forbidden);
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(
                        new { message = ex.Message });
                }
            });

        endpoints.MapPut(
            "/api/tags/{id:guid}",
            async (
                Guid id,
                CreateOrUpdateTagRequest request,
                TagService tags,
                CancellationToken ct) =>
            {
                try
                {
                    return Results.Ok(
                        await tags.UpdateAsync(
                            id,
                            request.DeviceId,
                            request.Name,
                            request.Address,
                            request.DataType,
                            request.BitIndex,
                            request.ByteOrder,
                            request.Unit,
                            request.Offset,
                            request.DecimalPlaces,
                            ct));
                }
                catch (KeyNotFoundException)
                {
                    return Results.NotFound();
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(
                        new { message = ex.Message });
                }
            });

        endpoints.MapDelete(
            "/api/tags/{id:guid}",
            async (
                Guid id,
                TagService tags,
                CancellationToken ct) =>
                await tags.DeleteAsync(id, ct)
                    ? Results.NoContent()
                    : Results.NotFound());

        // BATCH / LOT - minimum production runtime contract.
        endpoints.MapPost(
            "/api/batches/start",
            async (
                StartBatchRequest request,
                BatchService batches,
                AlarmBatchLinkService alarmBatchLinks,
                CancellationToken ct) =>
            {
                try
                {
                    var started = await batches.StartAsync(
                        request.BatchNo,
                        request.RecipeName,
                        request.Operator,
                        request.Note,
                        ct);

                    // An alarm may already be ACTIVE when a new Lot starts. Persist that
                    // overlap too, so Batch reports are correct even when activation
                    // happened before StartedAt.
                    await alarmBatchLinks.LinkCurrentActiveAsync(started.Id, ct);
                    return Results.Ok(started);
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { message = ex.Message });
                }
                catch (InvalidOperationException ex)
                {
                    return Results.Conflict(new { message = ex.Message });
                }
            });

        endpoints.MapGet(
            "/api/batches/current",
            async (BatchService batches, CancellationToken ct) =>
                Results.Ok(await batches.GetCurrentAsync(ct)));

        endpoints.MapGet(
            "/api/batches",
            async (int? limit, BatchService batches, CancellationToken ct) =>
                Results.Ok(await batches.GetRecentAsync(limit ?? 50, ct)));

        endpoints.MapGet(
            "/api/batches/{id:guid}",
            async (Guid id, BatchService batches, CancellationToken ct) =>
            {
                var item = await batches.GetByIdAsync(id, ct);
                if (item is null)
                    return Results.NotFound();
                return Results.Ok(item);
            });

        endpoints.MapPost(
            "/api/batches/{id:guid}/complete",
            async (Guid id, EndBatchRequest request, BatchService batches, CancellationToken ct) =>
            {
                try
                {
                    return Results.Ok(await batches.CompleteAsync(id, request.Note, ct));
                }
                catch (KeyNotFoundException)
                {
                    return Results.NotFound();
                }
                catch (InvalidOperationException ex)
                {
                    return Results.Conflict(new { message = ex.Message });
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { message = ex.Message });
                }
            });

        endpoints.MapPost(
            "/api/batches/{id:guid}/abort",
            async (Guid id, EndBatchRequest request, BatchService batches, CancellationToken ct) =>
            {
                try
                {
                    return Results.Ok(await batches.AbortAsync(id, request.Note, ct));
                }
                catch (KeyNotFoundException)
                {
                    return Results.NotFound();
                }
                catch (InvalidOperationException ex)
                {
                    return Results.Conflict(new { message = ex.Message });
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { message = ex.Message });
                }
            });

        endpoints.MapGet(
            "/api/batches/{id:guid}/historian",
            async (
                Guid id,
                string? tagIds,
                int? maxRows,
                BatchService batches,
                HistorianService historian,
                CancellationToken ct) =>
            {
                if (await batches.GetByIdAsync(id, ct) is null)
                    return Results.NotFound();

                var parsedTagIds = new List<Guid>();
                if (!string.IsNullOrWhiteSpace(tagIds))
                {
                    foreach (var raw in tagIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                    {
                        if (!Guid.TryParse(raw, out var tagId))
                            return Results.BadRequest(new { message = $"Invalid Tag ID '{raw}'." });
                        parsedTagIds.Add(tagId);
                    }
                }

                return Results.Ok(await historian.QueryByBatchAsync(
                    id,
                    parsedTagIds,
                    maxRows ?? 250_000,
                    ct));
            });

        endpoints.MapGet(
            "/api/batches/{id:guid}/alarms",
            async (
                Guid id,
                int? limit,
                BatchService batches,
                AlarmService alarms,
                CancellationToken ct) =>
            {
                if (await batches.GetByIdAsync(id, ct) is null)
                    return Results.NotFound();

                return Results.Ok(await alarms.GetHistoryByBatchAsync(id, limit ?? 200, ct));
            });

        // ALARMS
        endpoints.MapGet(
            "/api/alarms/definitions",
            async (
                AlarmService alarms,
                CancellationToken ct) =>
                Results.Ok(
                    await alarms.GetDefinitionsAsync(ct)));

        endpoints.MapGet(
            "/api/alarms/active",
            (AlarmService alarms, LicenseService license, MobileAckAuditStore ackAudit,
                ServerAccessService server) =>
            {
                if (!license.HasModule("ALARM"))
                    return Results.Ok(Array.Empty<object>());
                var active=alarms.GetActive();
                var acknowledgements=ackAudit.GetAcknowledgements(server.Identity.ServerId,
                    active.Select(x=>x.OccurrenceId));
                return Results.Ok(active.Select(alarm =>
                    acknowledgements.TryGetValue(alarm.OccurrenceId,out var attribution)
                        ? alarm with { AcknowledgedAt=attribution.TimestampUtc,
                            AcknowledgedBy=ResolveMobileAckActor(attribution,server,license) }
                        : alarm).ToArray());
            });

        endpoints.MapGet(
            "/api/alarms/history",
            async (
                int? limit,
                AlarmService alarms,
                CancellationToken ct) =>
                Results.Ok(
                    await alarms.GetHistoryAsync(
                        limit ?? 100,
                        ct)));

        endpoints.MapGet("/api/alarms/history/count",
            async (AlarmService alarms, CancellationToken ct) =>
                Results.Ok(new { count = await alarms.CountHistoryAsync(ct) }));

        endpoints.MapGet("/api/alarms/history/page",
            async (int? offset, int? limit, string? search, string? priority,
                AlarmService alarms, MobileAckAuditStore ackAudit, ServerAccessService server,
                LicenseService license,
                CancellationToken ct) =>
            {
                var page=await alarms.GetHistoryPageAsync(
                    Math.Max(0, offset ?? 0), Math.Clamp(limit ?? 25, 1, 100),
                    search, priority, ct);
                var acknowledgements=ackAudit.GetAcknowledgements(server.Identity.ServerId,
                    page.Select(x=>x.OccurrenceId));
                var items=page.Select(alarm =>
                    acknowledgements.TryGetValue(alarm.OccurrenceId,out var attribution)
                        ? alarm with { AcknowledgedAt=attribution.TimestampUtc,
                            AcknowledgedBy=ResolveMobileAckActor(attribution,server,license) }
                        : alarm).ToArray();
                return Results.Ok(new {
                    items,
                    total = await alarms.CountMatchingHistoryAsync(search, priority, ct)
                });
            });

        endpoints.MapPost("/api/alarms/history/delete-selected",
            async (DeleteAlarmHistoryRequest request, AlarmService alarms, CancellationToken ct) =>
            {
                var ids = request.OccurrenceIds?.Where(x => x != Guid.Empty).Distinct().Take(100).ToArray()
                    ?? Array.Empty<Guid>();
                if (ids.Length == 0) return Results.BadRequest(new { message = "Select alarm history records first." });
                return Results.Ok(new { deleted = await alarms.DeleteHistoryOccurrencesAsync(ids, ct) });
            });

        endpoints.MapPost(
            "/api/alarms/definitions",
            async (
                CreateOrUpdateAlarmRequest request,
                AlarmService alarms,
                LicenseService license,
                CancellationToken ct) =>
            {
                if (!license.HasModule("ALARM"))
                {
                    return Results.Json(new { message = "Alarm Monitoring is not included in this PROGNODE license." },
                        statusCode: StatusCodes.Status403Forbidden);
                }

                try
                {
                    var created =
                        await alarms.CreateAsync(
                            request.TagId,
                            request.Text,
                            request.Priority,
                            request.BitIndex,
                            request.TriggerValue,
                            request.Condition,
                            request.Threshold,
                            request.Deadband,
                            request.DelayOnMs,
                            request.DelayOffMs,
                            request.NotifyOnActive,
                            request.NotifyOnCleared,
                            request.NotificationMode,
                            request.RepeatIntervalSeconds,
                            request.ContinueAfterClearUntilAcknowledged,
                            ct,
                            request.RequiresAcknowledgement ??
                                AlarmDefaults.RequiresAcknowledgement);

                    return Results.Created(
                        $"/api/alarms/definitions/{created.Id}",
                        created);
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(
                        new { message = ex.Message });
                }
            });

        endpoints.MapPut(
            "/api/alarms/definitions/{id:guid}",
            async (
                Guid id,
                CreateOrUpdateAlarmRequest request,
                AlarmService alarms,
                LicenseService license,
                CancellationToken ct) =>
            {
                if (!license.HasModule("ALARM"))
                    return Results.Json(new { message = "Alarm Monitoring is not included in this PROGNODE license." },
                        statusCode: StatusCodes.Status403Forbidden);

                try
                {
                    return Results.Ok(
                        await alarms.UpdateAsync(
                            id,
                            request.TagId,
                            request.Text,
                            request.Priority,
                            request.BitIndex,
                            request.TriggerValue,
                            request.Condition,
                            request.Threshold,
                            request.Deadband,
                            request.DelayOnMs,
                            request.DelayOffMs,
                            request.NotifyOnActive,
                            request.NotifyOnCleared,
                            request.NotificationMode,
                            request.RepeatIntervalSeconds,
                            request.ContinueAfterClearUntilAcknowledged,
                            ct,
                            request.RequiresAcknowledgement ??
                                AlarmDefaults.RequiresAcknowledgement));
                }
                catch (KeyNotFoundException)
                {
                    return Results.NotFound();
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(
                        new { message = ex.Message });
                }
            });

        endpoints.MapDelete(
            "/api/alarms/definitions/{id:guid}",
            async (
                Guid id,
                AlarmService alarms,
                CancellationToken ct) =>
                await alarms.DeleteAsync(id, ct)
                    ? Results.NoContent()
                    : Results.NotFound());

        endpoints.MapPost(
            "/api/alarms/ack",
            async (
                AcknowledgeAlarmRequest request,
                AlarmService alarms,
                CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(
                    request.AlarmKey))
                {
                    return Results.BadRequest(
                        new
                        {
                            message =
                                "Alarm key is required."
                        });
                }

                return await alarms.AcknowledgeAsync(
                    request.AlarmKey,
                    ct)
                    ? Results.Ok(
                        new { acknowledged = true })
                    : Results.NotFound();
            });

        // RC6.4 Trend Studio: unsaved exploratory views through the normal Core API.
        // Read-only and bounded: never writes to PLCs, tags, historian or saved views.
        endpoints.MapGet(
            "/api/trend-studio/series",
            async (
                string? tagIds,
                string? from,
                string? to,
                int? maxPoints,
                TagService tags,
                HistorianService historian,
                CancellationToken ct) =>
            {
                if (string.IsNullOrWhiteSpace(tagIds))
                    return Results.BadRequest(new { message = "Choose at least one Tag." });

                var ids = new List<Guid>();
                foreach (var raw in tagIds.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (!Guid.TryParse(raw, out var parsedId) || parsedId == Guid.Empty)
                        return Results.BadRequest(new { message = "Tag IDs must be valid GUIDs." });
                    if (!ids.Contains(parsedId)) ids.Add(parsedId);
                }

                if (ids.Count is < 1 or > 8)
                    return Results.BadRequest(new { message = "Select between 1 and 8 Tags." });

                var knownTags = (await tags.GetAllAsync(ct)).Select(tag => tag.Id).ToHashSet();
                if (ids.Any(id => !knownTags.Contains(id)))
                    return Results.BadRequest(new { message = "One or more Tags no longer exist." });

                // Trend Studio only queries explicitly enrolled Historian Tags.
                // Live Tag values are still available on Tags/Devices pages.
                var recordedIds = await historian.GetRecordingTagIdsAsync(ct);
                if (ids.Any(id => !recordedIds.Contains(id)))
                    return Results.BadRequest(new { message = "Add each selected Tag to Historian before using Trend Studio." });

                var end = TryParseDate(to) ?? DateTimeOffset.UtcNow;
                var start = TryParseDate(from) ?? end.AddHours(-1);
                if (end <= start || end - start > TimeSpan.FromDays(366))
                    return Results.BadRequest(new { message = "Choose a valid time range up to 366 days." });

                var series = new List<object>(ids.Count);
                foreach (var tagId in ids)
                    series.Add(await historian.QuerySeriesAsync(
                        tagId, start, end, Math.Clamp(maxPoints ?? 1800, 100, 5000), ct));

                return Results.Ok(new
                {
                    trend = new { name = "Ad-hoc analysis", tagIds = ids, colors = Array.Empty<string>() },
                    from = start,
                    to = end,
                    series
                });
            });

        // TRENDS + HISTORIAN
        endpoints.MapGet(
            "/api/trends",
            async (
                TrendService trends,
                CancellationToken ct) =>
                Results.Ok(
                    await trends.GetAllAsync(ct)));

        endpoints.MapPost(
            "/api/trends",
            async (
                CreateOrUpdateTrendRequest request,
                TrendService trends,
                CancellationToken ct) =>
            {
                try
                {
                    var created =
                        await trends.CreateAsync(
                            request.Name,
                            request.TagIds,
                            request.Colors,
                            request.DefaultWindowMinutes,
                            ct);

                    return Results.Created(
                        $"/api/trends/{created.Id}",
                        created);
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(
                        new { message = ex.Message });
                }
            });

        endpoints.MapPut(
            "/api/trends/{id:guid}",
            async (
                Guid id,
                CreateOrUpdateTrendRequest request,
                TrendService trends,
                CancellationToken ct) =>
            {
                try
                {
                    return Results.Ok(
                        await trends.UpdateAsync(
                            id,
                            request.Name,
                            request.TagIds,
                            request.Colors,
                            request.DefaultWindowMinutes,
                            ct));
                }
                catch (KeyNotFoundException)
                {
                    return Results.NotFound();
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(
                        new { message = ex.Message });
                }
            });

        endpoints.MapDelete(
            "/api/trends/{id:guid}",
            async (
                Guid id,
                TrendService trends,
                CancellationToken ct) =>
                await trends.DeleteAsync(id, ct)
                    ? Results.NoContent()
                    : Results.NotFound());

        endpoints.MapGet(
            "/api/trends/{id:guid}/points",
            async (
                Guid id,
                string? from,
                string? to,
                int? maxPoints,
                TrendService trends,
                HistorianService historian,
                CancellationToken ct) =>
            {
                var trend =
                    await trends.GetByIdAsync(
                        id,
                        ct);

                if (trend is null)
                    return Results.NotFound();

                // Saved views obey the same explicit Historian-enrollment rule as ad-hoc views.
                var historianTags = await historian.GetRecordingTagIdsAsync(ct);
                if (trend.TagIds.Any(tagId => !historianTags.Contains(tagId)))
                    return Results.BadRequest(new { message = "Add each Trend Tag to Historian before querying samples." });

                var end =
                    TryParseDate(to)
                    ?? DateTimeOffset.UtcNow;

                var start =
                    TryParseDate(from)
                    ?? end.AddMinutes(
                        -trend.DefaultWindowMinutes);

                if (end <= start)
                {
                    return Results.BadRequest(
                        new
                        {
                            message =
                                "End time must be after start time."
                        });
                }

                var series =
                    new List<object>();

                foreach (var tagId in trend.TagIds)
                {
                    series.Add(
                        await historian.QuerySeriesAsync(
                            tagId,
                            start,
                            end,
                            maxPoints ?? 1600,
                            ct));
                }

                return Results.Ok(
                    new
                    {
                        trend,
                        from = start,
                        to = end,
                        series
                    });
            });


        endpoints.MapGet(
            "/api/historian/configurations",
            async (
                HistorianService historian,
                CancellationToken ct) =>
                Results.Ok(
                    await historian.GetConfigurationsAsync(ct)));

        endpoints.MapPost(
            "/api/historian/configurations",
            async (
                CreateOrUpdateHistorianConfigurationRequest request,
                HistorianService historian,
                LicenseService license,
                CancellationToken ct) =>
            {
                try
                {
                    if (!license.HasModule("HISTORIAN"))
                        return Results.Json(new { message = "Historian is not included in this PROGNODE license." }, statusCode: StatusCodes.Status403Forbidden);

                    var created = await historian.CreateConfigurationAsync(
                        request.TagId,
                        request.SampleIntervalSeconds,
                        request.RetentionDays,
                        ct);

                    return Results.Created(
                        $"/api/historian/configurations/{created.Id}",
                        created);
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { message = ex.Message });
                }
            });

        endpoints.MapPut(
            "/api/historian/configurations/{id:guid}",
            async (
                Guid id,
                CreateOrUpdateHistorianConfigurationRequest request,
                HistorianService historian,
                LicenseService license,
                CancellationToken ct) =>
            {
                if (!license.HasModule("HISTORIAN"))
                    return Results.Json(new { message = "Historian is not included in this PROGNODE license." },
                        statusCode: StatusCodes.Status403Forbidden);

                try
                {
                    return Results.Ok(
                        await historian.UpdateConfigurationAsync(
                            id,
                            request.TagId,
                            request.SampleIntervalSeconds,
                            request.RetentionDays,
                            ct));
                }
                catch (KeyNotFoundException)
                {
                    return Results.NotFound();
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(new { message = ex.Message });
                }
            });

        endpoints.MapDelete(
            "/api/historian/configurations/{id:guid}",
            async (
                Guid id,
                bool? deleteData,
                HistorianService historian,
                CancellationToken ct) =>
                await historian.RemoveConfigurationAsync(
                    id,
                    deleteData ?? false,
                    ct)
                    ? Results.NoContent()
                    : Results.NotFound());

        endpoints.MapGet(
            "/api/historian/stats",
            async (
                HistorianService historian,
                CancellationToken ct) =>
                Results.Ok(
                    await historian.GetStatsAsync(ct)));

        endpoints.MapGet(
            "/api/historian/export.zip",
            async (Guid trendId, string? from, string? to, TrendService trends,
                HistorianService historian, TagService tags, CancellationToken ct) =>
            {
                var trend = await trends.GetByIdAsync(trendId, ct);
                if (trend is null) return Results.NotFound();
                var end = TryParseDate(to) ?? DateTimeOffset.UtcNow;
                var start = TryParseDate(from) ?? end.AddMinutes(-trend.DefaultWindowMinutes);
                if (end <= start) return Results.BadRequest(new { message = "End time must be after start time." });
                var names = (await tags.GetAllAsync(ct)).Where(x => trend.TagIds.Contains(x.Id))
                    .ToDictionary(x => x.Id, x => x.Name);
                return await CreateHistorianArchiveDownloadAsync(historian, names, start, end,
                    $"PROGNODE_Trend_{SafeFileName(trend.Name)}_{DateTime.Now:yyyyMMdd_HHmmss}.zip", ct);
            });

        endpoints.MapGet(
            "/api/historian/export.csv",
            async (
                Guid trendId,
                string? from,
                string? to,
                TrendService trends,
                HistorianService historian,
                TagService tags,
                CancellationToken ct) =>
            {
                var trend =
                    await trends.GetByIdAsync(
                        trendId,
                        ct);

                if (trend is null)
                    return Results.NotFound();

                var end =
                    TryParseDate(to)
                    ?? DateTimeOffset.UtcNow;

                var start =
                    TryParseDate(from)
                    ?? end.AddMinutes(
                        -trend.DefaultWindowMinutes);

                if (end <= start)
                {
                    return Results.BadRequest(
                        new
                        {
                            message =
                                "End time must be after start time."
                        });
                }

                var names = (await tags.GetAllAsync(ct)).Where(x => trend.TagIds.Contains(x.Id))
                    .ToDictionary(x => x.Id, x => x.Name);
                return Results.Stream(stream => WriteHistorianCsvAsync(stream, historian, names, start, end, ct),
                    "text/csv; charset=utf-8", $"PROGNODE_{SafeFileName(trend.Name)}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            });

        // DATA EXCHANGE — Excel-friendly UTF-8 CSV exports used by commissioning.
        endpoints.MapGet(
            "/api/data-exchange/devices.csv",
            async (
                DeviceService devices,
                CancellationToken ct) =>
            {
                var rows = await devices.GetAllAsync(ct);
                var csv = new StringBuilder();
                csv.AppendLine("Name;Protocol;Host;Port;UnitId;PollIntervalMs;Status;CreatedAtUtc");

                foreach (var device in rows)
                {
                    csv.Append(Csv(device.Name));
                    csv.Append(';');
                    csv.Append(Csv(device.Protocol));
                    csv.Append(';');
                    csv.Append(Csv(device.Host ?? string.Empty));
                    csv.Append(';');
                    csv.Append(device.Port?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
                    csv.Append(';');
                    csv.Append(device.UnitId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
                    csv.Append(';');
                    csv.Append(device.PollIntervalMs.ToString(CultureInfo.InvariantCulture));
                    csv.Append(';');
                    csv.Append(Csv(device.Status));
                    csv.Append(';');
                    csv.Append(ExportUtcSeconds(device.CreatedAt));
                    csv.AppendLine();
                }

                return CsvDownload(csv, $"PROGNODE_Devices_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            });

        endpoints.MapGet(
            "/api/data-exchange/tags.csv",
            async (
                DeviceService devices,
                TagService tags,
                CancellationToken ct) =>
            {
                var deviceMap = (await devices.GetAllAsync(ct)).ToDictionary(x => x.Id);
                var rows = await tags.GetAllAsync(ct);
                var csv = new StringBuilder();
                csv.AppendLine("Device;Name;Address;DataType;BitIndex;ByteOrder;Unit;Offset;DecimalPlaces;Enabled");

                foreach (var tag in rows)
                {
                    var deviceName = deviceMap.TryGetValue(tag.DeviceId, out var device)
                        ? device.Name
                        : tag.DeviceId.ToString();

                    csv.Append(Csv(deviceName));
                    csv.Append(';');
                    csv.Append(Csv(tag.Name));
                    csv.Append(';');
                    csv.Append(Csv(tag.Address));
                    csv.Append(';');
                    csv.Append(Csv(tag.DataType.ToString()));
                    csv.Append(';');
                    csv.Append(tag.BitIndex?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
                    csv.Append(';');
                    csv.Append(Csv(tag.ByteOrder.ToString()));
                    csv.Append(';');
                    csv.Append(Csv(tag.Unit));
                    csv.Append(';');
                    csv.Append(tag.Offset.ToString(CultureInfo.InvariantCulture));
                    csv.Append(';');
                    csv.Append(tag.DecimalPlaces.ToString(CultureInfo.InvariantCulture));
                    csv.Append(';');
                    csv.Append(tag.Enabled ? "true" : "false");
                    csv.AppendLine();
                }

                return CsvDownload(csv, $"PROGNODE_Tags_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            });

        endpoints.MapGet(
            "/api/data-exchange/alarm-definitions.csv",
            async (
                DeviceService devices,
                TagService tags,
                AlarmService alarms,
                CancellationToken ct) =>
            {
                var deviceMap = (await devices.GetAllAsync(ct)).ToDictionary(x => x.Id);
                var tagMap = (await tags.GetAllAsync(ct)).ToDictionary(x => x.Id);
                var rows = await alarms.GetDefinitionsAsync(ct);
                var csv = new StringBuilder();
                csv.AppendLine("Device;Tag;AlarmText;Priority;BitIndex;TriggerValue;Condition;Threshold;Deadband;DelayOnMs;DelayOffMs;NotifyOnActive;NotifyOnCleared;NotificationMode;RepeatIntervalSeconds;ContinueAfterClearUntilAck;RequiresAcknowledgement;Enabled");

                foreach (var alarm in rows)
                {
                    tagMap.TryGetValue(alarm.TagId, out var tag);
                    var deviceName = tag is not null && deviceMap.TryGetValue(tag.DeviceId, out var device)
                        ? device.Name
                        : string.Empty;

                    csv.Append(Csv(deviceName));
                    csv.Append(';');
                    csv.Append(Csv(tag?.Name ?? alarm.TagId.ToString()));
                    csv.Append(';');
                    csv.Append(Csv(alarm.Text));
                    csv.Append(';');
                    csv.Append(Csv(alarm.Priority.ToString()));
                    csv.Append(';');
                    csv.Append(alarm.BitIndex?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
                    csv.Append(';');
                    csv.Append(alarm.TriggerValue ? "true" : "false");
                    csv.Append(';');
                    csv.Append(Csv(alarm.Condition.ToString()));
                    csv.Append(';');
                    csv.Append(alarm.Threshold?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
                    csv.Append(';');
                    csv.Append(alarm.Deadband.ToString(CultureInfo.InvariantCulture));
                    csv.Append(';');
                    csv.Append(alarm.DelayOnMs.ToString(CultureInfo.InvariantCulture));
                    csv.Append(';');
                    csv.Append(alarm.DelayOffMs.ToString(CultureInfo.InvariantCulture));
                    csv.Append(';');
                    csv.Append(alarm.NotifyOnActive ? "true" : "false");
                    csv.Append(';');
                    csv.Append(alarm.NotifyOnCleared ? "true" : "false");
                    csv.Append(';');
                    csv.Append(Csv(alarm.NotificationMode.ToString()));
                    csv.Append(';');
                    csv.Append(alarm.RepeatIntervalSeconds.ToString(CultureInfo.InvariantCulture));
                    csv.Append(';');
                    csv.Append(alarm.ContinueAfterClearUntilAcknowledged ? "true" : "false");
                    csv.Append(';');
                    csv.Append(alarm.RequiresAcknowledgement ? "true" : "false");
                    csv.Append(';');
                    csv.Append(alarm.Enabled ? "true" : "false");
                    csv.AppendLine();
                }

                return CsvDownload(csv, $"PROGNODE_AlarmDefinitions_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            });

        endpoints.MapGet(
            "/api/data-exchange/alarm-history.csv",
            async (
                AlarmService alarms,
                DateOnly? fromUtc,
                DateOnly? toUtc,
                CancellationToken ct) =>
            {
                if (fromUtc > toUtc) return Results.BadRequest("fromUtc must not be after toUtc");
                var rows = (await LoadAllAlarmOccurrencesAsync(alarms, ct))
                    .Where(alarm => (!fromUtc.HasValue || DateOnly.FromDateTime(alarm.ActiveAt.UtcDateTime) >= fromUtc.Value)
                        && (!toUtc.HasValue || DateOnly.FromDateTime(alarm.ActiveAt.UtcDateTime) <= toUtc.Value));
                var csv = new StringBuilder();
                csv.AppendLine("ActiveAtUtc;ClearedAtUtc;DurationSeconds;Source;AlarmText;Priority;State;SystemAlarm");

                foreach (var alarm in rows)
                {
                    var duration = (alarm.ClearedAt ?? DateTimeOffset.UtcNow) - alarm.ActiveAt;
                    csv.Append(ExportUtcSeconds(alarm.ActiveAt));
                    csv.Append(';');
                    csv.Append(alarm.ClearedAt is { } clearedAt ? ExportUtcSeconds(clearedAt) : string.Empty);
                    csv.Append(';');
                    csv.Append(Math.Max(0, Math.Round(duration.TotalSeconds)).ToString("0", CultureInfo.InvariantCulture));
                    csv.Append(';');
                    csv.Append(Csv(alarm.SourceName));
                    csv.Append(';');
                    csv.Append(Csv(alarm.Text));
                    csv.Append(';');
                    csv.Append(Csv(alarm.Priority.ToString()));
                    csv.Append(';');
                    csv.Append(Csv(alarm.State));
                    csv.Append(';');
                    csv.Append(alarm.IsSystem ? "true" : "false");
                    csv.AppendLine();
                }

                return CsvDownload(csv, $"PROGNODE_AlarmHistory_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            });

        endpoints.MapGet(
            "/api/data-exchange/alarm-history.xlsx",
            async (
                DeviceService devices,
                AlarmService alarms,
                DateOnly? fromUtc,
                DateOnly? toUtc,
                CancellationToken ct) =>
            {
                if (fromUtc > toUtc) return Results.BadRequest("fromUtc must not be after toUtc");
                var deviceMap = (await devices.GetAllAsync(ct)).ToDictionary(x => x.Id);
                var rows = (await LoadAllAlarmOccurrencesAsync(alarms, ct))
                    .Where(alarm => (!fromUtc.HasValue || DateOnly.FromDateTime(alarm.ActiveAt.UtcDateTime) >= fromUtc.Value)
                        && (!toUtc.HasValue || DateOnly.FromDateTime(alarm.ActiveAt.UtcDateTime) <= toUtc.Value));
                var headers = new[]
                {
                    "Active At UTC",
                    "Acknowledged At UTC",
                    "Cleared At UTC",
                    "Duration (s)",
                    "Device",
                    "Tag",
                    "Alarm Text",
                    "Priority",
                    "State",
                    "System Alarm"
                };

                var data = rows.Select(alarm =>
                {
                    var duration = (alarm.ClearedAt ?? DateTimeOffset.UtcNow) - alarm.ActiveAt;
                    var deviceName = alarm.IsSystem
                        ? alarm.SourceName
                        : alarm.DeviceId is Guid deviceId && deviceMap.TryGetValue(deviceId, out var device)
                            ? device.Name
                            : alarm.DeviceId?.ToString() ?? string.Empty;
                    var tagName = alarm.IsSystem ? "SYSTEM" : alarm.SourceName;

                    return (IReadOnlyList<object?>)new object?[]
                    {
                        ExportUtcSeconds(alarm.ActiveAt),
                        alarm.AcknowledgedAt is { } acknowledgedAt ? ExportUtcSeconds(acknowledgedAt) : string.Empty,
                        alarm.ClearedAt is { } clearedAt ? ExportUtcSeconds(clearedAt) : string.Empty,
                        Math.Max(0, Math.Round(duration.TotalSeconds)),
                        deviceName,
                        tagName,
                        alarm.Text,
                        alarm.Priority.ToString(),
                        alarm.State,
                        alarm.IsSystem ? "true" : "false"
                    };
                });

                return XlsxDownload(
                    "Alarm History",
                    headers,
                    data,
                    $"PROGNODE_AlarmHistory_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
            });

        endpoints.MapGet(
            "/api/data-exchange/historian-configurations.csv",
            async (
                DeviceService devices,
                TagService tags,
                HistorianService historian,
                CancellationToken ct) =>
            {
                var deviceMap = (await devices.GetAllAsync(ct)).ToDictionary(x => x.Id);
                var tagMap = (await tags.GetAllAsync(ct)).ToDictionary(x => x.Id);
                var rows = await historian.GetConfigurationsAsync(ct);
                var csv = new StringBuilder();
                csv.AppendLine("Device;Tag;SampleIntervalSeconds;RetentionDays;Enabled");

                foreach (var status in rows)
                {
                    var config = status.Configuration;
                    tagMap.TryGetValue(config.TagId, out var tag);
                    var deviceName = tag is not null && deviceMap.TryGetValue(tag.DeviceId, out var device)
                        ? device.Name
                        : string.Empty;

                    csv.Append(Csv(deviceName));
                    csv.Append(';');
                    csv.Append(Csv(tag?.Name ?? config.TagId.ToString()));
                    csv.Append(';');
                    csv.Append(config.SampleIntervalSeconds.ToString(CultureInfo.InvariantCulture));
                    csv.Append(';');
                    csv.Append(config.RetentionDays.ToString(CultureInfo.InvariantCulture));
                    csv.Append(';');
                    csv.Append(config.Enabled ? "true" : "false");
                    csv.AppendLine();
                }

                return CsvDownload(csv, $"PROGNODE_HistorianConfig_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            });

        endpoints.MapGet(
            "/api/data-exchange/historian/{tagId:guid}.zip",
            async (Guid tagId, HistorianService historian, TagService tags, CancellationToken ct) =>
            {
                var tag = (await tags.GetAllAsync(ct)).FirstOrDefault(x => x.Id == tagId);
                if (tag is null) return Results.NotFound();
                var status = (await historian.GetConfigurationsAsync(ct)).FirstOrDefault(x => x.Configuration.TagId == tagId);
                var from = status?.FirstSample ?? DateTimeOffset.FromUnixTimeSeconds(0);
                var to = DateTimeOffset.UtcNow.AddMinutes(1);
                var names = new Dictionary<Guid, string> { [tagId] = tag.Name };
                return await CreateHistorianArchiveDownloadAsync(historian, names, from, to,
                    $"PROGNODE_Historian_{SafeFileName(tag.Name)}_{DateTime.Now:yyyyMMdd_HHmmss}.zip", ct);
            });

        endpoints.MapGet(
            "/api/data-exchange/historian/{tagId:guid}.csv",
            async (
                Guid tagId,
                HistorianService historian,
                TagService tags,
                CancellationToken ct) =>
            {
                var tagMap = (await tags.GetAllAsync(ct)).ToDictionary(x => x.Id);
                if (!tagMap.TryGetValue(tagId, out var tag))
                    return Results.NotFound();

                var status = (await historian.GetConfigurationsAsync(ct)).FirstOrDefault(x => x.Configuration.TagId == tagId);
                var from = status?.FirstSample ?? DateTimeOffset.FromUnixTimeSeconds(0);
                var to = DateTimeOffset.UtcNow.AddMinutes(1);
                var names = new Dictionary<Guid, string> { [tagId] = tag.Name };
                return Results.Stream(stream => WriteHistorianCsvAsync(stream, historian, names, from, to, ct),
                    "text/csv; charset=utf-8", $"PROGNODE_Historian_{SafeFileName(tag.Name)}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            });

        endpoints.MapGet(
            "/api/system/time",
            () =>
            {
                var utcNow =
                    DateTimeOffset.UtcNow;

                var offset =
                    TimeZoneInfo.Local.GetUtcOffset(
                        utcNow);

                return Results.Ok(
                    new
                    {
                        utcUnixMilliseconds =
                            utcNow.ToUnixTimeMilliseconds(),
                        offsetMinutes =
                            (int)offset.TotalMinutes,
                        timeZone =
                            TimeZoneInfo.Local.Id
                    });
            });

        endpoints.MapPost(
            "/api/agent/heartbeat",
            (
                AgentHeartbeatRequest request,
                AgentPresenceService presence) =>
            {
                presence.Report(
                    request.MachineName,
                    request.Version,
                    request.NotificationMode);

                return Results.Ok(
                    new { accepted = true });
            });

        endpoints.MapGet(
            "/api/agent/status",
            (AgentPresenceService presence) =>
                Results.Ok(
                    presence.GetStatus()));

        // HF6.4: pairing proves device identity, NOT authorization to acknowledge.
        // Only the physical Core administrator grants device ACK explicitly.
        endpoints.MapGet("/api/mobile/v2/admin/paired-devices",(
            HttpContext context,ServerAccessService server,LocalAccessSessionService sessions) =>
        {
            var rejected=CheckQrAdmin(context,sessions);
            return rejected ?? Results.Ok(new {items=server.GetClients()});
        });
        endpoints.MapPut("/api/mobile/v2/admin/paired-devices/{clientId:guid}/access",(
            HttpContext context,Guid clientId,DeviceAccessUpdateRequest request,
            ServerAccessService server,LocalAccessSessionService sessions) =>
        {
            var rejected=CheckQrAdmin(context,sessions);
            if(rejected is not null) return rejected;
            try
            {
                var admin=sessions.GetStatus(context.Request.Headers["X-PROGNODE-Session"].ToString());
                var updated=server.SetDeviceAccess(clientId,request.DisplayName,
                    request.CanViewAlarms,request.CanAcknowledge,request.AckAuthMode,
                    admin.UserEmail ?? "LOCAL_ADMIN");
                return updated is null ? Results.NotFound(new {code="DEVICE_NOT_FOUND"})
                    : Results.Ok(updated);
            }
            catch(ArgumentException e)
            {return Results.BadRequest(new {code="INVALID_DEVICE_ACCESS",message=e.Message});}
        });
        endpoints.MapGet("/api/mobile/v2/admin/ack-audit",(
            HttpContext context,int? limit,LocalAccessSessionService sessions,
            MobileAckAuditStore audit) =>
        {
            var rejected=CheckQrAdmin(context,sessions);
            return rejected ?? Results.Ok(audit.GetRecent(limit??100));
        });

        endpoints.MapGet("/api/mobile/v2/device/access",(
            HttpContext context,ServerAccessService server) =>
        {
            if(!context.Request.IsHttps)
                return Results.Json(new {code="LAN_HTTPS_REQUIRED"},statusCode:426);
            var device=GetMobileBearer(context,server);
            if(device is null) return Results.Unauthorized();
            return Results.Ok(new {
                deviceId=device.ClientId,displayName=device.Name,
                device.CanViewAlarms,device.CanAcknowledge,
                ackAuthMode=device.AckAuthMode,
                requiresUserLogin=device.AckAuthMode!="DEVICE" || !device.CanAcknowledge,
                device.GrantedBy,lastUpdatedUtc=device.AccessUpdatedAtUtc
            });
        });

        endpoints.MapGet("/api/mobile/v2/alerts/pending",async(
            HttpContext context,ServerAccessService server,AlarmService alarms,
            LocalAccessSessionService sessions,CancellationToken ct) =>
        {
            var device=GetMobileBearer(context,server);
            var localUser=IsLoopbackRequest(context) &&
                sessions.Validate(context.Request.Headers["X-PROGNODE-Session"].ToString());
            if(!localUser && device is null) return Results.Unauthorized();
            if(device is not null && !device.CanViewAlarms)
                return Results.Json(new {code="ALARM_VIEW_FORBIDDEN"},statusCode:403);
            if(device is not null && !context.Request.IsHttps)
                return Results.Json(new {code="LAN_HTTPS_REQUIRED"},statusCode:426);
            return Results.Ok(new {schemaVersion=1,items=await alarms.GetPendingMobileAlertsAsync(ct)});
        });

        // A paired device may read scoped alarm notifications without human login;
        // DEVICE ACK requires an explicit persisted Core-admin grant.
        endpoints.MapGet("/api/mobile/v2/notifications",(
            HttpContext context,string? cursor,int? limit,
            NotificationEventStore notifications,ServerAccessService server,
            LocalAccessSessionService sessions) =>
        {
            if (!MobileReaderAllowed(context,sessions,server)) return Results.Unauthorized();
            if(!long.TryParse(cursor,out var after) && !string.IsNullOrWhiteSpace(cursor))
                return Results.BadRequest(new { code="INVALID_CURSOR" });
            var size=Math.Clamp(limit ?? 100,1,200);
            var page=notifications.GetAfter(after,size+1);
            var scanned=page.Take(size).ToArray();
            var paired=GetMobileBearer(context,server);
            var deliveryEnabled=server.NotificationDeliveryEnabled &&
                (paired is null || server.DeliveryEnabledFor(paired.ClientId));
            var items=deliveryEnabled?scanned:Array.Empty<Prognode.Contracts.Notifications.NotificationEvent>();
            return Results.Ok(new {
                schemaVersion=2,serverId=server.Identity.ServerId,
                items,hasMore=page.Count>size,
                nextCursor=(scanned.LastOrDefault()?.Id ?? after).ToString(),
                deliveryEnabled,
                storageHealthy=notifications.StorageHealthy
            });
        });

        endpoints.MapGet("/api/mobile/v2/alarms/active",(
            HttpContext context, AlarmService alarms, LocalAccessSessionService sessions,
            ServerAccessService server) =>
        {
            if(!MobileReaderAllowed(context,sessions,server)) return Results.Unauthorized();
            return Results.Ok(new { schemaVersion=2,items=alarms.GetActive() });
        });

        endpoints.MapPost("/api/mobile/v2/alarms/{occurrenceId:guid}/ack",
            async (HttpContext context,Guid occurrenceId,AlarmService alarms,
                LocalAccessSessionService sessions,ServerAccessService server,
                MobileAckAuditStore audit,CancellationToken ct) =>
        {
            var device=GetMobileBearer(context,server);
            var session=sessions.GetStatus(context.Request.Headers["X-PROGNODE-Session"].ToString());
            var userAllowed=session.Authenticated &&
                ((device is not null && device.CanViewAlarms) || IsLoopbackRequest(context));
            string principal;
            if(userAllowed)
                principal="USER:"+(session.UserEmail??"UNKNOWN");
            else if(device is null)
                return Results.Json(new {code="ACK_USER_AUTH_REQUIRED"},statusCode:401);
            else if(!context.Request.IsHttps)
                return Results.Json(new {code="LAN_HTTPS_REQUIRED"},statusCode:426);
            else if(!device.CanAcknowledge || !device.CanViewAlarms || device.AckAuthMode!="DEVICE")
            {
                var code=device.AckAuthMode=="USER_SESSION" ? "ACK_USER_AUTH_REQUIRED" : "ACK_FORBIDDEN";
                audit.Record(server.Identity.ServerId,occurrenceId,device.ClientId,
                    "DEVICE:"+device.ClientId.ToString("D"),code,server.GetClient(device.ClientId)?.Name);
                return Results.Json(new {code},statusCode:code=="ACK_USER_AUTH_REQUIRED"?401:403);
            }
            else principal="DEVICE:"+device.ClientId.ToString("D");

            var result=await alarms.AcknowledgeOccurrenceAsync(occurrenceId,ct);
            var actorDisplayName=userAllowed
                ? (string.IsNullOrWhiteSpace(session.UserName)?session.UserEmail:session.UserName)
                : device is null ? null : server.GetClient(device.ClientId)?.Name;
            audit.Record(server.Identity.ServerId,occurrenceId,device?.ClientId,principal,
                result.ToString().ToUpperInvariant(),actorDisplayName);
            return result switch {
                OccurrenceAckResult.Acknowledged => Results.Ok(new {acknowledged=true,occurrenceId}),
                OccurrenceAckResult.StaleOccurrence => Results.Json(
                    new {code="STALE_OCCURRENCE",message="A newer activation cannot be acknowledged by an older notification."},
                    statusCode:409),
                _ => Results.NotFound(new {code="OCCURRENCE_NOT_FOUND"})
            };
        });

        endpoints.MapPost("/api/mobile/v2/devices/push-token",async(
            HttpContext context,MobilePushTokenRequest input,
            LocalAccessSessionService sessions,RemoteAccessService remote,CancellationToken ct) =>
        {
            if(!TryGetPairedClientId(context,out var clientId)) return Results.Unauthorized();
            if(!sessions.Validate(context.Request.Headers["X-PROGNODE-Session"].ToString()))
                return Results.Unauthorized();
            var result=await remote.RegisterPushTokenAsync(clientId,input.Platform,input.PushToken,ct);
            return RemoteOperationResult(result);
        });

        endpoints.MapPost("/api/mobile/v2/notifications/receipt",(
            HttpContext context,MobileReceiptRequest input,
            NotificationEventStore notifications) =>
        {
            if(!TryGetPairedClientId(context,out var clientId)) return Results.Unauthorized();
            if(input.EventId<=0 || input.State is not ("DEVICE_RECEIVED" or "USER_OPENED"))
                return Results.BadRequest(new {code="INVALID_RECEIPT"});
            return notifications.TryRecordReceipt(input.EventId,clientId,input.State)
                ? Results.Ok(new {recorded=true})
                : Results.NotFound(new {code="EVENT_NOT_FOUND"});
        });

        endpoints.MapGet("/api/mobile/v2/notifications/health",(
            HttpContext context,NotificationEventStore notifications,
            LocalAccessSessionService sessions) =>
        {
            if(!IsLoopbackRequest(context) ||
               !sessions.Validate(context.Request.Headers["X-PROGNODE-Session"].ToString()))
                return Results.Unauthorized();
            return Results.Ok(notifications.GetHealth());
        });

        endpoints.MapGet(
            "/api/notifications",
            (
                long? after,
                NotificationEventStore notifications) =>
                Results.Ok(
                    after is null
                        ? notifications.GetRecent(100)
                        : notifications.GetAfter(
                            after.Value)));

        endpoints.MapGet("/api/notifications/delivery",(
            HttpContext context,ServerAccessService server) =>
        {
            if(!IsLoopbackRequest(context))return Results.StatusCode(403);
            return Results.Ok(new {enabled=server.NotificationDeliveryEnabled,
                clients=server.GetClients().Select(x=>new {x.ClientId,x.Name,x.Platform,x.NotificationsEnabled})});
        });
        endpoints.MapPut("/api/notifications/delivery",(
            HttpContext context,NotificationDeliveryUpdateRequest request,
            ServerAccessService server,LocalAccessSessionService sessions,
            RemoteNotificationOutboxStore outbox) =>
        {
            var denied=CheckQrAdmin(context,sessions);
            if(denied is not null)return denied;
            server.SetNotificationDelivery(request.Enabled);
            if(!request.Enabled)outbox.SuppressPending();
            return Results.Ok(new {enabled=server.NotificationDeliveryEnabled});
        });
        endpoints.MapPut("/api/notifications/delivery/clients/{clientId:guid}",(
            HttpContext context,Guid clientId,NotificationDeliveryUpdateRequest request,
            ServerAccessService server,LocalAccessSessionService sessions) =>
        {
            var denied=CheckQrAdmin(context,sessions);
            if(denied is not null)return denied;
            var updated=server.SetClientNotifications(clientId,request.Enabled);
            return updated is null?Results.NotFound(new {code="CLIENT_NOT_FOUND"})
                :Results.Ok(new {updated.ClientId,updated.NotificationsEnabled});
        });

        endpoints.MapPost(
            "/api/notifications/test",
            (
                TestNotificationRequest request,
                NotificationEventStore notifications) =>
            {
                var item =
                    notifications.Publish(
                        "Information",
                        string.IsNullOrWhiteSpace(
                            request.Title)
                            ? "Agent Test"
                            : request.Title.Trim(),
                        string.IsNullOrWhiteSpace(
                            request.Message)
                            ? "Windows Agent notification path is working."
                            : request.Message.Trim());

                return Results.Ok(item);
            });

        endpoints.MapGet(
            "/api/overview",
            async (
                DeviceService devices,
                TagService tags,
                AlarmService alarms,
                HistorianService historian,
                SystemStatusService status,
                LicenseService license,
                CancellationToken ct) =>
            {
                var historianStats =
                    await historian.GetStatsAsync(ct);

                return Results.Ok(
                    new
                    {
                        core = status.GetSnapshot(),
                        license = new
                        {
                            license.Current.Status,
                            license.Current.Plan
                        },
                        devices = new
                        {
                            total =
                                await devices.CountAsync(ct)
                        },
                        tags = new
                        {
                            monitored =
                                await tags.CountAsync(ct)
                        },
                        alarms = new
                        {
                            active =
                                alarms.GetActive().Count,
                            definitions =
                                await alarms.CountAsync(ct)
                        },
                        historian = new
                        {
                            recordedTags =
                                historianStats.RecordedTags,
                            totalSamples =
                                historianStats.TotalSamples
                        }
                    });
            });

        return endpoints;
    }

    private static object SignedOutLicenseSummary(LicenseSnapshot current)
    {
        var installed = current.LicenseId is not ("NONE" or "INVALID");
        return new
        {
            licenseInstalled = installed,
            isValid = false,
            status = installed ? "SIGN_IN_REQUIRED" : current.Status,
            licenseId = installed ? "INSTALLED" : current.LicenseId,
            customer = (string?)null,
            plan = (string?)null,
            expiresAt = (DateTimeOffset?)null,
            graceUntil = (DateTimeOffset?)null,
            assignedUserId = (string?)null,
            assignedUserName = (string?)null,
            assignedUserEmail = (string?)null,
            portalRole = (string?)null,
            billingPeriod = (string?)null,
            validFrom = (DateTimeOffset?)null,
            pricingVersion = (string?)null,
            entitlements = new
            {
                modules = Array.Empty<string>(),
                remoteAccessEnabled = false,
                remoteAccessUnlimited = false,
                maxRemoteClients = (int?)null,
                remoteAccessExpiresAtUtc = (DateTimeOffset?)null
            }
        };
    }

    public sealed record MobileReceiptRequest(long EventId,string State);
    public sealed record MobilePushTokenRequest(string Platform,string PushToken);

    private static bool MobileReaderAllowed(HttpContext context,LocalAccessSessionService sessions,
        ServerAccessService server)
    {
        var paired=GetMobileBearer(context,server);
        if(paired is not null) return paired.CanViewAlarms && context.Request.IsHttps;
        return IsLoopbackRequest(context) &&
            sessions.Validate(context.Request.Headers["X-PROGNODE-Session"].ToString());
    }

    private static bool IsLoopbackRequest(HttpContext context)
    {
        var remote = context.Connection.RemoteIpAddress;
        return remote is null || System.Net.IPAddress.IsLoopback(remote);
    }

    private static bool TryGetPairedClientId(HttpContext context, out Guid clientId)
    {
        if (context.Items.TryGetValue("PROGNODE_CLIENT_ID", out var value) && value is Guid id)
        {
            clientId = id;
            return true;
        }

        clientId = Guid.Empty;
        return false;
    }

    private static IResult RemoteOperationResult(RemoteAccessOperationResult result)
    {
        if (result.Success)
            return Results.Ok(result);

        var statusCode = result.Code switch
        {
            "LOCAL_LICENSE_INVALID" or "REMOTE_ACCESS_NOT_ENTITLED" or "USER_NOT_AUTHORIZED" => StatusCodes.Status403Forbidden,
            "CLOUD_NOT_CONFIGURED" or "CLOUD_UNAVAILABLE" => StatusCodes.Status503ServiceUnavailable,
            "CLIENT_NOT_FOUND" => StatusCodes.Status404NotFound,
            "DEVICE_IDENTITY_REQUIRED" => StatusCodes.Status400BadRequest,
            "REMOTE_CLIENT_LIMIT_REACHED" or "SERVER_NOT_BOUND" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };

        return Results.Json(result, statusCode: statusCode);
    }

    private static DateTimeOffset? TryParseDate(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var parsed)
                ? parsed.ToUniversalTime()
                : null;
    }

    private static async Task<IReadOnlyList<Prognode.Contracts.Alarms.AlarmOccurrenceRecord>>
        LoadAllAlarmOccurrencesAsync(AlarmService alarms, CancellationToken ct)
    {
        const int pageSize = 100;
        var rows = new List<Prognode.Contracts.Alarms.AlarmOccurrenceRecord>();
        while (true)
        {
            var page = await alarms.GetHistoryPageAsync(rows.Count, pageSize,
                cancellationToken: ct);
            if (page.Count == 0) break;
            rows.AddRange(page);
            if (page.Count < pageSize) break;
        }
        return rows;
    }

    private static IResult XlsxDownload(
        string sheetName,
        IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<object?>> rows,
        string fileName)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteZipText(archive, "[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                "</Types>");

            WriteZipText(archive, "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                "</Relationships>");

            WriteZipText(archive, "xl/workbook.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                "<sheets><sheet name=\"" + XmlEscape(sheetName) + "\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");

            WriteZipText(archive, "xl/_rels/workbook.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                "</Relationships>");

            var sheet = new StringBuilder();
            sheet.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sheet.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
            sheet.Append("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
            sheet.Append("<cols>");
            sheet.Append("<col min=\"1\" max=\"3\" width=\"26\" customWidth=\"1\"/>");
            sheet.Append("<col min=\"4\" max=\"4\" width=\"14\" customWidth=\"1\"/>");
            sheet.Append("<col min=\"5\" max=\"6\" width=\"24\" customWidth=\"1\"/>");
            sheet.Append("<col min=\"7\" max=\"7\" width=\"42\" customWidth=\"1\"/>");
            sheet.Append("<col min=\"8\" max=\"10\" width=\"16\" customWidth=\"1\"/>");
            sheet.Append("</cols><sheetData>");
            AppendXlsxRow(sheet, headers.Cast<object?>());
            foreach (var row in rows) AppendXlsxRow(sheet, row);
            sheet.Append("</sheetData></worksheet>");
            WriteZipText(archive, "xl/worksheets/sheet1.xml", sheet.ToString());
        }

        return Results.File(
            output.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    private static void AppendXlsxRow(StringBuilder sheet, IEnumerable<object?> values)
    {
        sheet.Append("<row>");
        foreach (var value in values)
        {
            if (value is double or float or decimal or int or long or short)
            {
                var numeric = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0";
                sheet.Append("<c t=\"n\"><v>").Append(numeric).Append("</v></c>");
                continue;
            }

            sheet.Append("<c t=\"inlineStr\"><is><t xml:space=\"preserve\">")
                .Append(XmlEscape(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty))
                .Append("</t></is></c>");
        }
        sheet.Append("</row>");
    }

    private static void WriteZipText(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private static string XmlEscape(string value) => SecurityElement.Escape(value) ?? string.Empty;

    private static string SafeFileName(string name) =>
        string.Concat(name.Select(c => char.IsLetterOrDigit(c) ? c : '_'));

    private static async Task WriteHistorianCsvAsync(
        Stream output, HistorianService historian, IReadOnlyDictionary<Guid, string> names,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        await using var writer = new StreamWriter(output, new UTF8Encoding(true), 8192, leaveOpen: true);
        await writer.WriteLineAsync("sep=;");
        await writer.WriteLineAsync("Timestamp UTC;Tag;Value;Quality");
        for (var cursor = from; cursor < to;)
        {
            var monthEnd = new DateTimeOffset(cursor.Year, cursor.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1);
            var next = monthEnd < to ? monthEnd : to;
            await foreach (var sample in historian.StreamRawAsync(names.Keys.ToArray(), cursor, next, ct))
                await writer.WriteLineAsync(HistorianCsvRow(sample, names));
            await writer.FlushAsync(ct);
            cursor = next;
        }
    }

    private static string HistorianCsvRow(
        Prognode.Contracts.Historian.HistorianSample sample, IReadOnlyDictionary<Guid, string> names)
    {
        var timestamp = ExportUtcSeconds(DateTimeOffset.FromUnixTimeMilliseconds(sample.TimestampUnixMs));
        var tagName = names.TryGetValue(sample.TagId, out var name) ? name : sample.TagId.ToString();
        return $"{timestamp};{Csv(tagName)};{sample.Value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty};{Csv(sample.Quality)}";
    }

    private static async Task<IResult> CreateHistorianArchiveDownloadAsync(
        HistorianService historian, IReadOnlyDictionary<Guid, string> names,
        DateTimeOffset from, DateTimeOffset to, string fileName, CancellationToken ct)
    {
        // ZipArchive writes its final central directory synchronously when disposed.
        // Build it on an async-capable temporary file, then let ASP.NET copy that file
        // asynchronously to the HTTP response. DeleteOnClose removes it after delivery.
        var path = Path.Combine(Path.GetTempPath(), $"prognode-historian-{Guid.NewGuid():N}.zip");
        var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.DeleteOnClose);
        try
        {
            await WriteHistorianArchiveAsync(file, historian, names, from, to, ct);
            await file.FlushAsync(ct);
            file.Position = 0;
            return Results.File(file, "application/zip", fileName);
        }
        catch
        {
            await file.DisposeAsync();
            throw;
        }
    }

    private static async Task WriteHistorianArchiveAsync(
        Stream output, HistorianService historian, IReadOnlyDictionary<Guid, string> names,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        const int rowsPerFile = 500_000; // Safely below Excel's worksheet row limit.
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        StreamWriter? writer = null;
        var part = 0;
        var rows = 0;
        try
        {
            // Monthly slices release SQLite read transactions instead of pinning the WAL
            // for an entire multi-year download. Each CSV part is streamed, never buffered.
            for (var cursor = from; cursor < to;)
            {
                var monthEnd = new DateTimeOffset(cursor.Year, cursor.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1);
                var next = monthEnd < to ? monthEnd : to;
                await foreach (var sample in historian.StreamRawAsync(names.Keys.ToArray(), cursor, next, ct))
                {
                    if (writer is null || rows >= rowsPerFile)
                    {
                        if (writer is not null) await writer.DisposeAsync();
                        writer = new StreamWriter(archive.CreateEntry($"historian_part_{++part:D4}.csv", CompressionLevel.Fastest).Open(),
                            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                        await writer.WriteLineAsync("sep=;");
                        await writer.WriteLineAsync("Timestamp UTC;Tag;Value;Quality");
                        rows = 0;
                    }
                    await writer.WriteLineAsync(HistorianCsvRow(sample, names));
                    rows++;
                }
                cursor = next;
            }
            if (writer is null)
            {
                await using var empty = new StreamWriter(archive.CreateEntry("historian_part_0001.csv").Open(), new UTF8Encoding(true));
                await empty.WriteLineAsync("sep=;");
                await empty.WriteLineAsync("Timestamp UTC;Tag;Value;Quality");
            }
        }
        finally { if (writer is not null) await writer.DisposeAsync(); }
    }

    private static IResult CsvDownload(
        StringBuilder csv,
        string fileName)
    {
        var preamble = Encoding.UTF8.GetPreamble();
        var normalized = csv.ToString().StartsWith("sep=;", StringComparison.OrdinalIgnoreCase) ? csv.ToString() : "sep=;\n" + csv;
        var content = Encoding.UTF8.GetBytes(normalized);
        var bytes = new byte[preamble.Length + content.Length];
        Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
        Buffer.BlockCopy(content, 0, bytes, preamble.Length, content.Length);

        return Results.File(
            bytes,
            "text/csv; charset=utf-8",
            fileName);
    }

    private static string ExportUtcSeconds(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static string Csv(string value) =>
        "\"" + value.Replace("\"", "\"\"") + "\"";
}


public sealed record QrCreateRequest(string? SelectedHost);
public sealed record QrPairClientRequest(Guid ServerId, string PairingTicket,
    string ClientName, string? Platform, string? DevicePublicKey);

public sealed record PairClientRequest(
    Guid ServerId,
    string ClientName,
    string PairingCode,
    string? Platform = null,
    string? DevicePublicKey = null);

public sealed record RemoteClientRegistrationRequest(
    Guid? ClientId,
    string? DevicePublicKey,
    string? Platform);

public sealed record LanProbeRequest(int InterfaceIndex);
public sealed record LanAgentClaimRequest(string RequestId);
