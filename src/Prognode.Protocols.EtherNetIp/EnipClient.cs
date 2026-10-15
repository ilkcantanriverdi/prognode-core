using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.Sockets;
using System.Text;

namespace Prognode.Protocols.EtherNetIp;

/// <summary>A CIP request was refused by the controller or a module on the route (the session stays valid).</summary>
public sealed class CipException(byte status, ushort? extended, string message) : IOException(message)
{
    public byte Status { get; } = status;
    public ushort? Extended { get; } = extended;
}

/// <summary>One Read Tag reply: the CIP data type code and its little-endian data, or the CIP status that refused it.</summary>
public readonly record struct CipReadReply(byte Status, ushort? Extended, ushort TypeCode, byte[] Data);

/// <summary>
/// EtherNet/IP explicit messaging client for Logix controllers (ControlLogix, CompactLogix, GuardLogix,
/// Micro800): RegisterSession, then SendRRData (UCMM) with Read Tag (0x4C) requests, several per
/// Multiple Service Packet (0x0A), wrapped in Unconnected Send to the CPU slot unless the controller is
/// addressed directly. One persistent session per endpoint; requests on an endpoint are serialized. Read-only.
/// </summary>
public sealed class EnipClient : IAsyncDisposable
{
    /// <summary>UnitId value meaning "no backplane route" (Micro800 and controllers with a built-in port reached directly).</summary>
    public const int DirectRoute = 255;
    /// <summary>Unconnected messages are limited to about 500 bytes each way.</summary>
    public const int MaxRequestBytes = 480;
    public const int MaxReadsPerPacket = 24;
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(2);
    private readonly ConcurrentDictionary<string, Session> _sessions = new(StringComparer.OrdinalIgnoreCase);

    public bool HasRecentSuccess(string host, int port, TimeSpan window) =>
        _sessions.TryGetValue(Key(host, port), out var s) && s.LastSuccess is { } last &&
        DateTimeOffset.UtcNow - last <= window && (s.LastFailure is null || s.LastFailure < last);

    /// <summary>Reads several tags in one Multiple Service Packet (or one plain Read Tag); one reply per path, in order.</summary>
    public async Task<CipReadReply[]> ReadTagsAsync(string host, int port, int slot, IReadOnlyList<byte[]> paths, int timeoutMs, CancellationToken ct)
    {
        if (paths.Count is < 1 or > MaxReadsPerPacket) throw new ArgumentOutOfRangeException(nameof(paths));
        var request = paths.Count == 1 ? ReadTagRequest(paths[0]) : MultipleServiceRequest(paths.Select(ReadTagRequest).ToArray());
        var reply = await ExecuteAsync(host, port, slot, request, timeoutMs, ct);
        if (paths.Count == 1)
            return [ParseReadReply(reply)];
        var (status, extended, body) = ParseReply(reply, 0x0A);
        // 0x1E: one or more embedded services failed; each embedded reply carries its own status.
        if (status is not (0 or 0x1E)) throw Refused(status, extended);
        if (body.Length < 2) throw new IOException("EtherNet/IP Multiple Service reply is too short.");
        var count = BinaryPrimitives.ReadUInt16LittleEndian(body);
        if (count != paths.Count || body.Length < 2 + 2 * count) throw new IOException("EtherNet/IP Multiple Service reply has the wrong number of replies.");
        var replies = new CipReadReply[count];
        for (var i = 0; i < count; i++)
        {
            int start = BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(2 + 2 * i));
            var end = i + 1 < count ? BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(4 + 2 * i)) : body.Length;
            if (start < 2 + 2 * count || end > body.Length || end < start) throw new IOException("EtherNet/IP Multiple Service reply offsets are invalid.");
            replies[i] = ParseReadReply(body[start..end]);
        }
        return replies;
    }

    /// <summary>ListIdentity over the TCP connection: the product name the device reports (e.g. 1769-L33ER/A LOGIX5333ER).</summary>
    public async Task<string> ListIdentityAsync(string host, int port, int timeoutMs, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(timeoutMs);
        using var client = new TcpClient { NoDelay = true };
        await client.ConnectAsync(host, port, timeout.Token);
        var stream = client.GetStream();
        await stream.WriteAsync(Encapsulation(0x63, 0, []), timeout.Token);
        var (_, body) = await ReadEncapsulationAsync(stream, 0x63, timeout.Token);
        // Item count, then item 0x0C: type, length, version, socket address (16), vendor, device type,
        // product code, revision (2), status, serial (4), name length, name.
        if (body.Length < 2 + 4 + 2 + 16 + 14 + 1) throw new IOException("EtherNet/IP ListIdentity reply is too short.");
        var nameAt = 2 + 4 + 2 + 16 + 2 + 2 + 2 + 2 + 2 + 4;
        var length = body[nameAt];
        return Encoding.ASCII.GetString(body, nameAt + 1, Math.Min(length, body.Length - nameAt - 1)).Trim();
    }

    public static byte[] ReadTagRequest(byte[] path) =>
        [0x4C, (byte)(path.Length / 2), .. path, 0x01, 0x00];

    /// <summary>Multiple Service Packet (0x0A) to the Message Router (class 2, instance 1).</summary>
    public static byte[] MultipleServiceRequest(IReadOnlyList<byte[]> services)
    {
        var header = 2 + 2 * services.Count;
        var data = new List<byte> { 0x0A, 0x02, 0x20, 0x02, 0x24, 0x01, (byte)services.Count, (byte)(services.Count >> 8) };
        var offset = header;
        foreach (var service in services) { data.Add((byte)offset); data.Add((byte)(offset >> 8)); offset += service.Length; }
        foreach (var service in services) data.AddRange(service);
        return [.. data];
    }

    /// <summary>Unconnected Send (0x52) to the Connection Manager, routed over the backplane (port 1) to the CPU slot.</summary>
    public static byte[] UnconnectedSend(byte[] message, int slot)
    {
        var data = new List<byte>
        {
            0x52, 0x02, 0x20, 0x06, 0x24, 0x01,
            0x0A, 0x05,                                         // priority / tick 1024 ms, 5 ticks
            (byte)message.Length, (byte)(message.Length >> 8),
        };
        data.AddRange(message);
        if (message.Length % 2 == 1) data.Add(0);
        data.AddRange([0x01, 0x00, 0x01, (byte)slot]);          // route path: 1 word, reserved, port 1, link = slot
        return [.. data];
    }

    /// <summary>SendRRData (0x6F) carrying one unconnected CIP message: null address item + unconnected data item.</summary>
    public static byte[] SendRRData(uint session, byte[] cip, ulong context)
    {
        byte[] data = [0, 0, 0, 0, 0x0A, 0x00, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0xB2, 0x00, (byte)cip.Length, (byte)(cip.Length >> 8), .. cip];
        return Encapsulation(0x6F, session, data, context);
    }

    public static byte[] Encapsulation(ushort command, uint session, byte[] data, ulong context = 0)
    {
        var frame = new byte[24 + data.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(frame, command);
        BinaryPrimitives.WriteUInt16LittleEndian(frame.AsSpan(2), (ushort)data.Length);
        BinaryPrimitives.WriteUInt32LittleEndian(frame.AsSpan(4), session);
        BinaryPrimitives.WriteUInt64LittleEndian(frame.AsSpan(12), context);
        data.CopyTo(frame, 24);
        return frame;
    }

    private static CipReadReply ParseReadReply(byte[] reply)
    {
        var (status, extended, body) = ParseReply(reply, 0x4C);
        if (status != 0) return new(status, extended, 0, []);
        if (body.Length < 2) throw new IOException("EtherNet/IP Read Tag reply has no data type.");
        var type = BinaryPrimitives.ReadUInt16LittleEndian(body);
        // Structures carry a 2-byte structure handle after 0x02A0.
        return type == 0x02A0 ? new(0, null, type, body[Math.Min(4, body.Length)..]) : new(0, null, type, body[2..]);
    }

    private static (byte Status, ushort? Extended, byte[] Body) ParseReply(byte[] reply, byte service)
    {
        if (reply.Length < 4 || reply[0] != (service | 0x80)) throw new IOException($"EtherNet/IP reply to service 0x{service:X2} is malformed.");
        var extendedWords = reply[3];
        if (reply.Length < 4 + 2 * extendedWords) throw new IOException("EtherNet/IP reply is too short.");
        ushort? extended = extendedWords > 0 ? BinaryPrimitives.ReadUInt16LittleEndian(reply.AsSpan(4)) : null;
        return (reply[2], extended, reply[(4 + 2 * extendedWords)..]);
    }

    internal static CipException Refused(byte status, ushort? extended) =>
        new(status, extended, $"Controller refused the request (CIP status 0x{status:X2}{(extended is { } e ? $"/0x{e:X4}" : "")}{Hint(status, extended)}).");

    public static string Hint(byte status, ushort? extended) => (status, extended) switch
    {
        (0x01, 0x0204) => ": no answer from the CPU slot — check the slot number",
        (0x01, 0x0311 or 0x0312 or 0x0315) => ": invalid route — check the CPU slot, or use 'direct' for Micro800",
        (0x01, _) => ": connection failure on the route",
        (0x04, _) or (0x05, _) => ": tag not found — check the name and scope (Program:Name.Tag)",
        (0x08, _) => ": service not supported by this device",
        (0x0F, _) => ": access denied — set the tag's External Access to Read Only or Read/Write",
        (0x06, _) => ": the tag is larger than one message; read a member or element instead",
        (0x1E, _) => ": one of the requests failed",
        _ => "",
    };

    private async Task<byte[]> ExecuteAsync(string host, int port, int slot, byte[] request, int timeoutMs, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("Controller IP is required.", nameof(host));
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        if (slot is not (DirectRoute or >= 0 and <= 31)) throw new ArgumentOutOfRangeException(nameof(slot));
        CloseIdle();
        var session = _sessions.GetOrAdd(Key(host, port), _ => new Session());
        await session.Gate.WaitAsync(ct);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(timeoutMs);
            var cip = slot == DirectRoute ? request : UnconnectedSend(request, slot);
            for (var attempt = 0; ; attempt++)
            {
                var reused = session.Stream is not null;
                try
                {
                    session.LastUsed = DateTimeOffset.UtcNow;
                    if (session.Stream is null) await session.ConnectAsync(host, port, timeout.Token);
                    var context = session.NextContext();
                    await session.Stream!.WriteAsync(SendRRData(session.Handle, cip, context), timeout.Token);
                    var (replyContext, body) = await ReadEncapsulationAsync(session.Stream, 0x6F, timeout.Token);
                    if (replyContext != context) throw new IOException("EtherNet/IP reply does not match the request.");
                    var reply = UnconnectedData(body);
                    session.MarkSuccess();
                    // A failed Unconnected Send comes back as its own reply (0xD2) with the route error.
                    if (reply.Length >= 4 && reply[0] == 0xD2)
                    {
                        var (status, extended, _) = ParseReply(reply, 0x52);
                        throw Refused(status, extended);
                    }
                    return reply;
                }
                catch (CipException) { throw; }
                catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException && !ct.IsCancellationRequested)
                {
                    session.Close();
                    session.MarkFailure();
                    // A reused session the controller already closed (idle timeout) is retried once on a fresh one.
                    if (reused && attempt == 0 && ex is not OperationCanceledException) continue;
                    if (ex is OperationCanceledException)
                        throw new IOException($"Controller {host}:{port} did not answer within {timeoutMs} ms.", ex);
                    throw new IOException($"Controller {host}:{port}: {ex.Message}", ex);
                }
            }
        }
        finally { session.Gate.Release(); }
    }

    /// <summary>The unconnected data item (0x00B2) of a SendRRData reply.</summary>
    private static byte[] UnconnectedData(byte[] body)
    {
        if (body.Length < 8) throw new IOException("EtherNet/IP SendRRData reply is too short.");
        var count = BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(6));
        var at = 8;
        for (var i = 0; i < count; i++)
        {
            if (at + 4 > body.Length) break;
            var type = BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(at));
            var length = BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(at + 2));
            if (at + 4 + length > body.Length) break;
            if (type == 0x00B2) return body[(at + 4)..(at + 4 + length)];
            at += 4 + length;
        }
        throw new IOException("EtherNet/IP reply has no unconnected data item.");
    }

    internal static async Task<(ulong Context, byte[] Body)> ReadEncapsulationAsync(Stream stream, ushort expectedCommand, CancellationToken ct)
    {
        var header = new byte[24];
        await stream.ReadExactlyAsync(header, ct);
        var command = BinaryPrimitives.ReadUInt16LittleEndian(header);
        var length = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(2));
        var status = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8));
        var body = new byte[length];
        await stream.ReadExactlyAsync(body, ct);
        if (command != expectedCommand) throw new IOException($"EtherNet/IP reply has command 0x{command:X4}, expected 0x{expectedCommand:X4}; check that the port is EtherNet/IP (44818).");
        if (status != 0)
            throw new IOException($"EtherNet/IP encapsulation error 0x{status:X4}" + status switch
            {
                0x01 => ": command not supported.",
                0x64 => ": invalid session; reconnecting.",
                0x69 => ": unsupported protocol version.",
                _ => ".",
            });
        return (BinaryPrimitives.ReadUInt64LittleEndian(header.AsSpan(12)), body);
    }

    private static string Key(string host, int port) => $"{host.Trim()}:{port}";

    private void CloseIdle()
    {
        foreach (var (key, s) in _sessions)
            if (DateTimeOffset.UtcNow - s.LastUsed > IdleTimeout && s.Gate.Wait(0))
            {
                try { s.Close(); _sessions.TryRemove(key, out _); }
                finally { s.Gate.Release(); }
            }
    }

    public ValueTask DisposeAsync()
    {
        foreach (var (_, s) in _sessions) s.Close();
        _sessions.Clear();
        return ValueTask.CompletedTask;
    }

    private sealed class Session
    {
        private TcpClient? _client;
        private ulong _context;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public NetworkStream? Stream { get; private set; }
        public uint Handle { get; private set; }
        public DateTimeOffset LastUsed { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? LastSuccess { get; private set; }
        public DateTimeOffset? LastFailure { get; private set; }

        public ulong NextContext() => ++_context;

        public async Task ConnectAsync(string host, int port, CancellationToken ct)
        {
            Close();
            _client = new TcpClient { NoDelay = true };
            await _client.ConnectAsync(host, port, ct);
            Stream = _client.GetStream();
            // RegisterSession: protocol version 1, options 0.
            await Stream.WriteAsync(Encapsulation(0x65, 0, [1, 0, 0, 0]), ct);
            var header = new byte[24];
            await Stream.ReadExactlyAsync(header, ct);
            var body = new byte[BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(2))];
            await Stream.ReadExactlyAsync(body, ct);
            var status = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(8));
            if (BinaryPrimitives.ReadUInt16LittleEndian(header) != 0x65 || status != 0)
                throw new IOException($"EtherNet/IP RegisterSession refused (status 0x{status:X4}); check that the port is EtherNet/IP (44818).");
            Handle = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(4));
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
