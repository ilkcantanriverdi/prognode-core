using System.Collections.Concurrent;
using Prognode.Contracts.Devices;

namespace Prognode.Protocols.S7;

/// <summary>
/// One persistent S7 session per device (review Y3). An S7-1200 offers only a few connection
/// resources shared with HMI and TIA Portal; reconnecting every poll and opening a separate
/// connection for every health check exhausts them. The poll reader and the health probe share
/// the session; access is serialized per device. Any transport error closes the session and the
/// next use reconnects.
/// </summary>
public sealed class S7SessionPool : IAsyncDisposable
{
    private const int ConnectTimeoutMs = 3000;
    private readonly ConcurrentDictionary<Guid, Session> _sessions = new();

    /// <summary>Runs <paramref name="work"/> on the device's connected session.</summary>
    public async Task<T> UseAsync<T>(
        DeviceDefinition device,
        Func<S7Client, CancellationToken, Task<T>> work,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(device.Host)) throw new IOException("Siemens PLC IP missing.");
        var (rack, slot) = S7TagReader.DecodeRackSlot(device.UnitId ?? 1);
        var endpoint = $"{device.Host.Trim()}:{device.Port ?? 102}:{rack}:{slot}";
        var session = _sessions.GetOrAdd(device.Id, _ => new Session());
        await session.Gate.WaitAsync(cancellationToken);
        try
        {
            if (session.Client is null || !session.Client.IsConnected || session.Endpoint != endpoint)
            {
                await session.CloseAsync();
                var client = new S7Client();
                await client.ConnectAsync(device.Host, device.Port ?? 102, rack, slot, ConnectTimeoutMs, cancellationToken);
                session.Client = client;
                session.Endpoint = endpoint;
            }

            var result = await work(session.Client, cancellationToken);
            session.LastSuccess = DateTimeOffset.UtcNow;
            return result;
        }
        catch (S7ItemRejectedException)
        {
            // The PLC answered; the session is still in sync.
            session.LastSuccess = DateTimeOffset.UtcNow;
            throw;
        }
        catch
        {
            session.LastFailure = DateTimeOffset.UtcNow;
            await session.CloseAsync();
            throw;
        }
        finally
        {
            session.Gate.Release();
        }
    }

    public bool HasRecentSuccess(Guid deviceId, TimeSpan window) =>
        _sessions.TryGetValue(deviceId, out var session) &&
        session.LastSuccess is { } last && DateTimeOffset.UtcNow - last <= window &&
        (session.LastFailure is null || session.LastFailure < last);

    /// <summary>Closes sessions of devices that no longer exist.</summary>
    public void PruneExcept(IReadOnlySet<Guid> deviceIds)
    {
        foreach (var id in _sessions.Keys.Where(x => !deviceIds.Contains(x)).ToArray())
            if (_sessions.TryRemove(id, out var session))
                _ = session.CloseAsync();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var session in _sessions.Values) await session.CloseAsync();
        _sessions.Clear();
    }

    private sealed class Session
    {
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public S7Client? Client { get; set; }
        public string? Endpoint { get; set; }
        public DateTimeOffset? LastSuccess { get; set; }
        public DateTimeOffset? LastFailure { get; set; }

        public async ValueTask CloseAsync()
        {
            if (Client is not null) await Client.DisposeAsync();
            Client = null;
        }
    }
}
