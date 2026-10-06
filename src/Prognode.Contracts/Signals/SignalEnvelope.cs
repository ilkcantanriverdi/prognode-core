namespace Prognode.Contracts.Signals;

public enum SignalQuality
{
    Good,
    Uncertain,
    Bad,
    Stale,
    NotConnected,
    ConfigError,
    DeviceError
}

public sealed record SignalEnvelope(
    Guid DeviceId,
    Guid SignalId,
    object? Value,
    SignalQuality Quality,
    DateTimeOffset Timestamp,
    string Source,
    string? Unit = null
);
