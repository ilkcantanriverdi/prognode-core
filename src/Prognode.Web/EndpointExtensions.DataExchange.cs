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
    private static void MapDataExchangeEndpoints(IEndpointRouteBuilder endpoints)
    {
        // DATA EXCHANGE — Excel-friendly UTF-8 CSV exports used by commissioning.
        endpoints.MapGet(
            "/api/data-exchange/devices.csv",
            async (
                DeviceService devices,
                CancellationToken ct) =>
            {
                var rows = await devices.GetAllAsync(ct);
                var csv = new StringBuilder();
                csv.AppendLine("Name;Protocol;Host;Port;UnitId;PollIntervalMs;Status;CreatedAtUtc");

                foreach (var device in rows)
                {
                    csv.Append(Csv(device.Name));
                    csv.Append(';');
                    csv.Append(Csv(device.Protocol));
                    csv.Append(';');
                    csv.Append(Csv(device.Host ?? string.Empty));
                    csv.Append(';');
                    csv.Append(device.Port?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
                    csv.Append(';');
                    csv.Append(device.UnitId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
                    csv.Append(';');
                    csv.Append(device.PollIntervalMs.ToString(CultureInfo.InvariantCulture));
                    csv.Append(';');
                    csv.Append(Csv(device.Status));
                    csv.Append(';');
                    csv.Append(ExportUtcSeconds(device.CreatedAt));
                    csv.AppendLine();
                }

                return CsvDownload(csv, $"PROGNODE_Devices_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            });

        endpoints.MapGet(
            "/api/data-exchange/tags.csv",
            async (
                DeviceService devices,
                TagService tags,
                CancellationToken ct) =>
            {
                var deviceMap = (await devices.GetAllAsync(ct)).ToDictionary(x => x.Id);
                var rows = await tags.GetAllAsync(ct);
                var csv = new StringBuilder();
                csv.AppendLine("Device;Name;Address;DataType;BitIndex;ByteOrder;Unit;Offset;DecimalPlaces;Enabled");

                foreach (var tag in rows)
                {
                    var deviceName = deviceMap.TryGetValue(tag.DeviceId, out var device)
                        ? device.Name
                        : tag.DeviceId.ToString();

                    csv.Append(Csv(deviceName));
                    csv.Append(';');
                    csv.Append(Csv(tag.Name));
                    csv.Append(';');
                    csv.Append(Csv(tag.Address));
                    csv.Append(';');
                    csv.Append(Csv(tag.DataType.ToString()));
                    csv.Append(';');
                    csv.Append(tag.BitIndex?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
                    csv.Append(';');
                    csv.Append(Csv(tag.ByteOrder.ToString()));
                    csv.Append(';');
                    csv.Append(Csv(tag.Unit));
                    csv.Append(';');
                    csv.Append(tag.Offset.ToString(CultureInfo.InvariantCulture));
                    csv.Append(';');
                    csv.Append(tag.DecimalPlaces.ToString(CultureInfo.InvariantCulture));
                    csv.Append(';');
                    csv.Append(tag.Enabled ? "true" : "false");
                    csv.AppendLine();
                }

                return CsvDownload(csv, $"PROGNODE_Tags_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            });

        endpoints.MapGet(
            "/api/data-exchange/alarm-definitions.csv",
            async (
                DeviceService devices,
                TagService tags,
                AlarmService alarms,
                CancellationToken ct) =>
            {
                var deviceMap = (await devices.GetAllAsync(ct)).ToDictionary(x => x.Id);
                var tagMap = (await tags.GetAllAsync(ct)).ToDictionary(x => x.Id);
                var rows = await alarms.GetDefinitionsAsync(ct);
                var csv = new StringBuilder();
                csv.AppendLine("Device;Tag;AlarmText;Priority;BitIndex;TriggerValue;Condition;Threshold;Deadband;DelayOnMs;DelayOffMs;NotifyOnActive;NotifyOnCleared;NotificationMode;RepeatIntervalSeconds;ContinueAfterClearUntilAck;RequiresAcknowledgement;Enabled");

                foreach (var alarm in rows)
                {
                    tagMap.TryGetValue(alarm.TagId, out var tag);
                    var deviceName = tag is not null && deviceMap.TryGetValue(tag.DeviceId, out var device)
                        ? device.Name
                        : string.Empty;

                    csv.Append(Csv(deviceName));
                    csv.Append(';');
                    csv.Append(Csv(tag?.Name ?? alarm.TagId.ToString()));
                    csv.Append(';');
                    csv.Append(Csv(alarm.Text));
                    csv.Append(';');
                    csv.Append(Csv(alarm.Priority.ToString()));
                    csv.Append(';');
                    csv.Append(alarm.BitIndex?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
                    csv.Append(';');
                    csv.Append(alarm.TriggerValue ? "true" : "false");
                    csv.Append(';');
                    csv.Append(Csv(alarm.Condition.ToString()));
                    csv.Append(';');
                    csv.Append(alarm.Threshold?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
                    csv.Append(';');
                    csv.Append(alarm.Deadband.ToString(CultureInfo.InvariantCulture));
                    csv.Append(';');
                    csv.Append(alarm.DelayOnMs.ToString(CultureInfo.InvariantCulture));
                    csv.Append(';');
                    csv.Append(alarm.DelayOffMs.ToString(CultureInfo.InvariantCulture));
                    csv.Append(';');
                    csv.Append(alarm.NotifyOnActive ? "true" : "false");
                    csv.Append(';');
                    csv.Append(alarm.NotifyOnCleared ? "true" : "false");
                    csv.Append(';');
                    csv.Append(Csv(alarm.NotificationMode.ToString()));
                    csv.Append(';');
                    csv.Append(alarm.RepeatIntervalSeconds.ToString(CultureInfo.InvariantCulture));
                    csv.Append(';');
                    csv.Append(alarm.ContinueAfterClearUntilAcknowledged ? "true" : "false");
                    csv.Append(';');
                    csv.Append(alarm.RequiresAcknowledgement ? "true" : "false");
                    csv.Append(';');
                    csv.Append(alarm.Enabled ? "true" : "false");
                    csv.AppendLine();
                }

                return CsvDownload(csv, $"PROGNODE_AlarmDefinitions_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            });

        endpoints.MapGet(
            "/api/data-exchange/alarm-history.csv",
            async (
                AlarmService alarms,
                DateOnly? fromUtc,
                DateOnly? toUtc,
                CancellationToken ct) =>
            {
                if (fromUtc > toUtc) return Results.BadRequest("fromUtc must not be after toUtc");
                var rows = (await LoadAllAlarmOccurrencesAsync(alarms, ct))
                    .Where(alarm => (!fromUtc.HasValue || DateOnly.FromDateTime(alarm.ActiveAt.UtcDateTime) >= fromUtc.Value)
                        && (!toUtc.HasValue || DateOnly.FromDateTime(alarm.ActiveAt.UtcDateTime) <= toUtc.Value));
                var csv = new StringBuilder();
                csv.AppendLine("ActiveAtUtc;ClearedAtUtc;DurationSeconds;Source;AlarmText;Priority;State;SystemAlarm");

                foreach (var alarm in rows)
                {
                    var duration = (alarm.ClearedAt ?? DateTimeOffset.UtcNow) - alarm.ActiveAt;
                    csv.Append(ExportUtcSeconds(alarm.ActiveAt));
                    csv.Append(';');
                    csv.Append(alarm.ClearedAt is { } clearedAt ? ExportUtcSeconds(clearedAt) : string.Empty);
                    csv.Append(';');
                    csv.Append(Math.Max(0, Math.Round(duration.TotalSeconds)).ToString("0", CultureInfo.InvariantCulture));
                    csv.Append(';');
                    csv.Append(Csv(alarm.SourceName));
                    csv.Append(';');
                    csv.Append(Csv(alarm.Text));
                    csv.Append(';');
                    csv.Append(Csv(alarm.Priority.ToString()));
                    csv.Append(';');
                    csv.Append(Csv(alarm.State));
                    csv.Append(';');
                    csv.Append(alarm.IsSystem ? "true" : "false");
                    csv.AppendLine();
                }

                return CsvDownload(csv, $"PROGNODE_AlarmHistory_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            });

        endpoints.MapGet(
            "/api/data-exchange/alarm-history.xlsx",
            async (
                DeviceService devices,
                AlarmService alarms,
                DateOnly? fromUtc,
                DateOnly? toUtc,
                CancellationToken ct) =>
            {
                if (fromUtc > toUtc) return Results.BadRequest("fromUtc must not be after toUtc");
                var deviceMap = (await devices.GetAllAsync(ct)).ToDictionary(x => x.Id);
                var rows = (await LoadAllAlarmOccurrencesAsync(alarms, ct))
                    .Where(alarm => (!fromUtc.HasValue || DateOnly.FromDateTime(alarm.ActiveAt.UtcDateTime) >= fromUtc.Value)
                        && (!toUtc.HasValue || DateOnly.FromDateTime(alarm.ActiveAt.UtcDateTime) <= toUtc.Value));
                var headers = new[]
                {
                    "Active At UTC",
                    "Acknowledged At UTC",
                    "Cleared At UTC",
                    "Duration (s)",
                    "Device",
                    "Tag",
                    "Alarm Text",
                    "Priority",
                    "State",
                    "System Alarm"
                };

                var data = rows.Select(alarm =>
                {
                    var duration = (alarm.ClearedAt ?? DateTimeOffset.UtcNow) - alarm.ActiveAt;
                    var deviceName = alarm.IsSystem
                        ? alarm.SourceName
                        : alarm.DeviceId is Guid deviceId && deviceMap.TryGetValue(deviceId, out var device)
                            ? device.Name
                            : alarm.DeviceId?.ToString() ?? string.Empty;
                    var tagName = alarm.IsSystem ? "SYSTEM" : alarm.SourceName;

                    return (IReadOnlyList<object?>)new object?[]
                    {
                        ExportUtcSeconds(alarm.ActiveAt),
                        alarm.AcknowledgedAt is { } acknowledgedAt ? ExportUtcSeconds(acknowledgedAt) : string.Empty,
                        alarm.ClearedAt is { } clearedAt ? ExportUtcSeconds(clearedAt) : string.Empty,
                        Math.Max(0, Math.Round(duration.TotalSeconds)),
                        deviceName,
                        tagName,
                        alarm.Text,
                        alarm.Priority.ToString(),
                        alarm.State,
                        alarm.IsSystem ? "true" : "false"
                    };
                });

                return XlsxDownload(
                    "Alarm History",
                    headers,
                    data,
                    $"PROGNODE_AlarmHistory_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx");
            });

        endpoints.MapGet(
            "/api/data-exchange/historian-configurations.csv",
            async (
                DeviceService devices,
                TagService tags,
                HistorianService historian,
                CancellationToken ct) =>
            {
                var deviceMap = (await devices.GetAllAsync(ct)).ToDictionary(x => x.Id);
                var tagMap = (await tags.GetAllAsync(ct)).ToDictionary(x => x.Id);
                var rows = await historian.GetConfigurationsAsync(ct);
                var csv = new StringBuilder();
                csv.AppendLine("Device;Tag;SampleIntervalSeconds;RetentionDays;Enabled");

                foreach (var status in rows)
                {
                    var config = status.Configuration;
                    tagMap.TryGetValue(config.TagId, out var tag);
                    var deviceName = tag is not null && deviceMap.TryGetValue(tag.DeviceId, out var device)
                        ? device.Name
                        : string.Empty;

                    csv.Append(Csv(deviceName));
                    csv.Append(';');
                    csv.Append(Csv(tag?.Name ?? config.TagId.ToString()));
                    csv.Append(';');
                    csv.Append(config.SampleIntervalSeconds.ToString(CultureInfo.InvariantCulture));
                    csv.Append(';');
                    csv.Append(config.RetentionDays.ToString(CultureInfo.InvariantCulture));
                    csv.Append(';');
                    csv.Append(config.Enabled ? "true" : "false");
                    csv.AppendLine();
                }

                return CsvDownload(csv, $"PROGNODE_HistorianConfig_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            });

        endpoints.MapGet(
            "/api/data-exchange/historian/{tagId:guid}.zip",
            async (Guid tagId, HistorianService historian, TagService tags, CancellationToken ct) =>
            {
                var tag = (await tags.GetAllAsync(ct)).FirstOrDefault(x => x.Id == tagId);
                if (tag is null) return Results.NotFound();
                var status = (await historian.GetConfigurationsAsync(ct)).FirstOrDefault(x => x.Configuration.TagId == tagId);
                var from = status?.FirstSample ?? DateTimeOffset.FromUnixTimeSeconds(0);
                var to = DateTimeOffset.UtcNow.AddMinutes(1);
                var names = new Dictionary<Guid, Prognode.Contracts.Tags.TagDefinition> { [tagId] = tag };
                return await CreateHistorianArchiveDownloadAsync(historian, names, from, to,
                    $"PROGNODE_Historian_{SafeFileName(tag.Name)}_{DateTime.Now:yyyyMMdd_HHmmss}.zip", ct);
            });

        endpoints.MapGet(
            "/api/data-exchange/historian/{tagId:guid}.csv",
            async (
                Guid tagId,
                HistorianService historian,
                TagService tags,
                CancellationToken ct) =>
            {
                var tagMap = (await tags.GetAllAsync(ct)).ToDictionary(x => x.Id);
                if (!tagMap.TryGetValue(tagId, out var tag))
                    return Results.NotFound();

                var status = (await historian.GetConfigurationsAsync(ct)).FirstOrDefault(x => x.Configuration.TagId == tagId);
                var from = status?.FirstSample ?? DateTimeOffset.FromUnixTimeSeconds(0);
                var to = DateTimeOffset.UtcNow.AddMinutes(1);
                var names = new Dictionary<Guid, Prognode.Contracts.Tags.TagDefinition> { [tagId] = tag };
                return Results.Stream(stream => WriteHistorianCsvAsync(stream, historian, names, from, to, ct),
                    "text/csv; charset=utf-8", $"PROGNODE_Historian_{SafeFileName(tag.Name)}_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            });
    }
}
