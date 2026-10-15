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
    private static void MapBatchesEndpoints(IEndpointRouteBuilder endpoints)
    {
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
    }
}
