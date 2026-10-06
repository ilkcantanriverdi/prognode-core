using Prognode.Contracts.Trends;

namespace Prognode.Contracts.Historian;

public sealed record HistorianRecordingConfiguration(
    Guid Id,
    Guid TagId,
    int SampleIntervalSeconds,
    int RetentionDays,
    bool Enabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public sealed record HistorianSample(
    Guid TagId,
    long TimestampUnixMs,
    double? Value,
    string Quality,
    Guid? BatchId = null
);

public sealed record HistorianSeriesResult(
    Guid TagId,
    IReadOnlyList<TrendPoint> Points,
    long SampleCount,
    double? Minimum,
    double? Maximum,
    double? Average
);

public sealed record HistorianTagStatus(
    HistorianRecordingConfiguration Configuration,
    long SampleCount,
    DateTimeOffset? FirstSample,
    DateTimeOffset? LastSample,
    double? LastValue,
    string? LastQuality
);

public sealed record HistorianStats(
    long TotalSamples,
    int RecordedTags,
    long DatabaseBytes,
    DateTimeOffset? OldestSample,
    DateTimeOffset? NewestSample
);
