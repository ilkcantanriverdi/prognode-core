using System.Collections.Concurrent;
using System.Net.Sockets;

namespace Prognode.Protocols.Modbus;

/// <summary>A Modbus exception response (e.g. 0x02 illegal data address). The connection stays valid.</summary>
public sealed class ModbusExceptionResponseException(byte functionCode, byte exceptionCode)
    : IOException($"Modbus exception 0x{exceptionCode:X2} for function 0x{functionCode:X2}.")
{
    public byte ExceptionCode { get; } = exceptionCode;
}

/// <summary>
/// Modbus TCP client with one persistent connection per endpoint (review Y3). Many gateways allow
/// only 4–8 sockets and S7-1200 Modbus servers few connections, so polls reuse the socket instead
/// of opening one per block. Requests on one endpoint are serialized. Any transport error closes
/// the socket; the next request reconnects. A request on a reused socket that the peer already
/// closed is retried once on a fresh connection.
/// </summary>
public sealed class ModbusTcpRegisterClient : IAsyncDisposable
{
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(2);
    private readonly ConcurrentDictionary<string, Connection> _connections = new(StringComparer.OrdinalIgnoreCase);
    private int _transactionId;

    public Task<bool[]> ReadCoilsAsync(
        string host, int port, int unitId, int startAddress, int quantity,
        int timeoutMs, CancellationToken cancellationToken) =>
        ReadBitsAsync(host, port, unitId, 0x01, startAddress, quantity, timeoutMs, cancellationToken);

    public Task<bool[]> ReadDiscreteInputsAsync(
        string host, int port, int unitId, int startAddress, int quantity,
        int timeoutMs, CancellationToken cancellationToken) =>
        ReadBitsAsync(host, port, unitId, 0x02, startAddress, quantity, timeoutMs, cancellationToken);

    public Task<ushort[]> ReadInputRegistersAsync(
        string host, int port, int unitId, int startAddress, int quantity,
        int timeoutMs, CancellationToken cancellationToken) =>
        ReadRegistersAsync(host, port, unitId, 0x04, startAddress, quantity, timeoutMs, cancellationToken);

    public Task<ushort[]> ReadHoldingRegistersAsync(
        string host, int port, int unitId, int startAddress, int quantity,
        int timeoutMs, CancellationToken cancellationToken) =>
        ReadRegistersAsync(host, port, unitId, 0x03, startAddress, quantity, timeoutMs, cancellationToken);

    /// <summary>True when a poll on this endpoint succeeded within <paramref name="window"/>; lets the
    /// health probe avoid opening an extra socket while polling is healthy.</summary>
    public bool HasRecentSuccess(string host, int port, TimeSpan window) =>
        _connections.TryGetValue(Key(host, port), out var connection) &&
        connection.LastSuccess is { } last && DateTimeOffset.UtcNow - last <= window &&
        (connection.LastFailure is null || connection.LastFailure < last);

    private async Task<bool[]> ReadBitsAsync(
        string host,
        int port,
        int unitId,
        byte functionCode,
        int startAddress,
        int quantity,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        if (quantity is < 1 or > 2000)
            throw new ArgumentOutOfRangeException(nameof(quantity));

        var pdu = await ExecuteReadAsync(
            host, port, unitId, functionCode, startAddress, quantity, timeoutMs, cancellationToken);

        if (pdu.Length < 2)
            throw new IOException("Invalid Modbus bit payload.");

        var byteCount = pdu[1];
        var expectedByteCount = (quantity + 7) / 8;
        if (byteCount != expectedByteCount || pdu.Length != 2 + byteCount)
            throw new IOException("Invalid Modbus bit payload length.");

        var bits = new bool[quantity];
        for (var i = 0; i < quantity; i++)
            bits[i] = (pdu[2 + (i / 8)] & (1 << (i % 8))) != 0;

        return bits;
    }

    private async Task<ushort[]> ReadRegistersAsync(
        string host,
        int port,
        int unitId,
        byte functionCode,
        int startAddress,
        int quantity,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        if (quantity is < 1 or > 125)
            throw new ArgumentOutOfRangeException(nameof(quantity));

        var pdu = await ExecuteReadAsync(
            host, port, unitId, functionCode, startAddress, quantity, timeoutMs, cancellationToken);

        if (pdu.Length < 2)
            throw new IOException("Invalid Modbus register payload.");

        var byteCount = pdu[1];
        if (byteCount != quantity * 2 || pdu.Length != 2 + byteCount)
            throw new IOException("Invalid Modbus register payload length.");

        var registers = new ushort[quantity];
        for (var i = 0; i < quantity; i++)
            registers[i] = (ushort)((pdu[2 + i * 2] << 8) | pdu[3 + i * 2]);

        return registers;
    }

    private async Task<byte[]> ExecuteReadAsync(
        string host,
        int port,
        int unitId,
        byte functionCode,
        int startAddress,
        int quantity,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(host))
            throw new ArgumentException("Modbus host is required.", nameof(host));
        if (port is < 1 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port));
        if (unitId is < 0 or > 255)
            throw new ArgumentOutOfRangeException(nameof(unitId));
        if (startAddress is < 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(startAddress));
        if ((long)startAddress + quantity > 65536)
            throw new ArgumentOutOfRangeException(nameof(quantity), "Modbus read exceeds the 16-bit address space.");
        if (timeoutMs < 1)
            throw new ArgumentOutOfRangeException(nameof(timeoutMs));

        CloseIdleConnections();
        var connection = _connections.GetOrAdd(Key(host, port), _ => new Connection());
        await connection.Gate.WaitAsync(cancellationToken);
        try
        {
            for (var attempt = 0; ; attempt++)
            {
                var reused = connection.Stream is not null;
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(timeoutMs);
                try
                {
                    if (connection.Stream is null)
                        await connection.ConnectAsync(host, port, timeout.Token);
                    var pdu = await ExchangeAsync(connection.Stream!, unitId, functionCode,
                        startAddress, quantity, timeout.Token);
                    connection.MarkSuccess();
                    return pdu;
                }
                catch (ModbusExceptionResponseException)
                {
                    // Protocol-level answer: the socket is still in sync and stays open.
                    connection.MarkSuccess();
                    throw;
                }
                catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException or ObjectDisposedException)
                {
                    // Timeouts can leave a half-read response on the wire: never reuse that socket.
                    connection.MarkFailure();
                    connection.Close();
                    if (cancellationToken.IsCancellationRequested) throw;
                    if (reused && attempt == 0 && ex is not OperationCanceledException)
                        continue;
                    if (ex is OperationCanceledException)
                        throw new IOException($"Modbus TCP timeout after {timeoutMs} ms.", ex);
                    throw;
                }
            }
        }
        finally
        {
            connection.Gate.Release();
        }
    }

    private async Task<byte[]> ExchangeAsync(
        NetworkStream stream,
        int unitId,
        byte functionCode,
        int startAddress,
        int quantity,
        CancellationToken cancellationToken)
    {
        var transactionId = unchecked((ushort)Interlocked.Increment(ref _transactionId));

        var request = new byte[]
        {
            (byte)(transactionId >> 8), (byte)(transactionId & 0xFF),
            0x00, 0x00, 0x00, 0x06, (byte)unitId, functionCode,
            (byte)(startAddress >> 8), (byte)(startAddress & 0xFF),
            (byte)(quantity >> 8), (byte)(quantity & 0xFF)
        };

        await stream.WriteAsync(request, cancellationToken);

        var header = new byte[7];
        await ReadExactlyAsync(stream, header, cancellationToken);

        var responseTransactionId = (ushort)((header[0] << 8) | header[1]);
        var protocolId = (ushort)((header[2] << 8) | header[3]);
        var length = (ushort)((header[4] << 8) | header[5]);

        if (responseTransactionId != transactionId)
            throw new IOException("Modbus transaction ID mismatch.");
        if (protocolId != 0)
            throw new IOException($"Invalid Modbus protocol ID {protocolId}.");
        if (header[6] != (byte)unitId)
            throw new IOException("Modbus unit ID mismatch.");

        var remaining = length - 1;
        if (remaining is <= 0 or > 260)
            throw new IOException($"Invalid Modbus response length {length}.");

        var pdu = new byte[remaining];
        await ReadExactlyAsync(stream, pdu, cancellationToken);

        if (pdu.Length < 1)
            throw new IOException("Empty Modbus PDU.");

        var responseFunction = pdu[0];
        if ((responseFunction & 0x80) != 0)
        {
            if (pdu.Length != 2)
                throw new IOException("Invalid Modbus exception response.");
            throw new ModbusExceptionResponseException(functionCode, pdu[1]);
        }

        if (responseFunction != functionCode)
            throw new IOException($"Unexpected Modbus function 0x{responseFunction:X2}; expected 0x{functionCode:X2}.");

        return pdu;
    }

    private static async Task ReadExactlyAsync(
        NetworkStream stream,
        byte[] buffer,
        CancellationToken cancellationToken)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(
                buffer.AsMemory(offset, buffer.Length - offset),
                cancellationToken);

            if (read == 0)
                throw new IOException("Remote endpoint closed the connection.");

            offset += read;
        }
    }

    private void CloseIdleConnections()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var (key, connection) in _connections)
        {
            if (now - connection.LastUsed <= IdleTimeout || !connection.Gate.Wait(0)) continue;
            try
            {
                if (now - connection.LastUsed > IdleTimeout)
                {
                    connection.Close();
                    _connections.TryRemove(key, out _);
                }
            }
            finally { connection.Gate.Release(); }
        }
    }

    private static string Key(string host, int port) => $"{host.Trim()}:{port}";

    public ValueTask DisposeAsync()
    {
        foreach (var connection in _connections.Values) connection.Close();
        _connections.Clear();
        return ValueTask.CompletedTask;
    }

    private sealed class Connection
    {
        private TcpClient? _socket;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public NetworkStream? Stream { get; private set; }
        public DateTimeOffset LastUsed { get; private set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? LastSuccess { get; private set; }
        public DateTimeOffset? LastFailure { get; private set; }

        public void MarkFailure() => LastFailure = DateTimeOffset.UtcNow;

        public async Task ConnectAsync(string host, int port, CancellationToken cancellationToken)
        {
            Close();
            var socket = new TcpClient { NoDelay = true };
            try
            {
                await socket.ConnectAsync(host, port, cancellationToken);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
            _socket = socket;
            Stream = socket.GetStream();
        }

        public void MarkSuccess()
        {
            LastUsed = DateTimeOffset.UtcNow;
            LastSuccess = LastUsed;
        }

        public void Close()
        {
            LastUsed = DateTimeOffset.UtcNow;
            Stream?.Dispose();
            _socket?.Dispose();
            Stream = null;
            _socket = null;
        }
    }
}
