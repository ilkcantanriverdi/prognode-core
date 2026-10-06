namespace Prognode.Contracts.Trends;

public sealed record TrendPoint(
    DateTimeOffset Timestamp,
    double? Value,
    string Quality
);

public sealed record TrendSeriesSnapshot(
    Guid TagId,
    IReadOnlyList<TrendPoint> Points
);
