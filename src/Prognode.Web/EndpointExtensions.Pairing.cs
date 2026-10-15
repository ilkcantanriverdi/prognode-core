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
    private static void MapPairingEndpoints(IEndpointRouteBuilder endpoints)
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
                        message = "Prepare LAN access on the local PROGNODE Monitor and confirm it in the Windows tray within three minutes."
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
                    return Results.Json(new { code = "HTTPS_NOT_READY", message = "LAN HTTPS is not configured on this PROGNODE Monitor." }, statusCode: 409);
                if (!ctx.Request.IsHttps ||
                    ctx.Connection.LocalPort != serverAccess.Identity.SecureApiPort)
                    return Results.Json(new { code = "HTTPS_REQUIRED", message = "Use the PROGNODE Monitor LAN HTTPS endpoint in the scanned QR." }, statusCode: 426);
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
    }
}
