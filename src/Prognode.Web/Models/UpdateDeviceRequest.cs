namespace Prognode.Web.Models;

public sealed record UpdateDeviceRequest(
    string? Name,
    string? Host,
    int? Port,
    int? UnitId,
    int? PollIntervalMs,
    // null: keep the credential; "": remove it; a name: save it (an empty Password keeps the stored one).
    string? Username = null,
    string? Password = null
);
