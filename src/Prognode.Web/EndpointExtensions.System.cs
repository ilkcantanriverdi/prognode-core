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
    private static void MapSystemEndpoints(IEndpointRouteBuilder endpoints)
    {
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

        endpoints.MapPost(
            "/api/agent/open-alarms",
            (HttpContext context, AgentPresenceService presence) =>
                IsLoopbackRequest(context)
                    ? Results.Ok(new { request = presence.RequestOpenAlarms() })
                    : Results.StatusCode(403));

        endpoints.MapGet(
            "/api/agent/status",
            (AgentPresenceService presence) =>
                Results.Ok(
                    presence.GetStatus()));

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
    }
}
