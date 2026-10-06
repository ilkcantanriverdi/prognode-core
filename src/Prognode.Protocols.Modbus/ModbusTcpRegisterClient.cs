using System.Net.Sockets;

namespace Prognode.Protocols.Modbus;

public sealed class ModbusTcpRegisterClient
{
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

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(timeoutMs);

        using var client = new TcpClient();
        await client.ConnectAsync(host, port, timeout.Token);
        var stream = client.GetStream();
        var transactionId = unchecked((ushort)Interlocked.Increment(ref _transactionId));

        var request = new byte[]
        {
            (byte)(transactionId >> 8), (byte)(transactionId & 0xFF),
            0x00, 0x00, 0x00, 0x06, (byte)unitId, functionCode,
            (byte)(startAddress >> 8), (byte)(startAddress & 0xFF),
            (byte)(quantity >> 8), (byte)(quantity & 0xFF)
        };

        await stream.WriteAsync(request, timeout.Token);

        var header = new byte[7];
        await ReadExactlyAsync(stream, header, timeout.Token);

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
        await ReadExactlyAsync(stream, pdu, timeout.Token);

        if (pdu.Length < 1)
            throw new IOException("Empty Modbus PDU.");

        var responseFunction = pdu[0];
        if ((responseFunction & 0x80) != 0)
        {
            if (pdu.Length != 2)
                throw new IOException("Invalid Modbus exception response.");
            throw new IOException($"Modbus exception 0x{pdu[1]:X2} for function 0x{functionCode:X2}.");
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
}
