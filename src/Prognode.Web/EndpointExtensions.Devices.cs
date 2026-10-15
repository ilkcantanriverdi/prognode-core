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
    private static void MapDevicesEndpoints(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
            "/api/protocols",
            (ProtocolCatalog catalog) =>
                Results.Ok(catalog.GetAll()));

        endpoints.MapGet(
            "/api/devices",
            async (
                DeviceService devices,
                IDeviceCredentialStore credentials,
                CancellationToken ct) =>
                Results.Ok(
                    (await devices.GetAllAsync(ct))
                        .Select(device => device with { Username = credentials.GetUsername(device.Id) })));

        endpoints.MapPost(
            "/api/network/ping",
            async (
                PingHostRequest request,
                NetworkPingService ping,
                CancellationToken ct) =>
            {
                try
                {
                    var timeoutMs =
                        request.TimeoutMs <= 0
                            ? 1200
                            : Math.Clamp(
                                request.TimeoutMs,
                                250,
                                10000);

                    return Results.Ok(
                        await ping.PingAsync(
                            request.Host,
                            timeoutMs,
                            ct));
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(
                        new { message = ex.Message });
                }
            });

        endpoints.MapPost(
            "/api/devices/{id:guid}/ping",
            async (
                Guid id,
                DeviceService devices,
                NetworkPingService ping,
                CancellationToken ct) =>
            {
                var device =
                    await devices.GetByIdAsync(
                        id,
                        ct);

                if (device is null)
                    return Results.NotFound();

                if (string.IsNullOrWhiteSpace(
                    device.Host))
                {
                    return Results.BadRequest(
                        new
                        {
                            message =
                                "This device has no Host / IP."
                        });
                }

                return Results.Ok(
                    await ping.PingAsync(
                        device.Host,
                        1200,
                        ct));
            });

        endpoints.MapPost(
            "/api/devices/modbus-tcp/test",
            async (
                TestModbusTcpRequest request,
                ModbusTcpProbe probe,
                CancellationToken ct) =>
            {
                var host =
                    (request.Host ?? string.Empty).Trim();

                if (string.IsNullOrWhiteSpace(host))
                    return Results.BadRequest(
                        new { message = "Host / IP is required." });

                if (request.Port is < 1 or > 65535)
                    return Results.BadRequest(
                        new { message = "Port must be between 1 and 65535." });

                if (request.UnitId is < 0 or > 255)
                    return Results.BadRequest(
                        new { message = "Unit ID must be between 0 and 255." });

                var timeoutMs =
                    request.TimeoutMs <= 0
                        ? 2000
                        : Math.Clamp(
                            request.TimeoutMs,
                            250,
                            30000);

                return Results.Ok(
                    await probe.TestAsync(
                        host,
                        request.Port,
                        request.UnitId,
                        timeoutMs,
                        ct));
            });

        endpoints.MapPost(
            "/api/devices/modbus-tcp",
            async (
                CreateModbusTcpDeviceRequest request,
                DeviceService devices,
                CancellationToken ct) =>
            {
                try
                {
                    var created =
                        await devices.CreateModbusTcpAsync(
                            request.Name,
                            request.Host,
                            request.Port,
                            request.UnitId,
                            request.PollIntervalMs,
                            ct);

                    return Results.Created(
                        $"/api/devices/{created.Id}",
                        created);
                }
                catch (DeviceCapacityExceededException)
                {
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                }
                catch (ArgumentException ex)
                {
                    return Results.BadRequest(
                        new { message = ex.Message });
                }
            });

        endpoints.MapPost("/api/devices/siemens-s7-tcp/test",async (S7TestRequest req,CancellationToken ct)=>
        {
            if (string.IsNullOrWhiteSpace(req.Host)||req.Port is < 1 or > 65535||req.Rack is < 0 or > 7||req.Slot is < 0 or > 31)
                return Results.BadRequest(new {message="Check host/port/rack/slot."});
            var sw=System.Diagnostics.Stopwatch.StartNew();
            try {await using var plc=new S7Client();await plc.ConnectAsync(req.Host,req.Port,req.Rack,req.Slot,Math.Clamp(req.TimeoutMs,250,30000),ct);
                // Protocol handshake only. Does not claim PLC DB read permission.
                return Results.Ok(new {success=true,message="S7 COTP + SetupCommunication OK. Add a DB tag to test reading.",responseTimeMs=sw.ElapsedMilliseconds});}
            catch(Exception e) when(e is not OperationCanceledException || !ct.IsCancellationRequested)
            {return Results.Ok(new {success=false,message=e.Message,responseTimeMs=sw.ElapsedMilliseconds});}
        });
        endpoints.MapPost("/api/devices/siemens-s7-tcp",async(S7DeviceRequest req,DeviceService devices,CancellationToken ct)=>
        {
            try {var item=await devices.CreateS7TcpAsync(req.Name,req.Host,req.Port,req.Rack,req.Slot,req.PollIntervalMs,ct);
                return Results.Created($"/api/devices/{item.Id}",item);}
            catch(DeviceCapacityExceededException){return Results.StatusCode(403);}
            catch(ArgumentException e){return Results.BadRequest(new {message=e.Message});}
        });

        endpoints.MapPost("/api/devices/modbus-rtu", async (ModbusRtuDeviceRequest req, DeviceService devices, CancellationToken ct) =>
        {
            try
            {
                var item = await devices.CreateModbusRtuAsync(req.Name, req.SerialSettings(), req.UnitId, req.PollIntervalMs, ct);
                return Results.Created($"/api/devices/{item.Id}", item);
            }
            catch (DeviceCapacityExceededException) { return Results.StatusCode(403); }
            catch (ArgumentException ex) { return Results.BadRequest(new { message = ex.Message }); }
        });

        endpoints.MapPost("/api/devices/mitsubishi-mc", async (MitsubishiMcDeviceRequest req, DeviceService devices, CancellationToken ct) =>
        {
            try
            {
                var item = await devices.CreateMitsubishiMcAsync(req.Name, req.Host, req.Port, req.Series, req.PollIntervalMs, ct);
                return Results.Created($"/api/devices/{item.Id}", item);
            }
            catch (DeviceCapacityExceededException) { return Results.StatusCode(403); }
            catch (ArgumentException ex) { return Results.BadRequest(new { message = ex.Message }); }
        });

        endpoints.MapPost("/api/devices/omron-fins", async (OmronFinsDeviceRequest req, DeviceService devices, CancellationToken ct) =>
        {
            try
            {
                var item = await devices.CreateOmronFinsAsync(req.Name, req.Host, req.Port, req.PollIntervalMs, ct);
                return Results.Created($"/api/devices/{item.Id}", item);
            }
            catch (DeviceCapacityExceededException) { return Results.StatusCode(403); }
            catch (ArgumentException ex) { return Results.BadRequest(new { message = ex.Message }); }
        });

        endpoints.MapPost("/api/devices/ethernet-ip", async (EtherNetIpDeviceRequest req, DeviceService devices, CancellationToken ct) =>
        {
            try
            {
                var item = await devices.CreateEtherNetIpAsync(req.Name, req.Host, req.Port, req.Slot, req.PollIntervalMs, ct);
                return Results.Created($"/api/devices/{item.Id}", item);
            }
            catch (DeviceCapacityExceededException) { return Results.StatusCode(403); }
            catch (ArgumentException ex) { return Results.BadRequest(new { message = ex.Message }); }
        });

        endpoints.MapPost("/api/devices/bacnet-ip", async (BacnetIpDeviceRequest req, DeviceService devices, CancellationToken ct) =>
        {
            try
            {
                var item = await devices.CreateBacnetIpAsync(req.Name, req.Host, req.Port, req.PollIntervalMs, ct);
                return Results.Created($"/api/devices/{item.Id}", item);
            }
            catch (DeviceCapacityExceededException) { return Results.StatusCode(403); }
            catch (ArgumentException ex) { return Results.BadRequest(new { message = ex.Message }); }
        });

        // Serial ports on this PC (USB RS-485 adapters show up as COM ports) for the Modbus RTU form.
        endpoints.MapGet("/api/serial-ports", () =>
            Results.Ok(System.IO.Ports.SerialPort.GetPortNames()
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name.Length).ThenBy(name => name, StringComparer.OrdinalIgnoreCase)));

        endpoints.MapPost("/api/devices/mqtt", async (MqttDeviceRequest req, DeviceService devices, IDeviceCredentialStore credentials, CancellationToken ct) =>
        {
            try
            {
                var credential = NewCredential(req.Username, req.Password);
                var item = await devices.CreateMqttAsync(req.Name, req.Host, req.Port, req.PollIntervalMs, ct);
                credentials.Save(item.Id, credential);
                return Results.Created($"/api/devices/{item.Id}", item with { Username = credential?.Username });
            }
            catch (DeviceCapacityExceededException) { return Results.StatusCode(403); }
            catch (ArgumentException ex) { return Results.BadRequest(new { message = ex.Message }); }
        });

        endpoints.MapPost("/api/devices/opc-ua", async (OpcUaDeviceRequest req, DeviceService devices, IDeviceCredentialStore credentials, CancellationToken ct) =>
        {
            try
            {
                var credential = NewCredential(req.Username, req.Password);
                var item = await devices.CreateOpcUaAsync(req.Name, req.EndpointUrl, req.PollIntervalMs, ct);
                credentials.Save(item.Id, credential);
                return Results.Created($"/api/devices/{item.Id}", item with { Username = credential?.Username });
            }
            catch (DeviceCapacityExceededException) { return Results.StatusCode(403); }
            catch (ArgumentException ex) { return Results.BadRequest(new { message = ex.Message }); }
        });

        endpoints.MapPost("/api/devices/opc-ua/certificate/inspect",
            async (OpcUaCertificateRequest request, IOpcUaCertificateApproval certificates,
                HttpContext context, LocalAccessSessionService sessions, CancellationToken ct) =>
            {
                var denied = CheckQrAdmin(context, sessions);
                if (denied is not null) return denied;
                context.Response.Headers.CacheControl = "no-store";
                try { return Results.Ok(await certificates.InspectAsync(request.EndpointUrl, ct)); }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                { return Results.Json(new { message = ex.Message }, statusCode: 409); }
            });

        endpoints.MapPost("/api/devices/opc-ua/certificate/trust",
            async (OpcUaTrustRequest request, IOpcUaCertificateApproval certificates,
                HttpContext context, LocalAccessSessionService sessions, CancellationToken ct) =>
            {
                var denied = CheckQrAdmin(context, sessions);
                if (denied is not null) return denied;
                context.Response.Headers.CacheControl = "no-store";
                try { return Results.Ok(await certificates.TrustAsync(request.EndpointUrl, request.Sha256, ct)); }
                catch (Exception ex) when (ex is not OperationCanceledException || !ct.IsCancellationRequested)
                { return Results.Json(new { message = ex.Message }, statusCode: 409); }
            });

        endpoints.MapPut(
            "/api/devices/{id:guid}",
            async (
                Guid id,
                UpdateDeviceRequest request,
                DeviceService devices,
                IDeviceCredentialStore credentials,
                CancellationToken ct) =>
            {
                try
                {
                    var existing = await devices.GetByIdAsync(id, ct);
                    if (existing is null) return Results.NotFound();
                    DeviceCredential? credential = null;
                    if (request.Username is not null)
                    {
                        if (request.Username.Trim().Length > 0 && !SupportsCredentials(existing.Protocol))
                            throw new ArgumentException($"{existing.Protocol} devices do not use a user name and password.");
                        credential = request.Username.Trim().Length == 0 ? null
                            : string.IsNullOrEmpty(request.Password)
                                ? credentials.Get(id) is { } stored
                                    ? stored with { Username = request.Username.Trim() }
                                    : throw new ArgumentException("Enter the password for this user name.")
                                : NewCredential(request.Username, request.Password);
                    }
                    var host = request.Host;
                    if (host is not null && existing.Protocol.Equals("Modbus RTU", StringComparison.OrdinalIgnoreCase))
                        host = ModbusSerialSettings.Parse(host).ToString();
                    var updated = await devices.UpdateAsync(
                        id,
                        request.Name,
                        host,
                        request.Port,
                        request.UnitId,
                        request.PollIntervalMs,
                        ct);
                    if (request.Username is not null) credentials.Save(id, credential);
                    return Results.Ok(updated with { Username = credentials.GetUsername(id) });
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
            "/api/devices/{id:guid}",
            async (
                Guid id,
                DeviceService devices,
                CancellationToken ct) =>
                await devices.DeleteAsync(id, ct)
                    ? Results.NoContent()
                    : Results.NotFound());
    }
}
