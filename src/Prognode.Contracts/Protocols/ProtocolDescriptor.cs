namespace Prognode.Contracts.Protocols;

public sealed record ProtocolDescriptor(
    string Id,
    string Name,
    string Description,
    string Availability,
    bool Enabled,
    string? Note = null
);
