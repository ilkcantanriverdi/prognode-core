using Prognode.Contracts.Signals;

namespace Prognode.Protocols.Abstractions;

public interface IProtocolConnector : IAsyncDisposable
{
    string ProtocolName { get; }
    ConnectorCapabilities Capabilities { get; }

    Task ConnectAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<SignalEnvelope>> ReadAsync(
        CancellationToken cancellationToken);

    Task DisconnectAsync(CancellationToken cancellationToken);
}
