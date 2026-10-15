using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.Sockets;

namespace Prognode.Protocols.Mitsubishi;

/// <summary>The PLC answered with a non-zero MC end code (e.g. 0xC056 device out of range). The connection stays valid.</summary>
public sealed class McEndCodeException(ushort endCode)
    : IOException($"Mitsubishi PLC refused the read (end code 0x{endCode:X4}{Hint(endCode)}).")
{
    public ushort EndCode { get; } = endCode;

    private static string Hint(ushort code) => code switch
    {
        0xC050 or 0xC051 or 0xC052 or 0xC053 or 0xC054 => ": request format not accepted — check the PLC's MC protocol (3E, binary) settings",
        0xC056 => ": device number out of range for this PLC",
        0xC059 => ": command not supported by this PLC",
        0xC05C or 0xC061 => ": request content not accepted",
        0xC05B => ": device not accessible on this PLC",
        _ => "",
    };
}

/// <summary>
/// Mitsubishi MC protocol client (SLMP-compatible 3E frame, binary code) for Q, L, iQ-R and iQ-F
/// (FX5) PLCs over TCP: batch read of word and bit devices (command 0x0401). One persistent
/// connection per endpoint; requests on an endpoint are serialized. Read-only.
/// </summary>
public sealed class McClient : IAsyncDisposable
{
    public const int MaxWordPoints = 960;
    public const int MaxBitPoints = 3584;
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(2);
    private readonly ConcurrentDictionary<string, Connection> _connections = new(StringComparer.OrdinalIgnoreCase);

    public async Task<ushort[]> ReadWordsAsync(string host, int port, byte deviceCode, int start, int count, int timeoutMs, CancellationToken ct)
    {
        if (count is < 1 or > MaxWordPoints) throw new ArgumentOutOfRangeException(nameof(count));
        var data = await ExecuteAsync(host, port, deviceCode, start, count, bitUnits: false, timeoutMs, ct);
        if (data.Length != count * 2) throw new IOException("Mitsubishi word read returned an unexpected length.");
        var words = new ushort[count];
        for (var i = 0; i < count; i++) words[i] = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(i * 2));
        return words;
    }

    public async Task<bool[]> ReadBitsAsync(string host, int port, byte deviceCode, int start, int count, int timeoutMs, CancellationToken ct)
    {
        if (count is < 1 or > MaxBitPoints) throw new ArgumentOutOfRangeException(nameof(count));
        var data = await ExecuteAsync(host, port, deviceCode, start, count, bitUnits: true, timeoutMs, ct);
        if (data.Length != (count + 1) / 2) throw new IOException("Mitsubishi bit read returned an unexpected length.");
        // Bit units: two points per byte, the first in the high nibble.
        var bits = new bool[count];
        for (var i = 0; i < count; i++) bits[i] = (i % 2 == 0 ? data[i / 2] >> 4 : data[i / 2] & 0x0F) != 0;
        return bits;
    }

    public bool HasRecentSuccess(string host, int port, TimeSpan window) =>
        _connections.TryGetValue(Key(host, port), out var c) && c.LastSuccess is { } last &&
        DateTimeOffset.UtcNow - last <= window && (c.LastFailure is null || c.LastFailure < last);

    /// <summary>Builds the 3E binary batch-read request: subheader 50 00, network 0, PC FF, I/O 03FF, station 0.</summary>
    public static byte[] BuildRequest(byte deviceCode, int start, int count, bool bitUnits, int timeoutMs)
    {
        var timer = (ushort)Math.Clamp(timeoutMs / 250, 1, 0xFFFF); // monitoring timer in 250 ms units
        byte[] frame =
        [
            0x50, 0x00, 0x00, 0xFF, 0xFF, 0x03, 0x00,
            12, 0,                                              // request data length (timer .. points)
            (byte)timer, (byte)(timer >> 8),
            0x01, 0x04,                                         // command 0x0401 batch read
            (byte)(bitUnits ? 1 : 0), 0x00,                     // subcommand 0x0000 words / 0x0001 bits
            (byte)start, (byte)(start >> 8), (byte)(start >> 16),
            deviceCode,
            (byte)count, (byte)(count >> 8),
        ];
        return frame;
    }

    private async Task<byte[]> ExecuteAsync(string host, int port, byte deviceCode, int start, int count, bool bitUnits, int timeoutMs, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("Mitsubishi PLC IP is required.", nameof(host));
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        if (start is < 0 or > 0xFFFFFF || (long)start + count - 1 > 0xFFFFFF) throw new ArgumentOutOfRangeException(nameof(start));
        CloseIdle();
        var connection = _connections.GetOrAdd(Key(host, port), _ => new Connection());
        await connection.Gate.WaitAsync(ct);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(timeoutMs);
            var request = BuildRequest(deviceCode, start, count, bitUnits, timeoutMs);
            for (var attempt = 0; ; attempt++)
            {
                var reused = connection.Stream is not null;
                try
                {
                    connection.LastUsed = DateTimeOffset.UtcNow;
                    if (connection.Stream is null) await connection.ConnectAsync(host, port, timeout.Token);
                    await connection.Stream!.WriteAsync(request, timeout.Token);
                    // Subheader D0 00, network, PC, I/O (2), station, response data length (2); the
                    // length counts the end code and the data that follow.
                    var header = new byte[9];
                    await connection.Stream.ReadExactlyAsync(header, timeout.Token);
                    if (header[0] != 0xD0 || header[1] != 0x00)
                        throw new IOException($"Mitsubishi answer has subheader {header[0]:X2} {header[1]:X2}; the PLC port may use ASCII code or another frame (set binary 3E / SLMP).");
                    var length = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(7));
                    if (length < 2 || length > 2 + MaxWordPoints * 2 + 32) throw new IOException("Mitsubishi answer has an invalid length.");
                    var body = new byte[length];
                    await connection.Stream.ReadExactlyAsync(body, timeout.Token);
                    var endCode = BinaryPrimitives.ReadUInt16LittleEndian(body);
                    connection.MarkSuccess();
                    if (endCode != 0) throw new McEndCodeException(endCode);
                    return body[2..];
                }
                catch (McEndCodeException) { throw; }
                catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException && !ct.IsCancellationRequested)
                {
                    connection.Close();
                    connection.MarkFailure();
                    // A reused socket the PLC already closed (idle timeout) is retried once on a fresh one.
                    if (reused && attempt == 0 && ex is not OperationCanceledException) continue;
                    if (ex is OperationCanceledException)
                        throw new IOException($"Mitsubishi PLC {host}:{port} did not answer within {timeoutMs} ms.", ex);
                    throw new IOException($"Mitsubishi PLC {host}:{port}: {ex.Message}", ex);
                }
            }
        }
        finally { connection.Gate.Release(); }
    }

    private static string Key(string host, int port) => $"{host.Trim()}:{port}";

    private void CloseIdle()
    {
        foreach (var (key, c) in _connections)
            if (DateTimeOffset.UtcNow - c.LastUsed > IdleTimeout && c.Gate.Wait(0))
            {
                try { c.Close(); _connections.TryRemove(key, out _); }
                finally { c.Gate.Release(); }
            }
    }

    public ValueTask DisposeAsync()
    {
        foreach (var (_, c) in _connections) c.Close();
        _connections.Clear();
        return ValueTask.CompletedTask;
    }

    private sealed class Connection
    {
        private TcpClient? _client;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public NetworkStream? Stream { get; private set; }
        public DateTimeOffset LastUsed { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? LastSuccess { get; private set; }
        public DateTimeOffset? LastFailure { get; private set; }

        public async Task ConnectAsync(string host, int port, CancellationToken ct)
        {
            Close();
            _client = new TcpClient { NoDelay = true };
            await _client.ConnectAsync(host, port, ct);
            Stream = _client.GetStream();
        }

        public void MarkSuccess() => LastSuccess = DateTimeOffset.UtcNow;
        public void MarkFailure() => LastFailure = DateTimeOffset.UtcNow;

        public void Close()
        {
            Stream?.Dispose();
            _client?.Dispose();
            Stream = null;
            _client = null;
        }
    }
}
