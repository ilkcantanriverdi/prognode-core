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
    private static void MapRemoteAccessEndpoints(IEndpointRouteBuilder endpoints)
    {
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

        // Remote registration is done by the paired device itself: it signs the challenge with its own
        // key and receives its off-site token, so both steps require the device's LAN pairing.
        endpoints.MapPost(
            "/api/remote-access/clients/challenge",
            async (HttpContext context, RemoteAccessService remoteAccess, CancellationToken ct) =>
            {
                if (!TryGetPairedClientId(context, out var clientId))
                    return Results.BadRequest(new { code = "PAIRED_CLIENT_REQUIRED", message = "Only a paired device can enable Remote Access for itself." });
                var (result, challenge) = await remoteAccess.CreateRegistrationChallengeAsync(clientId, ct);
                if (challenge is null)
                    return RemoteOperationResult(result);
                return Results.Ok(new
                {
                    success = true,
                    code = result.Code,
                    challengeId = challenge.ChallengeId,
                    challenge = challenge.Challenge,
                    expiresAtUtc = challenge.ExpiresAtUtc,
                });
            });

        endpoints.MapPost(
            "/api/remote-access/clients/register",
            async (
                HttpContext context,
                RemoteClientRegistrationRequest request,
                RemoteAccessService remoteAccess,
                CancellationToken ct) =>
            {
                if (!TryGetPairedClientId(context, out var clientId))
                    return Results.BadRequest(new { code = "PAIRED_CLIENT_REQUIRED", message = "Only a paired device can enable Remote Access for itself." });
                var result = await remoteAccess.RegisterClientAsync(
                    clientId,
                    request.DevicePublicKey,
                    request.Platform,
                    request.ChallengeId ?? Guid.Empty,
                    request.DeviceSignature,
                    ct);
                context.Response.Headers.CacheControl = "no-store";
                return RemoteOperationResult(result);
            });

        endpoints.MapDelete(
            "/api/remote-access/clients/{clientId:guid}",
            async (HttpContext context, Guid clientId, RemoteAccessService remoteAccess, CancellationToken ct) =>
            {
                if (TryGetPairedClientId(context, out var callerClientId) && callerClientId != clientId)
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                return RemoteOperationResult(await remoteAccess.RevokeClientAsync(clientId, ct));
            });
    }
}
