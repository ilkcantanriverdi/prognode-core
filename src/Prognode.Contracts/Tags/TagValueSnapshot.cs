namespace Prognode.Contracts.Tags;

public enum TagQuality
{
    Good,
    Uncertain,
    Bad,
    Stale,
    NotConnected,
    ConfigError,
    DeviceError
}

public sealed record TagValueSnapshot(
    Guid TagId,
    Guid DeviceId,
    double? RawValue,
    double? Value,
    TagQuality Quality,
    DateTimeOffset Timestamp,
    string Source,
    string? Error = null
);
