namespace Prognode.Web.Models;

public sealed record CreateOrUpdateTrendRequest(
    string? Name,
    IReadOnlyList<Guid>? TagIds,
    IReadOnlyList<string>? Colors,
    int DefaultWindowMinutes
);
