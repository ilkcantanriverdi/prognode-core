using Prognode.Contracts.Signals;
using Prognode.Protocols.Abstractions;

namespace Prognode.Protocols.Mock;

public sealed class MockConnector : IProtocolConnector
{
    private readonly Guid _deviceId = Guid.NewGuid();
    private readonly Guid _temperatureId = Guid.NewGuid();

    public string ProtocolName => "Mock";

    public ConnectorCapabilities Capabilities =>
        new(
            SupportsPolling: true,
            SupportsSubscription: false,
            SupportsBrowse: false,
            SupportsWrite: false
        );

    public Task ConnectAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public Task<IReadOnlyList<SignalEnvelope>> ReadAsync(
        CancellationToken cancellationToken)
    {
        var value = Math.Round(
            20.0 + Random.Shared.NextDouble() * 10.0,
            2);

        IReadOnlyList<SignalEnvelope> result =
        [
            new(
                DeviceId: _deviceId,
                SignalId: _temperatureId,
                Value: value,
                Quality: SignalQuality.Good,
                Timestamp: DateTimeOffset.UtcNow,
                Source: "Mock",
                Unit: "°C"
            )
        ];

        return Task.FromResult(result);
    }

    public Task DisconnectAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
