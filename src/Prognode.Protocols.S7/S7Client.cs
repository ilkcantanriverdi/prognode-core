using System.Buffers.Binary;
using System.Net.Sockets;
namespace Prognode.Protocols.S7;

/// <summary>The PLC answered ReadVar for one item with an error code (e.g. 0x05 address out of range,
/// 0x0A object does not exist). The session itself stays valid.</summary>
public sealed class S7ItemRejectedException(byte code)
    : IOException($"S7 ReadVar rejected by PLC (0x{code:X2}); check DB access and optimized block setting.")
{
    public byte Code { get; } = code;
}

// ISO-on-TCP (RFC1006), COTP, S7comm SetupCommunication + ReadVar.
// Strictly READ-ONLY: no WriteVar or PLC control endpoints.
public sealed class S7Client : IAsyncDisposable
{
    public bool IsConnected => stream is not null;

    /// <summary>Negotiated PDU size; a ReadVar item may carry at most PduSize - 18 data bytes.</summary>
    public int PduSize => pduSize;

    public int MaxReadBytes => pduSize - 18;

    private TcpClient? socket;
    private NetworkStream? stream;
    private ushort reference = 1;
    private int pduSize = 240;

    public async Task ConnectAsync(string host, int port, int rack, int slot, int timeoutMs, CancellationToken ct)
    {
        if (rack is < 0 or > 7 || slot is < 0 or > 31) throw new ArgumentException("S7 rack 0..7, slot 0..31 required.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(timeoutMs);
        socket = new TcpClient { NoDelay = true };
        try {
            await socket.ConnectAsync(host, port, timeout.Token);
            stream = socket.GetStream();
            // Standard S7 PG TSAP: connection type 0x01 plus rack/slot in low byte.
            var tsap = (ushort)(0x0100 + rack * 32 + slot);
            byte[] connect = [0x03,0x00,0x00,0x16,0x11,0xe0,0,0,0,1,0,0xc1,2,1,0,0xc2,2,(byte)(tsap>>8),(byte)tsap,0xc0,1,0x0a];
            await stream.WriteAsync(connect, timeout.Token);
            var cotp = await ReceiveAsync(timeout.Token);
            if (cotp.Length < 7 || cotp[5] != 0xd0) throw new IOException("S7 COTP connection was rejected (check rack/slot, PLC access and TCP 102).");
            byte[] setup = [0x03,0,0,0x19, 0x02,0xf0,0x80, 0x32,0x01,0,0,0,1,0,8,0,0, 0xf0,0,0,1,0,1,0x01,0xe0];
            await stream.WriteAsync(setup, timeout.Token);
            var reply = await ReceiveAsync(timeout.Token);
            CheckS7(reply, 0xf0);
            var p = 7 + (reply[8] == 3 ? 12 : 10);
            if (reply.Length < p+8) throw new IOException("S7 setup response is truncated.");
            pduSize = BinaryPrimitives.ReadUInt16BigEndian(reply.AsSpan(p+6,2));
            if (pduSize < 64) throw new IOException("S7 negotiated PDU too small.");
        } catch { await DisposeAsync(); throw; }
    }

    public Task<byte[]> ReadAsync(S7Address address, CancellationToken ct) =>
        ReadCoreAsync(address, address.Length == 1, ct);

    /// <summary>Reads a contiguous DB byte range with BYTE transport, also for a single byte.</summary>
    public Task<byte[]> ReadBytesAsync(int db, int startByte, int length, CancellationToken ct)
    {
        if (length < 1) throw new ArgumentOutOfRangeException(nameof(length));
        return ReadCoreAsync(new S7Address(db, startByte, 0, length), false, ct);
    }

    private async Task<byte[]> ReadCoreAsync(S7Address address, bool bitAccess, CancellationToken ct)
    {
        if (stream is null) throw new InvalidOperationException("S7 session not connected.");
        if (address.Length + 18 > pduSize) throw new ArgumentException("S7 read exceeds PLC negotiated PDU.");
        var refId = ++reference;
        var count = bitAccess ? 1 : address.Length;
        var bitAddress = address.ByteOffset * 8 + (bitAccess ? address.Bit : 0);
        byte[] packet = [3,0,0,31, 2,0xf0,0x80, 0x32,1,0,0,(byte)(refId>>8),(byte)refId,0,14,0,0,
            4,1,0x12,0x0a,0x10,(byte)(bitAccess?1:2),(byte)(count>>8),(byte)count,(byte)(address.Db>>8),(byte)address.Db,0x84,
            (byte)(bitAddress>>16),(byte)(bitAddress>>8),(byte)bitAddress];
        await stream.WriteAsync(packet, ct);
        var response = await ReceiveAsync(ct);
        CheckS7(response, 4);
        if (response[11] != (byte)(refId >> 8) || response[12] != (byte)refId)
            throw new IOException("S7 PDU reference mismatch.");
        var hdr = response[8] == 3 ? 12 : 10;
        var paramStart = 7 + hdr;
        var paramLength = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(13,2));
        var dataStart = paramStart + paramLength;
        // A rejected item carries its return code with transport size 0, so check the code first.
        if (response.Length < dataStart+1)
            throw new IOException("S7 ReadVar data truncated.");
        var code = response[dataStart];
        if (code != 0xff) throw new S7ItemRejectedException(code);
        if (response.Length < dataStart+4 || response[dataStart+1] is not (0x03 or 0x04 or 0x09))
            throw new IOException("S7 ReadVar data truncated or unexpected transport size.");
        var declaredBits = BinaryPrimitives.ReadUInt16BigEndian(response.AsSpan(dataStart+2,2));
        if (declaredBits != (bitAccess ? 1 : address.Length * 8))
            throw new IOException("S7 ReadVar response bit length mismatch.");
        var declaredBytes = (declaredBits+7)/8;
        if (declaredBytes < (bitAccess ? 1 : address.Length) || response.Length < dataStart+4+declaredBytes)
            throw new IOException("S7 ReadVar item length mismatch.");
        return response.AsSpan(dataStart+4,bitAccess?1:address.Length).ToArray();
    }

    private static void CheckS7(byte[] frame, byte function)
    {
        if (frame.Length < 20 || frame[4] != 0x02 || frame[5] != 0xf0 || frame[6] != 0x80 || frame[7] != 0x32 || frame[8] != 3)
            throw new IOException("Unexpected S7 acknowledgement.");
        if (frame[17] != 0 || frame[18] != 0)
            throw new IOException($"PLC returned S7 error class/code {frame[17]:X2}/{frame[18]:X2}.");
        var paramsLength = BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(13,2));
        var dataLength = BinaryPrimitives.ReadUInt16BigEndian(frame.AsSpan(15,2));
        if (19 + paramsLength + dataLength != frame.Length || paramsLength < 1 || frame[19] != function)
            throw new IOException("Unexpected S7 response function.");
    }

    private async Task<byte[]> ReceiveAsync(CancellationToken ct)
    {
        if (stream is null) throw new InvalidOperationException("No S7 network stream.");
        var hdr = new byte[4]; await stream.ReadExactlyAsync(hdr,ct);
        if (hdr[0] != 3 || hdr[1] != 0) throw new IOException("Invalid TPKT header.");
        var len = BinaryPrimitives.ReadUInt16BigEndian(hdr.AsSpan(2,2));
        if (len is < 7 or > 8192) throw new IOException("Invalid TPKT frame length.");
        var packet = new byte[len]; Array.Copy(hdr,packet,4);
        await stream.ReadExactlyAsync(packet.AsMemory(4,len-4),ct);
        return packet;
    }

    public ValueTask DisposeAsync() { stream?.Dispose(); socket?.Dispose(); stream=null; socket=null; return ValueTask.CompletedTask; }
}
