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
    private static void MapTagsEndpoints(IEndpointRouteBuilder endpoints)
    {
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
    }
}
