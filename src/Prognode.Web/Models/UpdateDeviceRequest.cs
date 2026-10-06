namespace Prognode.Web.Models;

public sealed record UpdateDeviceRequest(
    string? Name,
    string? Host,
    int? Port,
    int? UnitId,
    int? PollIntervalMs
);
