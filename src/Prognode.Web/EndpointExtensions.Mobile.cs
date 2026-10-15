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
    private static void MapMobileEndpoints(IEndpointRouteBuilder endpoints)
    {
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

        // Recent alarm history for paired PCs and phones: when each alarm came and went and who
        // acknowledged it. Same attribution as the Core history page; read-only.
        endpoints.MapGet("/api/mobile/v2/alarms/history",async(
            HttpContext context,int? offset,int? limit,AlarmService alarms,MobileAckAuditStore ackAudit,
            ServerAccessService server,LicenseService license,LocalAccessSessionService sessions,CancellationToken ct) =>
        {
            if(!MobileReaderAllowed(context,sessions,server)) return Results.Unauthorized();
            var page=await alarms.GetHistoryPageAsync(Math.Max(0,offset??0),Math.Clamp(limit??50,1,100),null,null,ct);
            var acknowledgements=ackAudit.GetAcknowledgements(server.Identity.ServerId,page.Select(x=>x.OccurrenceId));
            var items=page.Select(alarm =>
                acknowledgements.TryGetValue(alarm.OccurrenceId,out var attribution)
                    ? alarm with { AcknowledgedAt=attribution.TimestampUtc,
                        AcknowledgedBy=ResolveMobileAckActor(attribution,server,license) }
                    : alarm).ToArray();
            return Results.Ok(new { schemaVersion=1,items,total=await alarms.CountMatchingHistoryAsync(null,null,ct) });
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

            var actorDisplayName=userAllowed
                ? (string.IsNullOrWhiteSpace(session.UserName)?session.UserEmail:session.UserName)
                : device is null ? null : server.GetClient(device.ClientId)?.Name;
            var historyActor=userAllowed
                ? actorDisplayName
                : $"Mobile device: {actorDisplayName ?? device?.ClientId.ToString("D")}";
            var result=await alarms.AcknowledgeOccurrenceAsync(occurrenceId,ct,historyActor);
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
            // Push tokens are held by PROGNODE Cloud: the device registers its token there with its
            // Remote Access token (POST /api/remote-access/clients/push-token). Core never sends push.
            return Results.Json(new
            {
                success=false,
                code="PUSH_TOKEN_CLOUD",
                message="Register the push token with PROGNODE Cloud using the device Remote Access token."
            },statusCode:StatusCodes.Status410Gone);
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
    }
}
