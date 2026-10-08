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
        await CheckModbusPersistentAndIsolatedAsync();
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

    private static async Task CheckModbusPersistentAndIsolatedAsync()
    {
        // Holding registers 0..4 exist; 5 and above answer exception 0x02 (illegal data address).
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        var tags = new[]
        {
            Tag(id, "R1", "40001", TagDataType.Word, now),
            Tag(id, "R2", "40002", TagDataType.Word, now),
            Tag(id, "R5", "40005", TagDataType.Word, now),
            Tag(id, "Missing", "40006", TagDataType.Word, now),
            Tag(id, "Far", "40010", TagDataType.Word, now)
        };
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var accepted = 0;
        var server = Task.Run(async () =>
        {
            try
            {
                while (!timeout.IsCancellationRequested)
                {
                    using var peer = await listener.AcceptTcpClientAsync(timeout.Token);
                    Interlocked.Increment(ref accepted);
                    var stream = peer.GetStream();
                    var request = new byte[12];
                    while (true)
                    {
                        try { await stream.ReadExactlyAsync(request, timeout.Token); }
                        catch (EndOfStreamException) { break; }
                        var start = (request[8] << 8) | request[9];
                        var quantity = (request[10] << 8) | request[11];
                        byte[] pdu = start + quantity > 5
                            ? [(byte)(request[7] | 0x80), 0x02]
                            : [request[7], (byte)(quantity * 2), .. Enumerable.Range(start, quantity)
                                .SelectMany(r => new byte[] { 0, (byte)(10 + r) })];
                        byte[] response = [request[0], request[1], 0, 0, 0, (byte)(1 + pdu.Length), request[6], .. pdu];
                        await stream.WriteAsync(response, timeout.Token);
                    }
                }
            }
            catch (OperationCanceledException) { }
        });

        var client = new ModbusTcpRegisterClient();
        var reader = new ModbusTagReader(client);
        var device = Device(id, "Modbus TCP", port, now);
        var first = await reader.ReadAsync(device, tags, timeout.Token);
        var second = await reader.ReadAsync(device, tags, timeout.Token);
        var healthy = client.HasRecentSuccess("127.0.0.1", port, TimeSpan.FromSeconds(30));
        timeout.Cancel();
        await server;
        await client.DisposeAsync();

        var byName = second.ToDictionary(v => tags.Single(t => t.Id == v.TagId).Name);
        Check(first.Count == 5 && byName.Count == 5, "Modbus reader dropped Tags.");
        Check(byName["R1"].Quality == TagQuality.Good && byName["R1"].Value == 10 &&
              byName["R2"].Value == 11 && byName["R5"].Quality == TagQuality.Good && byName["R5"].Value == 14,
            "A rejected neighbour made valid Modbus Tags BAD.");
        Check(byName["Missing"].Quality == TagQuality.Bad && byName["Missing"].Error!.Contains("0x02") &&
              byName["Far"].Quality == TagQuality.Bad,
            "Illegal Modbus addresses were not isolated to their own Tags.");
        Check(accepted == 1, $"Modbus polls opened {accepted} connections instead of reusing one.");
        Check(healthy, "Health probe cannot see the healthy polling connection.");
    }

    private static async Task CheckS7Async()
    {
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        var tags = new[]
        {
            Tag(id, "Bit", "DB1.DBX0.0", TagDataType.Bool, now),
            Tag(id, "Bit3", "DB1.DBX0.3", TagDataType.Bool, now),
            Tag(id, "Word", "DB1.DBW2", TagDataType.Word, now),
            Tag(id, "Word6", "DB1.DBW6", TagDataType.Word, now),
            Tag(id, "Outside", "DB1.DBW9", TagDataType.Word, now),
            Tag(id, "OtherDb", "DB2.DBW0", TagDataType.Word, now)
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
        var stats = new S7ServerStats();
        // DB1 is 10 bytes; DB2 does not exist.
        byte[] db1 = [0b0000_1001, 0, 0, 42, 0, 0, 0, 7, 0, 0];
        var server = ServeS7Async(listener, db1, stats, timeout.Token);
        var pool = new S7SessionPool();
        var reader = new S7TagReader(pool);
        var device = Device(id, "Siemens S7 TCP", port, now);
        IReadOnlyList<TagValueSnapshot> values;
        bool healthy;
        try
        {
            await reader.ReadAsync(device, tags, timeout.Token);
            values = await reader.ReadAsync(device, tags, timeout.Token);
            healthy = pool.HasRecentSuccess(id, TimeSpan.FromSeconds(30));
        }
        finally
        {
            await pool.DisposeAsync();
            timeout.Cancel();
            await server;
        }
        var byName = values.ToDictionary(v => tags.Single(t => t.Id == v.TagId).Name);
        Check(byName.Count == 6 && byName["Bit"].Value == 1 && byName["Bit3"].Value == 1 &&
              byName["Word"].Value == 42 && byName["Word6"].Value == 7 &&
              new[] { "Bit", "Bit3", "Word", "Word6" }.All(n => byName[n].Quality == TagQuality.Good),
            "S7 range read, fallback or Tag mapping failed.");
        Check(byName["Outside"].Quality == TagQuality.Bad && byName["OtherDb"].Quality == TagQuality.Bad,
            "Rejected S7 addresses were not isolated to their own Tags.");
        Check(stats.Connections == 1, $"S7 polls opened {stats.Connections} sessions instead of reusing one.");
        Check(stats.Reads < 2 * tags.Length, $"S7 issued {stats.Reads} ReadVar requests; ranges were not merged.");
        Check(healthy, "S7 health probe cannot see the healthy polling session.");
    }

    private sealed class S7ServerStats { public int Connections; public int Reads; }

    private static async Task ServeS7Async(TcpListener listener, byte[] db1, S7ServerStats stats, CancellationToken ct)
    {
        try
        {
            using var peer = await listener.AcceptTcpClientAsync(ct);
            Interlocked.Increment(ref stats.Connections);
            var stream = peer.GetStream();
            var connect = await ReadFrameAsync(stream, ct);
            Check(connect.Length == 22 && connect[5] == 0xe0 && connect[18] == 0x01,
                "Unexpected S7 COTP connection request.");
            await stream.WriteAsync(new byte[] { 3, 0, 0, 11, 6, 0xd0, 0, 0, 0, 1, 0 }, ct);

            var setup = await ReadFrameAsync(stream, ct);
            Check(setup.Length == 25 && setup[17] == 0xf0, "Unexpected S7 setup request.");
            await stream.WriteAsync(S7Ack(1, [0xf0, 0, 0, 1, 0, 1, 0, 0xf0], []), ct);

            while (!ct.IsCancellationRequested)
            {
                var read = await ReadFrameAsync(stream, ct);
                Interlocked.Increment(ref stats.Reads);
                Check(read.Length == 31 && read[17] == 4 && read[27] == 0x84, "Unexpected S7 ReadVar request.");
                var reference = (ushort)((read[11] << 8) | read[12]);
                var transport = read[22];
                var count = (read[23] << 8) | read[24];
                var db = (read[25] << 8) | read[26];
                var bitAddress = (read[28] << 16) | (read[29] << 8) | read[30];
                var start = bitAddress / 8;
                var length = transport == 1 ? 1 : count;
                byte[] item;
                if (db != 1 || start + length > db1.Length)
                    item = [0x05, 0x00, 0, 0];
                else if (transport == 1)
                    item = [0xff, 0x03, 0, 1, (byte)((db1[start] >> (bitAddress % 8)) & 1)];
                else
                    item = [0xff, 0x04, (byte)((length * 8) >> 8), (byte)(length * 8), .. db1.AsSpan(start, length).ToArray()];
                await stream.WriteAsync(S7Ack(reference, [4, 1], item), ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (EndOfStreamException) { }
        catch (IOException) { }
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
