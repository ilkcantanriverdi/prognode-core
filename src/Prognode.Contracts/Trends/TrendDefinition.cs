namespace Prognode.Contracts.Trends;

public sealed record TrendDefinition(
    Guid Id,
    string Name,
    IReadOnlyList<Guid> TagIds,
    IReadOnlyList<string> Colors,
    int DefaultWindowMinutes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);
