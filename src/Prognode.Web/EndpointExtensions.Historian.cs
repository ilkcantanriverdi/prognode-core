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
    private static void MapHistorianEndpoints(IEndpointRouteBuilder endpoints)
    {
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
    }
}
