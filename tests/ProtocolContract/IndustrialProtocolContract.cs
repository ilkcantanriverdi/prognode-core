using System.Net;
using System.Net.Sockets;
using Prognode.Contracts.Devices;
using Prognode.Contracts.Tags;
using Prognode.Protocols.Modbus;
using Prognode.Protocols.S7;

internal static class IndustrialProtocolContract
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static async Task RunAsync()
    {
        await CheckModbusAsync();
        await CheckS7Async();
    }

    private static async Task CheckModbusAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        var tags = new[]
        {
            Tag(id, "Coil", "00001", TagDataType.Bool, now),
            Tag(id, "Discrete", "10001", TagDataType.Bool, now),
            Tag(id, "Holding", "40001", TagDataType.Word, now),
            Tag(id, "Input", "30001", TagDataType.Word, now)
        };
        var validator = new ModbusTagDefinitionValidator();
        foreach (var tag in tags) validator.Validate(tag, tags.Where(x => x.Id != tag.Id).ToArray());
        try
        {
            validator.Validate(Tag(id, "Invalid", "50001", TagDataType.Word, now), []);
            throw new Exception("Invalid Modbus address accepted.");
        }
        catch (ArgumentException) { }

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var server = ServeModbusAsync(listener, timeout.Token);
        var device = Device(id, "Modbus TCP", port, now);
        var values = await new ModbusTagReader(new ModbusTcpRegisterClient())
            .ReadAsync(device, tags, timeout.Token);
        await server;
        var byName = values.ToDictionary(v => tags.Single(t => t.Id == v.TagId).Name);
        Check(byName.Count == 4 && byName.Values.All(v => v.Quality == TagQuality.Good),
            "Modbus FC01/02/03/04 did not return four Good values.");
        Check(byName["Coil"].Value == 1 && byName["Discrete"].Value == 0 &&
              byName["Holding"].Value == 42 && byName["Input"].Value == 27,
            "Modbus read or Tag mapping failed.");
    }

    private static async Task ServeModbusAsync(TcpListener listener, CancellationToken ct)
    {
        foreach (var (function, payload) in new (byte Function, byte[] Payload)[]
        {
            (0x01, [0x01, 0x01]),
            (0x02, [0x01, 0x00]),
            (0x03, [0x02, 0x00, 0x2a]),
            (0x04, [0x02, 0x00, 0x1b])
        })
        {
            using var client = await listener.AcceptTcpClientAsync(ct);
            var stream = client.GetStream();
            var request = new byte[12];
            await stream.ReadExactlyAsync(request, ct);
            Check(request[7] == function && request[8] == 0 && request[9] == 0 &&
                  request[10] == 0 && request[11] == 1,
                $"Unexpected Modbus FC{function:X2} request.");
            var length = 2 + payload.Length;
            byte[] response = [request[0], request[1], 0, 0, 0, (byte)length,
                request[6], function, .. payload];
            await stream.WriteAsync(response, ct);
        }
    }

    private static async Task CheckS7Async()
    {
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        var tags = new[]
        {
            Tag(id, "Bit", "DB1.DBX0.0", TagDataType.Bool, now),
            Tag(id, "Word", "DB1.DBW2", TagDataType.Word, now)
        };
        var validator = new S7TagValidator();
        foreach (var tag in tags) validator.Validate(tag, tags.Where(x => x.Id != tag.Id).ToArray());
        Check(S7Address.Parse("DB1.DBD4", TagDataType.Float32).Length == 4,
            "S7 double-word address parsing failed.");
        try
        {
            validator.Validate(Tag(id, "Invalid", "DB1.DBD4", TagDataType.Bool, now), []);
            throw new Exception("Invalid S7 BOOL address accepted.");
        }
        catch (ArgumentException) { }

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var server = ServeS7Async(listener, timeout.Token);
        IReadOnlyList<TagValueSnapshot> values;
        try
        {
            values = await new S7TagReader().ReadAsync(
                Device(id, "Siemens S7 TCP", port, now), tags, timeout.Token);
        }
        catch
        {
            await server;
            throw;
        }
        await server;
        var byName = values.ToDictionary(v => tags.Single(t => t.Id == v.TagId).Name);
        Check(byName.Count == 2 && byName.Values.All(v => v.Quality == TagQuality.Good) &&
              byName["Bit"].Value == 1 && byName["Word"].Value == 42,
            "S7 COTP/setup/ReadVar or Tag mapping failed.");
    }

    private static async Task ServeS7Async(TcpListener listener, CancellationToken ct)
    {
        using var client = await listener.AcceptTcpClientAsync(ct);
        var stream = client.GetStream();
        var connect = await ReadFrameAsync(stream, ct);
        Check(connect.Length == 22 && connect[5] == 0xe0 && connect[18] == 0x01,
            "Unexpected S7 COTP connection request.");
        await stream.WriteAsync(new byte[] { 3, 0, 0, 11, 6, 0xd0, 0, 0, 0, 1, 0 }, ct);

        var setup = await ReadFrameAsync(stream, ct);
        Check(setup.Length == 25 && setup[17] == 0xf0, "Unexpected S7 setup request.");
        await stream.WriteAsync(S7Ack(1,
            [0xf0, 0, 0, 1, 0, 1, 0, 0xf0], []), ct);

        foreach (var (reference, transport, bitLength, data) in new (ushort Reference, byte Transport, ushort Bits, byte[] Data)[]
        {
            (2, 0x03, 1, [1]),
            (3, 0x04, 16, [0, 42])
        })
        {
            var read = await ReadFrameAsync(stream, ct);
            Check(read.Length == 31 && read[17] == 4 && read[25] == 0 && read[26] == 1,
                "Unexpected S7 ReadVar request.");
            byte[] item = [0xff, transport, (byte)(bitLength >> 8), (byte)bitLength, .. data];
            await stream.WriteAsync(S7Ack(reference, [4, 1], item), ct);
        }
    }

    private static byte[] S7Ack(ushort reference, byte[] parameters, byte[] data)
    {
        byte[] body = [2, 0xf0, 0x80, 0x32, 3, 0, 0,
            (byte)(reference >> 8), (byte)reference,
            0, (byte)parameters.Length, 0, (byte)data.Length, 0, 0,
            .. parameters, .. data];
        var length = 4 + body.Length;
        return [3, 0, (byte)(length >> 8), (byte)length, .. body];
    }

    private static async Task<byte[]> ReadFrameAsync(NetworkStream stream, CancellationToken ct)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, ct);
        var length = (header[2] << 8) | header[3];
        Check(length is >= 7 and <= 8192, "Invalid simulated PLC frame length.");
        var frame = new byte[length];
        header.CopyTo(frame, 0);
        await stream.ReadExactlyAsync(frame.AsMemory(4), ct);
        return frame;
    }

    private static DeviceDefinition Device(Guid id, string protocol, int port, DateTimeOffset now) =>
        new(id, protocol, protocol, "Configured", "127.0.0.1", port, 1, 250, now, now);

    private static TagDefinition Tag(Guid deviceId, string name, string address,
        TagDataType type, DateTimeOffset now) =>
        new(Guid.NewGuid(), deviceId, name, address, type, null,
            ModbusByteOrder.ABCD, string.Empty, 1, 0, 0, true, now, now);
}
