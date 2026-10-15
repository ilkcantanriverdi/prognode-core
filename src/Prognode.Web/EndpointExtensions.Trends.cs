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
    private static void MapTrendsEndpoints(IEndpointRouteBuilder endpoints)
    {
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
    }
}
