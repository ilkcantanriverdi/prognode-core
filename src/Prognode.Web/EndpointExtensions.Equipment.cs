using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Prognode.Trends;
using Prognode.Web.Models;

namespace Prognode.Web;

public static partial class EndpointExtensions
{
    // EQUIPMENT: optional cards that group Tags by machine. Live values, alarms and history come from
    // the existing Tag, alarm and Historian endpoints.
    private static void MapEquipmentEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/api/equipment",
            async (EquipmentService equipment, CancellationToken ct) => Results.Ok(await equipment.GetAllAsync(ct)));

        endpoints.MapPost("/api/equipment",
            async (CreateOrUpdateEquipmentRequest request, EquipmentService equipment, CancellationToken ct) =>
            {
                try
                {
                    var created = await equipment.CreateAsync(request.Name, request.DeviceId, request.Items, ct);
                    return Results.Created($"/api/equipment/{created.Id}", created);
                }
                catch (ArgumentException ex) { return Results.BadRequest(new { message = ex.Message }); }
            });

        endpoints.MapPut("/api/equipment/{id:guid}",
            async (Guid id, CreateOrUpdateEquipmentRequest request, EquipmentService equipment, CancellationToken ct) =>
            {
                try { return Results.Ok(await equipment.UpdateAsync(id, request.Name, request.DeviceId, request.Items, ct)); }
                catch (KeyNotFoundException) { return Results.NotFound(); }
                catch (ArgumentException ex) { return Results.BadRequest(new { message = ex.Message }); }
            });

        endpoints.MapDelete("/api/equipment/{id:guid}",
            async (Guid id, EquipmentService equipment, CancellationToken ct) =>
                await equipment.DeleteAsync(id, ct) ? Results.NoContent() : Results.NotFound());
    }
}
