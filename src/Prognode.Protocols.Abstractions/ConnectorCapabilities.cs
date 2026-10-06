namespace Prognode.Protocols.Abstractions;

public sealed record ConnectorCapabilities(
    bool SupportsPolling,
    bool SupportsSubscription,
    bool SupportsBrowse,
    bool SupportsWrite
);
