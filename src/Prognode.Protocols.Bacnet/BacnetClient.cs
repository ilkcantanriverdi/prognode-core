using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net.Sockets;

namespace Prognode.Protocols.Bacnet;

/// <summary>The device answered with a BACnet Error, Reject or Abort for the whole request.</summary>
public sealed class BacnetRefusedException(string kind, int first, int second, string message) : IOException(message)
{
    public string Kind { get; } = kind;
    /// <summary>Error class, reject reason or abort reason.</summary>
    public int First { get; } = first;
    /// <summary>Error code (Error only).</summary>
    public int Second { get; } = second;
}

/// <summary>One Present_Value: its application tag (1 boolean, 2 unsigned, 3 signed, 4 real, 5 double, 9 enumerated) and value, or the error.</summary>
public readonly record struct BacnetValue(int Tag, double Value, int? ErrorClass, int? ErrorCode);

/// <summary>
/// BACnet/IP client (Annex J, UDP 47808): ReadPropertyMultiple of Present_Value for up to 20 objects per
/// request, ReadProperty when the device does not support it. Devices on the IP network are addressed
/// directly (no BACnet routing to MS/TP). One UDP socket per endpoint; requests on an endpoint are serialized. Read-only.
/// </summary>
public sealed class BacnetClient : IAsyncDisposable
{
    public const int MaxObjectsPerRequest = 20;
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(2);
    private readonly ConcurrentDictionary<string, Endpoint> _endpoints = new(StringComparer.OrdinalIgnoreCase);

    public bool HasRecentSuccess(string host, int port, TimeSpan window) =>
        _endpoints.TryGetValue(Key(host, port), out var e) && e.LastSuccess is { } last &&
        DateTimeOffset.UtcNow - last <= window && (e.LastFailure is null || e.LastFailure < last);

    /// <summary>Present_Value of each object, in order. Uses ReadPropertyMultiple, or ReadProperty per object
    /// after the device rejected ReadPropertyMultiple once.</summary>
    public async Task<BacnetValue[]> ReadPresentValuesAsync(string host, int port, IReadOnlyList<BacnetObjectAddress> objects, int timeoutMs, CancellationToken ct)
    {
        if (objects.Count is < 1 or > MaxObjectsPerRequest) throw new ArgumentOutOfRangeException(nameof(objects));
        var endpoint = _endpoints.GetOrAdd(Key(host, port), _ => new Endpoint());
        if (!endpoint.SingleReadsOnly)
        {
            try
            {
                var ack = await ExecuteAsync(host, port, endpoint, 0x0E, ReadPropertyMultipleService(objects), timeoutMs, ct);
                return ParseReadPropertyMultipleAck(ack, objects);
            }
            catch (BacnetRefusedException ex) when (ex.Kind == "Reject" && ex.First is 9 or 2 || ex.Kind == "Error" && ex.First == 5 && ex.Second == 5)
            {
                // Reject unrecognized-service / buffer overflow or Error services:service-request-denied: no ReadPropertyMultiple here.
                endpoint.SingleReadsOnly = true;
            }
            catch (BacnetRefusedException ex) when (ex.Kind == "Error" && objects.Count > 1)
            {
                // Some devices refuse the whole request when one object is missing: read this batch one by one.
            }
        }
        var values = new BacnetValue[objects.Count];
        for (var i = 0; i < objects.Count; i++)
        {
            try { values[i] = ParseReadPropertyAck(await ExecuteAsync(host, port, endpoint, 0x0C, ReadPropertyService(objects[i]), timeoutMs, ct)); }
            catch (BacnetRefusedException ex) when (ex.Kind == "Error") { values[i] = new(0, 0, ex.First, ex.Second); }
        }
        return values;
    }

    /// <summary>ReadProperty of the device object (wildcard instance 4194303) Object_Name. Any answer, even an
    /// Error or Reject, proves the device is there; returns the device name when it gives one.</summary>
    public async Task<string?> PingAsync(string host, int port, int timeoutMs, CancellationToken ct)
    {
        var endpoint = _endpoints.GetOrAdd(Key(host, port), _ => new Endpoint());
        byte[] service = [0x0C, .. ObjectId(8u << 22 | 4194303), 0x19, 77];
        byte[] ack;
        try { ack = await ExecuteAsync(host, port, endpoint, 0x0C, service, timeoutMs, ct); }
        catch (BacnetRefusedException) { return null; }
        try
        {
            var at = SkipTag(ack, SkipTag(ack, 0));                  // object identifier, property identifier
            if (IsContext(ack, at, 2)) at = SkipTag(ack, at);
            if (!IsOpening(ack, at, 3)) return null;
            var (number, context, length, start) = ReadTag(ack, at + 1);
            // Character string: first byte is the character set (0 = UTF-8).
            return number == 7 && !context && length >= 1 ? System.Text.Encoding.UTF8.GetString(ack, start + 1, length - 1) : null;
        }
        catch (IOException) { return null; }
    }

    public static byte[] ReadPropertyService(BacnetObjectAddress o) => [0x0C, .. ObjectId(o.Identifier), 0x19, BacnetObjectAddress.PresentValue];

    /// <summary>One ReadAccessSpecification per object: object identifier, [ property Present_Value ].</summary>
    public static byte[] ReadPropertyMultipleService(IReadOnlyList<BacnetObjectAddress> objects) =>
        objects.SelectMany(o => (byte[])[0x0C, .. ObjectId(o.Identifier), 0x1E, 0x09, BacnetObjectAddress.PresentValue, 0x1F]).ToArray();

    /// <summary>BVLC Original-Unicast-NPDU, NPDU expecting a reply, confirmed request (no segmentation, max APDU 1476).</summary>
    public static byte[] Frame(byte invokeId, byte service, byte[] serviceData)
    {
        byte[] npdu = [0x01, 0x04, 0x00, 0x05, invokeId, service, .. serviceData];
        var length = 4 + npdu.Length;
        return [0x81, 0x0A, (byte)(length >> 8), (byte)length, .. npdu];
    }

    private static byte[] ObjectId(uint id) => [(byte)(id >> 24), (byte)(id >> 16), (byte)(id >> 8), (byte)id];

    private async Task<byte[]> ExecuteAsync(string host, int port, Endpoint endpoint, byte service, byte[] data, int timeoutMs, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("Device IP is required.", nameof(host));
        if (port is < 1 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        CloseIdle();
        await endpoint.Gate.WaitAsync(ct);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(timeoutMs);
            try
            {
                endpoint.LastUsed = DateTimeOffset.UtcNow;
                endpoint.Open(host, port);
                var invokeId = endpoint.NextInvokeId();
                await endpoint.Socket!.SendAsync(Frame(invokeId, service, data), timeout.Token);
                while (true)
                {
                    var received = await endpoint.Socket.ReceiveAsync(timeout.Token);
                    var apdu = Apdu(received.Buffer);
                    // Late answers to an earlier (timed-out) request and unconfirmed messages are skipped.
                    if (apdu is null || apdu.Length < 3 || apdu[1] != invokeId) continue;
                    var type = apdu[0] >> 4;
                    endpoint.MarkSuccess();
                    switch (type)
                    {
                        case 3 when (apdu[0] & 0x08) != 0:
                            throw new BacnetRefusedException("Abort", 4, 0, "BACnet device answered with a segmented response; read fewer objects per Device.");
                        case 3 when apdu[2] == service:
                            return apdu[3..];
                        case 5:
                            var (errorClass, errorCode) = ParseError(apdu, 3);
                            throw new BacnetRefusedException("Error", errorClass, errorCode, ErrorMessage(errorClass, errorCode));
                        case 6:
                            throw new BacnetRefusedException("Reject", apdu[2], 0, $"BACnet device rejected the request (reason {apdu[2]}{(apdu[2] == 9 ? ": unrecognized service" : "")}).");
                        case 7:
                            throw new BacnetRefusedException("Abort", apdu[2], 0, $"BACnet device aborted the request (reason {apdu[2]}{(apdu[2] == 4 ? ": segmentation not supported" : "")}).");
                    }
                }
            }
            catch (BacnetRefusedException) { throw; }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException && !ct.IsCancellationRequested)
            {
                endpoint.Close();
                endpoint.MarkFailure();
                if (ex is OperationCanceledException)
                    throw new IOException($"BACnet device {host}:{port} did not answer within {timeoutMs} ms.", ex);
                // On UDP a "reset" is the ICMP port-unreachable answer: the host is up, nothing listens on the port.
                if (ex is SocketException { SocketErrorCode: SocketError.ConnectionReset })
                    throw new IOException($"No BACnet device is listening at {host}:{port} (UDP port unreachable).", ex);
                throw new IOException($"BACnet device {host}:{port}: {ex.Message}", ex);
            }
        }
        finally { endpoint.Gate.Release(); }
    }

    /// <summary>The APDU of a BACnet/IP datagram (BVLC unicast or forwarded NPDU, NPDU without network message).</summary>
    private static byte[]? Apdu(byte[] datagram)
    {
        if (datagram.Length < 6 || datagram[0] != 0x81) return null;
        var at = datagram[1] switch { 0x0A or 0x0B => 4, 0x04 => 10, _ => -1 };
        if (at < 0 || datagram.Length < at + 2 || datagram[at] != 0x01) return null;
        var control = datagram[at + 1];
        if ((control & 0x80) != 0) return null;                    // network layer message
        at += 2;
        if ((control & 0x20) != 0) { at += 3; at += datagram[at - 1]; } // DNET, DLEN, DADR
        if ((control & 0x08) != 0) { at += 3; at += datagram[at - 1]; } // SNET, SLEN, SADR
        if ((control & 0x20) != 0) at += 1;                          // hop count
        return at < datagram.Length ? datagram[at..] : null;
    }

    private static (int Class, int Code) ParseError(byte[] apdu, int at)
    {
        var (_, _, l1, s1) = ReadTag(apdu, at);
        var errorClass = (int)ReadUnsigned(apdu, s1, l1);
        var (_, _, l2, s2) = ReadTag(apdu, s1 + l1);
        return (errorClass, (int)ReadUnsigned(apdu, s2, l2));
    }

    public static string ErrorMessage(int errorClass, int errorCode) => (errorClass, errorCode) switch
    {
        (1, 31) => "BACnet object not found in the device (unknown-object).",
        (2, 32) => "The object has no Present_Value (unknown-property).",
        (2, 27) or (4, 27) => "Reading this property is not allowed (read-access-denied).",
        (5, 5) => "The device denied the request (service-request-denied).",
        (0, _) => $"BACnet device error (device class, code {errorCode}).",
        _ => $"BACnet device error (class {errorClass}, code {errorCode}).",
    };

    internal static BacnetValue[] ParseReadPropertyMultipleAck(byte[] ack, IReadOnlyList<BacnetObjectAddress> objects)
    {
        var values = new BacnetValue[objects.Count];
        var filled = new bool[objects.Count];
        var at = 0;
        while (at < ack.Length)
        {
            var (number, context, length, start) = ReadTag(ack, at);
            if (!(number == 0 && context && length == 4)) throw new IOException("BACnet ReadPropertyMultiple answer is malformed.");
            var id = BinaryPrimitives.ReadUInt32BigEndian(ack.AsSpan(start));
            at = start + 4;
            at = Expect(ack, at, 1, opening: true);
            BacnetValue? value = null;
            while (!IsClosing(ack, at, 1))
            {
                (number, context, length, start) = ReadTag(ack, at);
                var property = ReadUnsigned(ack, start, length);
                at = start + length;
                if (IsContext(ack, at, 3)) at = SkipTag(ack, at);   // property array index
                if (IsOpening(ack, at, 4))
                {
                    var (vn, vc, vl, vs) = ReadTag(ack, at + 1);
                    var v = DecodeApplication(ack, vn, vc, vl, vs);
                    at = SkipConstructed(ack, at, 4);
                    if (property == BacnetObjectAddress.PresentValue) value = v;
                }
                else if (IsOpening(ack, at, 5))
                {
                    var (errorClass, errorCode) = ParseError(ack, at + 1);
                    at = SkipConstructed(ack, at, 5);
                    if (property == BacnetObjectAddress.PresentValue) value = new(0, 0, errorClass, errorCode);
                }
                else throw new IOException("BACnet ReadPropertyMultiple answer is malformed.");
            }
            at += 1; // closing tag 1
            var index = Enumerable.Range(0, objects.Count).FirstOrDefault(i => !filled[i] && objects[i].Identifier == id, -1);
            if (index >= 0 && value is { } found) { values[index] = found; filled[index] = true; }
        }
        for (var i = 0; i < objects.Count; i++)
            if (!filled[i]) throw new IOException($"BACnet ReadPropertyMultiple answer has no value for {objects[i]}.");
        return values;
    }

    internal static BacnetValue ParseReadPropertyAck(byte[] ack)
    {
        var at = SkipTag(ack, SkipTag(ack, 0));                    // object identifier, property identifier
        if (IsContext(ack, at, 2)) at = SkipTag(ack, at);         // array index
        if (!IsOpening(ack, at, 3)) throw new IOException("BACnet ReadProperty answer is malformed.");
        var (n, c, l, s) = ReadTag(ack, at + 1);
        return DecodeApplication(ack, n, c, l, s);
    }

    private static BacnetValue DecodeApplication(byte[] b, int number, bool context, int length, int start)
    {
        if (context) throw new IOException("BACnet value is not application-tagged.");
        return number switch
        {
            0 => new(0, double.NaN, null, null),
            1 => new(1, length != 0 ? 1 : 0, null, null),             // boolean: value in the length bits
            2 or 9 => new(number, ReadUnsigned(b, start, length), null, null),
            3 => new(3, ReadSigned(b, start, length), null, null),
            4 => new(4, BinaryPrimitives.ReadSingleBigEndian(b.AsSpan(start, 4)), null, null),
            5 => new(5, BinaryPrimitives.ReadDoubleBigEndian(b.AsSpan(start, 8)), null, null),
            _ => new(number, double.NaN, null, null),
        };
    }

    private static ulong ReadUnsigned(byte[] b, int start, int length)
    {
        ulong v = 0;
        for (var i = 0; i < length; i++) v = v << 8 | b[start + i];
        return v;
    }

    private static long ReadSigned(byte[] b, int start, int length)
    {
        long v = (sbyte)b[start];
        for (var i = 1; i < length; i++) v = v << 8 | b[start + i];
        return v;
    }

    /// <summary>Tag number, context flag, length (value bytes; for booleans the value) and where the value starts.</summary>
    private static (int Number, bool Context, int Length, int Start) ReadTag(byte[] b, int at)
    {
        if (at >= b.Length) throw new IOException("BACnet answer ends early.");
        var first = b[at];
        var number = first >> 4;
        var context = (first & 0x08) != 0;
        var lvt = first & 0x07;
        var next = at + 1;
        if (number == 0x0F) number = b[next++];
        if (lvt is 6 or 7 && context) return (number, true, -lvt, next);   // opening / closing
        var length = lvt;
        if (lvt == 5)
        {
            length = b[next++];
            if (length == 254) { length = BinaryPrimitives.ReadUInt16BigEndian(b.AsSpan(next)); next += 2; }
            else if (length == 255) { length = (int)BinaryPrimitives.ReadUInt32BigEndian(b.AsSpan(next)); next += 4; }
        }
        if (!context && number == 1) return (1, false, length, next);   // boolean has no value bytes
        if (next + length > b.Length) throw new IOException("BACnet answer ends early.");
        return (number, context, length, next);
    }

    private static int SkipTag(byte[] b, int at)
    {
        var (number, context, length, start) = ReadTag(b, at);
        if (!context && number == 1) return start;
        return start + Math.Max(length, 0);
    }

    private static int SkipConstructed(byte[] b, int at, int number)
    {
        var depth = 0;
        do
        {
            var (_, context, length, start) = ReadTag(b, at);
            if (context && length == -6) { depth++; at = start; }
            else if (context && length == -7) { depth--; at = start; }
            else at = SkipTag(b, at);
        } while (depth > 0);
        return at;
    }

    private static int Expect(byte[] b, int at, int number, bool opening)
    {
        if (!(opening ? IsOpening(b, at, number) : IsClosing(b, at, number))) throw new IOException("BACnet answer is malformed.");
        return at + 1;
    }

    private static bool IsOpening(byte[] b, int at, int number) => at < b.Length && b[at] == (number << 4 | 0x0E);
    private static bool IsClosing(byte[] b, int at, int number) => at < b.Length && b[at] == (number << 4 | 0x0F);
    private static bool IsContext(byte[] b, int at, int number) => at < b.Length && (b[at] & 0xF8) == (number << 4 | 0x08) && (b[at] & 0x07) < 6;

    private static string Key(string host, int port) => $"{host.Trim()}:{port}";

    private void CloseIdle()
    {
        foreach (var (key, e) in _endpoints)
            if (DateTimeOffset.UtcNow - e.LastUsed > IdleTimeout && e.Gate.Wait(0))
            {
                try { e.Close(); _endpoints.TryRemove(key, out _); }
                finally { e.Gate.Release(); }
            }
    }

    public ValueTask DisposeAsync()
    {
        foreach (var (_, e) in _endpoints) e.Close();
        _endpoints.Clear();
        return ValueTask.CompletedTask;
    }

    private sealed class Endpoint
    {
        private byte _invokeId;
        public SemaphoreSlim Gate { get; } = new(1, 1);
        public UdpClient? Socket { get; private set; }
        public volatile bool SingleReadsOnly;
        public DateTimeOffset LastUsed { get; set; } = DateTimeOffset.UtcNow;
        public DateTimeOffset? LastSuccess { get; private set; }
        public DateTimeOffset? LastFailure { get; private set; }

        public byte NextInvokeId() => ++_invokeId;

        public void Open(string host, int port)
        {
            if (Socket is not null) return;
            // A connected UDP socket only accepts datagrams from the device, and an unreachable
            // device shows up quickly as a reset instead of a timeout.
            Socket = new UdpClient();
            Socket.Connect(host, port);
        }

        public void MarkSuccess() => LastSuccess = DateTimeOffset.UtcNow;
        public void MarkFailure() => LastFailure = DateTimeOffset.UtcNow;

        public void Close()
        {
            Socket?.Dispose();
            Socket = null;
        }
    }
}
