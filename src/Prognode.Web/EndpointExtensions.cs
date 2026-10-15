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
    public sealed record S7TestRequest(string Host,int Port,int Rack,int Slot,int TimeoutMs=2000);
    public sealed record S7DeviceRequest(string Name,string Host,int Port,int Rack,int Slot,int PollIntervalMs);
    public sealed record ModbusRtuDeviceRequest(string? Name, string? SerialPort, int BaudRate, int DataBits, string? Parity, int StopBits, int UnitId, int PollIntervalMs)
    {
        /// <summary>Canonical serial settings string stored in the device Host: COM3;9600;8E1.</summary>
        public string SerialSettings() => ModbusSerialSettings.Parse(
            $"{(SerialPort ?? "").Trim()};{BaudRate};{DataBits}{(Parity ?? "N").Trim().ToUpperInvariant().FirstOrDefault('N')}{StopBits}").ToString();
    }
    public sealed record MitsubishiMcDeviceRequest(string? Name, string? Host, int Port, int Series, int PollIntervalMs);
    public sealed record OmronFinsDeviceRequest(string? Name, string? Host, int Port, int PollIntervalMs);
    public sealed record EtherNetIpDeviceRequest(string? Name, string? Host, int Port, int Slot, int PollIntervalMs);
    public sealed record BacnetIpDeviceRequest(string? Name, string? Host, int Port, int PollIntervalMs);
    public sealed record MqttDeviceRequest(string? Name, string? Host, int Port, int PollIntervalMs, string? Username = null, string? Password = null);
    public sealed record OpcUaDeviceRequest(string? Name, string? EndpointUrl, int PollIntervalMs, string? Username = null, string? Password = null);
    public sealed record OpcUaCertificateRequest(string EndpointUrl);
    public sealed record OpcUaTrustRequest(string EndpointUrl, string Sha256);
    public sealed record DeleteAlarmHistoryRequest(Guid[]? OccurrenceIds);
    private static string ResolveMobileAckActor(MobileAckAuditEntry attribution, ServerAccessService server,
        LicenseService? license = null)
    {
        if(!string.IsNullOrWhiteSpace(attribution.ActorDisplayName))
            return attribution.ActorDisplayName.Trim();
        if(attribution.Principal.StartsWith("USER:",StringComparison.OrdinalIgnoreCase))
        {
            var user=attribution.Principal.Substring("USER:".Length).Trim();
            var current=license?.Current;
            if(current is not null && !string.IsNullOrWhiteSpace(current.AssignedUserName) &&
                string.Equals(current.AssignedUserEmail,user,StringComparison.OrdinalIgnoreCase))
                return current.AssignedUserName;
            return string.IsNullOrWhiteSpace(user)?"User":user;
        }
        if(attribution.DeviceId is Guid deviceId)
            return server.GetClient(deviceId)?.Name ?? $"Device {deviceId.ToString("N")[..8]}";
        return attribution.Principal;
    }
    private static bool SupportsCredentials(string protocol) =>
        protocol.Equals("MQTT", StringComparison.OrdinalIgnoreCase) ||
        protocol.Equals("OPC UA", StringComparison.OrdinalIgnoreCase);

    /// <summary>A credential from a create request; no user name means none.</summary>
    private static DeviceCredential? NewCredential(string? username, string? password)
    {
        if (string.IsNullOrWhiteSpace(username)) return null;
        if (string.IsNullOrEmpty(password)) throw new ArgumentException("Enter the password for this user name.");
        if (username.Trim().Length > 256 || password.Length > 1024)
            throw new ArgumentException("User name or password is too long.");
        return new DeviceCredential(username.Trim(), password);
    }

    private static IResult? CheckQrAdmin(HttpContext context, LocalAccessSessionService sessions)
    {
        var ip = context.Connection.RemoteIpAddress;
        if (ip is null || !System.Net.IPAddress.IsLoopback(ip))
            return Results.Json(new { code = "LOCAL_ADMIN_ONLY", message = "QR creation is allowed only on the PROGNODE Monitor computer." }, statusCode: 403);
        var token = context.Request.Headers["X-PROGNODE-Session"].ToString();
        var session = sessions.GetStatus(token);
        if (!session.Authenticated)
            return Results.Json(new { code = "SIGN_IN_REQUIRED", message = "Sign in on the PROGNODE Monitor computer." }, statusCode: 401);
        if (session.PortalRole is not ("OWNER" or "ORGANIZATION_ADMIN"))
            return Results.Json(new { code = "ADMIN_REQUIRED", message = "A licensed administrator must authorize QR pairing." }, statusCode: 403);
        return null;
    }

    private static PairedClientSnapshot? GetMobileBearer(HttpContext context,ServerAccessService server)
    {
        var auth=context.Request.Headers.Authorization.ToString();
        var raw=auth.StartsWith("Bearer ",StringComparison.OrdinalIgnoreCase)
            ? auth[7..].Trim():string.Empty;
        return server.TryValidateToken(raw,out var paired) ? paired : null;
    }

    public sealed record DeviceAccessUpdateRequest(string? DisplayName,bool CanViewAlarms,
        bool CanAcknowledge,string AckAuthMode);
    public sealed record NotificationDeliveryUpdateRequest(bool Enabled);

    public static IEndpointRouteBuilder MapPrognodeEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        MapPairingEndpoints(endpoints);
        MapLicenseEndpoints(endpoints);
        MapRemoteAccessEndpoints(endpoints);
        MapDevicesEndpoints(endpoints);
        MapTagsEndpoints(endpoints);
        MapBatchesEndpoints(endpoints);
        MapAlarmsEndpoints(endpoints);
        MapTrendsEndpoints(endpoints);
        MapHistorianEndpoints(endpoints);
        MapDataExchangeEndpoints(endpoints);
        MapSystemEndpoints(endpoints);
        MapMobileEndpoints(endpoints);

        return endpoints;
    }

    private static object SignedOutLicenseSummary(LicenseSnapshot current)
    {
        var installed = current.LicenseId is not ("NONE" or "INVALID");
        return new
        {
            licenseInstalled = installed,
            isValid = false,
            status = installed ? "SIGN_IN_REQUIRED" : current.Status,
            licenseId = installed ? "INSTALLED" : current.LicenseId,
            customer = (string?)null,
            plan = (string?)null,
            expiresAt = (DateTimeOffset?)null,
            graceUntil = (DateTimeOffset?)null,
            assignedUserId = (string?)null,
            assignedUserName = (string?)null,
            assignedUserEmail = (string?)null,
            portalRole = (string?)null,
            billingPeriod = (string?)null,
            validFrom = (DateTimeOffset?)null,
            pricingVersion = (string?)null,
            entitlements = new
            {
                modules = Array.Empty<string>(),
                remoteAccessEnabled = false,
                remoteAccessUnlimited = false,
                maxRemoteClients = (int?)null,
                remoteAccessExpiresAtUtc = (DateTimeOffset?)null
            }
        };
    }

    public sealed record MobileReceiptRequest(long EventId,string State);
    public sealed record MobilePushTokenRequest(string Platform,string PushToken);

    private static bool MobileReaderAllowed(HttpContext context,LocalAccessSessionService sessions,
        ServerAccessService server)
    {
        var paired=GetMobileBearer(context,server);
        if(paired is not null) return paired.CanViewAlarms && context.Request.IsHttps;
        return IsLoopbackRequest(context) &&
            sessions.Validate(context.Request.Headers["X-PROGNODE-Session"].ToString());
    }

    private static bool IsLoopbackRequest(HttpContext context)
    {
        var remote = context.Connection.RemoteIpAddress;
        return remote is not null && System.Net.IPAddress.IsLoopback(remote);
    }

    private static bool TryGetPairedClientId(HttpContext context, out Guid clientId)
    {
        if (context.Items.TryGetValue("PROGNODE_CLIENT_ID", out var value) && value is Guid id)
        {
            clientId = id;
            return true;
        }

        clientId = Guid.Empty;
        return false;
    }

    private static IResult RemoteOperationResult(RemoteAccessOperationResult result)
    {
        if (result.Success)
            return Results.Ok(result);

        var statusCode = result.Code switch
        {
            "LOCAL_LICENSE_INVALID" or "REMOTE_ACCESS_NOT_ENTITLED" or "USER_NOT_AUTHORIZED" => StatusCodes.Status403Forbidden,
            "CLOUD_NOT_CONFIGURED" or "CLOUD_UNAVAILABLE" => StatusCodes.Status503ServiceUnavailable,
            "CLIENT_NOT_FOUND" => StatusCodes.Status404NotFound,
            "DEVICE_IDENTITY_REQUIRED" => StatusCodes.Status400BadRequest,
            "REMOTE_CLIENT_LIMIT_REACHED" or "SERVER_NOT_BOUND" => StatusCodes.Status409Conflict,
            _ => StatusCodes.Status400BadRequest
        };

        return Results.Json(result, statusCode: statusCode);
    }

    private static DateTimeOffset? TryParseDate(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal,
            out var parsed)
                ? parsed.ToUniversalTime()
                : null;
    }

    private static async Task<IReadOnlyList<Prognode.Contracts.Alarms.AlarmOccurrenceRecord>>
        LoadAllAlarmOccurrencesAsync(AlarmService alarms, CancellationToken ct)
    {
        const int pageSize = 100;
        var rows = new List<Prognode.Contracts.Alarms.AlarmOccurrenceRecord>();
        while (true)
        {
            var page = await alarms.GetHistoryPageAsync(rows.Count, pageSize,
                cancellationToken: ct);
            if (page.Count == 0) break;
            rows.AddRange(page);
            if (page.Count < pageSize) break;
        }
        return rows;
    }

    private static IResult XlsxDownload(
        string sheetName,
        IReadOnlyList<string> headers,
        IEnumerable<IReadOnlyList<object?>> rows,
        string fileName)
    {
        using var output = new MemoryStream();
        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteZipText(archive, "[Content_Types].xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                "<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>" +
                "<Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>" +
                "</Types>");

            WriteZipText(archive, "_rels/.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                "</Relationships>");

            WriteZipText(archive, "xl/workbook.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                "<sheets><sheet name=\"" + XmlEscape(sheetName) + "\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");

            WriteZipText(archive, "xl/_rels/workbook.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/>" +
                "</Relationships>");

            var sheet = new StringBuilder();
            sheet.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sheet.Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">");
            sheet.Append("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
            sheet.Append("<cols>");
            sheet.Append("<col min=\"1\" max=\"3\" width=\"26\" customWidth=\"1\"/>");
            sheet.Append("<col min=\"4\" max=\"4\" width=\"14\" customWidth=\"1\"/>");
            sheet.Append("<col min=\"5\" max=\"6\" width=\"24\" customWidth=\"1\"/>");
            sheet.Append("<col min=\"7\" max=\"7\" width=\"42\" customWidth=\"1\"/>");
            sheet.Append("<col min=\"8\" max=\"10\" width=\"16\" customWidth=\"1\"/>");
            sheet.Append("</cols><sheetData>");
            AppendXlsxRow(sheet, headers.Cast<object?>());
            foreach (var row in rows) AppendXlsxRow(sheet, row);
            sheet.Append("</sheetData></worksheet>");
            WriteZipText(archive, "xl/worksheets/sheet1.xml", sheet.ToString());
        }

        return Results.File(
            output.ToArray(),
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            fileName);
    }

    private static void AppendXlsxRow(StringBuilder sheet, IEnumerable<object?> values)
    {
        sheet.Append("<row>");
        foreach (var value in values)
        {
            if (value is double or float or decimal or int or long or short)
            {
                var numeric = Convert.ToString(value, CultureInfo.InvariantCulture) ?? "0";
                sheet.Append("<c t=\"n\"><v>").Append(numeric).Append("</v></c>");
                continue;
            }

            sheet.Append("<c t=\"inlineStr\"><is><t xml:space=\"preserve\">")
                .Append(XmlEscape(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty))
                .Append("</t></is></c>");
        }
        sheet.Append("</row>");
    }

    private static void WriteZipText(ZipArchive archive, string path, string content)
    {
        var entry = archive.CreateEntry(path, CompressionLevel.Fastest);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private static string XmlEscape(string value) => SecurityElement.Escape(value) ?? string.Empty;

    private static string SafeFileName(string name) =>
        string.Concat(name.Select(c => char.IsLetterOrDigit(c) ? c : '_'));

    private static async Task WriteHistorianCsvAsync(
        Stream output, HistorianService historian, IReadOnlyDictionary<Guid, string> names,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        await using var writer = new StreamWriter(output, new UTF8Encoding(true), 8192, leaveOpen: true);
        await writer.WriteLineAsync("Timestamp UTC;Tag;Value;Quality");
        for (var cursor = from; cursor < to;)
        {
            var monthEnd = new DateTimeOffset(cursor.Year, cursor.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1);
            var next = monthEnd < to ? monthEnd : to;
            await foreach (var sample in historian.StreamRawAsync(names.Keys.ToArray(), cursor, next, ct))
                await writer.WriteLineAsync(HistorianCsvRow(sample, names));
            await writer.FlushAsync(ct);
            cursor = next;
        }
    }

    private static string HistorianCsvRow(
        Prognode.Contracts.Historian.HistorianSample sample, IReadOnlyDictionary<Guid, string> names)
    {
        var timestamp = ExportUtcSeconds(DateTimeOffset.FromUnixTimeMilliseconds(sample.TimestampUnixMs));
        var tagName = names.TryGetValue(sample.TagId, out var name) ? name : sample.TagId.ToString();
        return $"{timestamp};{Csv(tagName)};{sample.Value?.ToString(CultureInfo.InvariantCulture) ?? string.Empty};{Csv(sample.Quality)}";
    }

    private static async Task<IResult> CreateHistorianArchiveDownloadAsync(
        HistorianService historian, IReadOnlyDictionary<Guid, string> names,
        DateTimeOffset from, DateTimeOffset to, string fileName, CancellationToken ct)
    {
        // ZipArchive writes its final central directory synchronously when disposed.
        // Build it on an async-capable temporary file, then let ASP.NET copy that file
        // asynchronously to the HTTP response. DeleteOnClose removes it after delivery.
        var path = Path.Combine(Path.GetTempPath(), $"prognode-historian-{Guid.NewGuid():N}.zip");
        var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read,
            81920, FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.DeleteOnClose);
        try
        {
            await WriteHistorianArchiveAsync(file, historian, names, from, to, ct);
            await file.FlushAsync(ct);
            file.Position = 0;
            return Results.File(file, "application/zip", fileName);
        }
        catch
        {
            await file.DisposeAsync();
            throw;
        }
    }

    private static async Task WriteHistorianArchiveAsync(
        Stream output, HistorianService historian, IReadOnlyDictionary<Guid, string> names,
        DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        const int rowsPerFile = 500_000; // Safely below Excel's worksheet row limit.
        using var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        StreamWriter? writer = null;
        var part = 0;
        var rows = 0;
        try
        {
            // Monthly slices release SQLite read transactions instead of pinning the WAL
            // for an entire multi-year download. Each CSV part is streamed, never buffered.
            for (var cursor = from; cursor < to;)
            {
                var monthEnd = new DateTimeOffset(cursor.Year, cursor.Month, 1, 0, 0, 0, TimeSpan.Zero).AddMonths(1);
                var next = monthEnd < to ? monthEnd : to;
                await foreach (var sample in historian.StreamRawAsync(names.Keys.ToArray(), cursor, next, ct))
                {
                    if (writer is null || rows >= rowsPerFile)
                    {
                        if (writer is not null) await writer.DisposeAsync();
                        writer = new StreamWriter(archive.CreateEntry($"historian_part_{++part:D4}.csv", CompressionLevel.Fastest).Open(),
                            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                        await writer.WriteLineAsync("Timestamp UTC;Tag;Value;Quality");
                        rows = 0;
                    }
                    await writer.WriteLineAsync(HistorianCsvRow(sample, names));
                    rows++;
                }
                cursor = next;
            }
            if (writer is null)
            {
                await using var empty = new StreamWriter(archive.CreateEntry("historian_part_0001.csv").Open(), new UTF8Encoding(true));
                await empty.WriteLineAsync("Timestamp UTC;Tag;Value;Quality");
            }
        }
        finally { if (writer is not null) await writer.DisposeAsync(); }
    }

    private static IResult CsvDownload(
        StringBuilder csv,
        string fileName)
    {
        var preamble = Encoding.UTF8.GetPreamble();
        // UTF-8 BOM, no "sep=" line: Excel ignores the BOM when that line is present and breaks non-ASCII text.
        var normalized = System.Text.RegularExpressions.Regex.Replace(csv.ToString(), @"^sep=[^\r\n]*\r?\n", "");
        var content = Encoding.UTF8.GetBytes(normalized);
        var bytes = new byte[preamble.Length + content.Length];
        Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
        Buffer.BlockCopy(content, 0, bytes, preamble.Length, content.Length);

        return Results.File(
            bytes,
            "text/csv; charset=utf-8",
            fileName);
    }

    private static string ExportUtcSeconds(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static string? CoreUserActor(LocalAccessSessionService sessions, HttpContext context)
    {
        var session = sessions.GetStatus(context.Request.Headers["X-PROGNODE-Session"].ToString());
        if (!session.Authenticated) return null;
        return string.IsNullOrWhiteSpace(session.UserName) ? session.UserEmail : session.UserName;
    }

    private static string Csv(string value) =>
        "\"" + value.Replace("\"", "\"\"") + "\"";
}


public sealed record QrCreateRequest(string? SelectedHost);
public sealed record QrPairClientRequest(Guid ServerId, string PairingTicket,
    string ClientName, string? Platform, string? DevicePublicKey);

public sealed record PairClientRequest(
    Guid ServerId,
    string ClientName,
    string PairingCode,
    string? Platform = null,
    string? DevicePublicKey = null);

public sealed record RemoteClientRegistrationRequest(
    Guid? ClientId,
    string? DevicePublicKey,
    string? Platform,
    Guid? ChallengeId = null,
    string? DeviceSignature = null);

public sealed record LanProbeRequest(int InterfaceIndex);
public sealed record LanAgentClaimRequest(string RequestId);
