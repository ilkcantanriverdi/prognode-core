using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.Sockets;

namespace Prognode.Protocols.Omron;

/// <summary>The PLC answered with a non-zero FINS end code (e.g. 0x1103 address out of range). The connection stays valid.</summary>
public sealed class FinsEndCodeException(ushort endCode)
    : IOException($"Omron PLC refused the read (end code 0x{endCode:X4}{Hint(endCode)}).")
{
    public ushort EndCode { get; } = endCode;

    private static string Hint(ushort code) => code switch
    {
        0x0401 => ": command not supported by this PLC",
        0x1101 => ": memory area not present on this PLC",
        0x1103 or 0x1104 => ": address out of range for this PLC",
        0x2002 => ": memory area is protected",
        0x2201 or 0x2202 => ": not possible in the PLC's current mode",
        _ => "",
    };
}

/// <summary>
/// Omron FINS/TCP client (CS, CJ, CP, NJ and NX CPUs or Ethernet units, port 9600): memory area
/// read (command 0101) of words and bit flags. Node addresses come from the FINS/TCP handshake.
/// One persistent connection per endpoint; requests on an endpoint are serialized. Read-only.
/// </summary>
public sealed class FinsClient : IAsyncDisposable
{
    public const int MaxWords = 500;
    private const int TcpHeader = 16;
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(2);
    private readonly ConcurrentDictionary<string, Connection> _connections = new(StringComparer.OrdinalIgnoreCase);

    public async Task<ushort[]> ReadWordsAsync(string host, int port, byte areaCode, int word, int count, int timeoutMs, CancellationToken ct)
    {
        if (count is < 1 or > MaxWords) throw new ArgumentOutOfRangeException(nameof(count));
        var data = await ExecuteAsync(host, port, areaCode, word, count, timeoutMs, ct);
        if (data.Length != count * 2) throw new IOException("Omron word read returned an unexpected length.");
        var words = new ushort[count];
        for (var i = 0; i < count; i++) words[i] = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(i * 2));
        return words;
    }

    /// <summary>Bit-area read (e.g. timer/counter completion flags, area 0x09): one byte per point.</summary>
    public async Task<bool[]> ReadFlagsAsync(string host, int port, byte areaCode, int word, int count, int timeoutMs, CancellationToken ct)
    {
        if (count is < 1 or > MaxWords) throw new ArgumentOutOfRangeException(nameof(count));
        var data = await ExecuteAsync(host, port, areaCode, word, count, timeoutMs, ct);
        if (data.Length != count) throw new IOException("Omron flag read returned an unexpected length.");
        return data.Select(b => (b & 1) != 0).ToArray();
    }

    public bool HasRecentSuccess(string host, int port, TimeSpan window) =>
        _connections.TryGetValue(Key(host, port), out var c) && c.LastSuccess is { } last &&
        DateTimeOffset.UtcNow - last <= window && (c.LastFailure is null || c.LastFailure < last);

    /// <summary>Client node-address request (FINS/TCP command 0); node 0 lets the PLC assign one.</summary>
    public static byte[] BuildHandshake() => [.. "FINS"u8, 0, 0, 0, 12, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    /// <summary>
    /// FINS/TCP frame (command 2) carrying a memory area read: ICF 80 (command, response wanted),
    /// GCT 02, local network 0, destination unit 0 (CPU), then area, word (big-endian), bit 00, count.
    /// </summary>
    public static byte[] BuildRead(byte serverNode, byte clientNode, byte sid, byte areaCode, int word, int count)
    {
        byte[] fins =
        [
            0x80, 0x00, 0x02, 0x00, serverNode, 0x00, 0x00, clientNode, 0x00, sid,
            0x01, 0x01,
            areaCode, (byte)(word >> 8), (byte)word, 0x00, (byte)(count >> 8), (byte)count,
        ];
        var length = 8 + fins.Length;
        return [.. "FINS"u8, (byte)(length >> 24), (byte)(length >> 16), (byte)(length >> 8), (byte)length, 0, 0, 0, 2, 0, 0, 0, 0, .. fins];
    }

    private async Task<byte[]> ExecuteAsync(string host, int port, byte areaCode, int word, int count, int timeoutMs, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("Omron PLC IP is required.", nameof(host));
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        if (word is < 0 or > 0xFFFF || word + count - 1 > 0xFFFF) throw new ArgumentOutOfRangeException(nameof(word));
        CloseIdle();
        var connection = _connections.GetOrAdd(Key(host, port), _ => new Connection());
        await connection.Gate.WaitAsync(ct);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(timeoutMs);
            for (var attempt = 0; ; attempt++)
            {
                var reused = connection.Stream is not null;
                try
                {
                    connection.LastUsed = DateTimeOffset.UtcNow;
                    if (connection.Stream is null) await connection.ConnectAsync(host, port, timeout.Token);
                    var sid = connection.NextSid();
                    await connection.Stream!.WriteAsync(BuildRead(connection.ServerNode, connection.ClientNode, sid, areaCode, word, count), timeout.Token);
                    var body = await ReadFrameAsync(connection.Stream, expectedCommand: 2, timeout.Token);
                    // FINS response header (10) + MRC SRC (2) + end code (2) + data.
                    if (body.Length < 14 || (body[0] & 0x40) == 0 || body[9] != sid || body[10] != 0x01 || body[11] != 0x01)
                        throw new IOException("Omron answer does not match the request.");
                    // Bit 7 of MRES (relay error) and bits 6-7 of SRES (CPU error flags) are status, not the result.
                    var endCode = (ushort)((body[12] & 0x7F) << 8 | body[13] & 0x3F);
                    connection.MarkSuccess();
                    if (endCode != 0) throw new FinsEndCodeException(endCode);
                    return body[14..];
                }
                catch (FinsEndCodeException) { throw; }
                catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException && !ct.IsCancellationRequested)
                {
                    connection.Close();
                    connection.MarkFailure();
                    // A reused socket the PLC already closed (idle timeout) is retried once on a fresh one.
                    if (reused && attempt == 0 && ex is not OperationCanceledException) continue;
                    if (ex is OperationCanceledException)
                        throw new IOException($"Omron PLC {host}:{port} did not answer within {timeoutMs} ms.", ex);
                    throw new IOException($"Omron PLC {host}:{port}: {ex.Message}", ex);
                }
            }
        }
        finally { connection.Gate.Release(); }
    }

    /// <summary>Reads one FINS/TCP frame and returns what follows its 16-byte header.</summary>
    internal static async Task<byte[]> ReadFrameAsync(Stream stream, uint expectedCommand, CancellationToken ct)
    {
        var header = new byte[TcpHeader];
        await stream.ReadExactlyAsync(header, ct);
        if (!header.AsSpan(0, 4).SequenceEqual("FINS"u8))
            throw new IOException("Omron answer is not FINS/TCP; check that the port is the PLC's FINS/TCP port (9600).");
        var length = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(4));
        if (length is < 8 or > 4096) throw new IOException("Omron answer has an invalid length.");
        var body = new byte[length - 8];
        await stream.ReadExactlyAsync(body, ct);
        var error = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(12));
        if (error != 0)
            throw new IOException($"Omron FINS/TCP error 0x{error:X8}" + error switch
            {
                0x20 => ": all FINS/TCP connections of the PLC are in use.",
                0x21 or 0x24 => ": FINS node address already in use; check the PLC's FINS/TCP node settings.",
                0x22 or 0x23 => ": FINS node address not allowed by the PLC's FINS/TCP settings.",
                _ => ".",
            });
        var command = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(8));
        if (command != expectedCommand) throw new IOException($"Omron answer has FINS/TCP command {command}, expected {expectedCommand}.");
        return body;
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
        private byte _sid;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public NetworkStream? Stream { get; private set; }
        public byte ClientNode { get; private set; }
        public byte ServerNode { get; private set; }
        public DateTimeOffset LastUsed { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? LastSuccess { get; private set; }
        public DateTimeOffset? LastFailure { get; private set; }

        public byte NextSid() => ++_sid;

        public async Task ConnectAsync(string host, int port, CancellationToken ct)
        {
            Close();
            _client = new TcpClient { NoDelay = true };
            await _client.ConnectAsync(host, port, ct);
            Stream = _client.GetStream();
            await Stream.WriteAsync(BuildHandshake(), ct);
            var body = await ReadFrameAsync(Stream, expectedCommand: 1, ct);
            if (body.Length < 8) throw new IOException("Omron FINS/TCP node address answer is too short.");
            ClientNode = body[3];
            ServerNode = body[7];
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
