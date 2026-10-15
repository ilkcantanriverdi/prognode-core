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
    private static void MapAlarmsEndpoints(IEndpointRouteBuilder endpoints)
    {
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

        // Core UI ACK (review Y4): occurrence-specific, so a newer activation is never acknowledged
        // unseen, and attributed to the signed-in Core user. The session gate is the mutation
        // middleware in Program.cs.
        endpoints.MapPost(
            "/api/alarms/occurrences/{occurrenceId:guid}/ack",
            async (
                HttpContext context,
                Guid occurrenceId,
                AlarmService alarms,
                LocalAccessSessionService sessions,
                CancellationToken ct) =>
            {
                var actor = CoreUserActor(sessions, context);
                var result = await alarms.AcknowledgeOccurrenceAsync(occurrenceId, ct, actor);
                return result switch
                {
                    OccurrenceAckResult.Acknowledged => Results.Ok(new { acknowledged = true, occurrenceId }),
                    OccurrenceAckResult.StaleOccurrence => Results.Json(
                        new { code = "STALE_OCCURRENCE", message = "This alarm was activated again. Review the new activation before acknowledging it." },
                        statusCode: 409),
                    _ => Results.NotFound(new { code = "OCCURRENCE_NOT_FOUND", message = "This alarm occurrence is no longer awaiting acknowledgement." })
                };
            });

        // Legacy key-based ACK, kept for older clients only. The Core UI uses the occurrence route.
        endpoints.MapPost(
            "/api/alarms/ack",
            async (
                HttpContext context,
                AcknowledgeAlarmRequest request,
                AlarmService alarms,
                LocalAccessSessionService sessions,
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
                    ct,
                    CoreUserActor(sessions, context))
                    ? Results.Ok(
                        new { acknowledged = true })
                    : Results.NotFound();
            });
    }
}
