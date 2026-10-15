using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Prognode.Contracts.Devices;
using Prognode.Contracts.Tags;
using Prognode.Protocols.Mitsubishi;

/// <summary>
/// Mitsubishi MC (3E binary): request bytes equal to an independent client's (pymcprotocol), address
/// rules (hex X/Y on Q/L/iQ-R, octal on FX5), low-word-first 32/64-bit values, bit-unit reads,
/// batching, and a refused address isolated to its own Tag.
/// </summary>
internal static class MitsubishiMcContract
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static async Task RunAsync()
    {
        CheckFramesAndAddresses();
        await CheckReadsAsync();
        Console.WriteLine("PASS: Mitsubishi MC 3E binary frames, addresses, values, batching and refused addresses.");
    }

    private static void CheckFramesAndAddresses()
    {
        // Captured from pymcprotocol 0.3.0 (Type3E, plctype "Q") for the same reads, monitoring timer 4 (1 s).
        Check(Hex(McClient.BuildRequest(0xA8, 1000, 18, false, 1000)) == "50 00 00 ff ff 03 00 0c 00 04 00 01 04 00 00 e8 03 00 a8 12 00",
            "D1000 x18 word request differs from pymcprotocol.");
        Check(Hex(McClient.BuildRequest(0x9C, 0x1F, 3, true, 1000)) == "50 00 00 ff ff 03 00 0c 00 04 00 01 04 01 00 1f 00 00 9c 03 00",
            "X1F x3 bit request differs from pymcprotocol.");

        Check(McAddress.Parse("D100", TagDataType.Word) is { Number: 100, Words: 1, Bit: null } a && a.Device.Code == 0xA8, "D100");
        Check(McAddress.Parse("x1f", TagDataType.Bool).Number == 0x1F, "X1F is hexadecimal on Q/L/iQ-R");
        Check(McAddress.Parse("X17", TagDataType.Bool, McSeries.FX5).Number == 15, "X17 is octal on FX5");
        Check(McAddress.Parse("W1A", TagDataType.Int16).Number == 0x1A && McAddress.Parse("SM400", TagDataType.Bool).Device.Code == 0x91, "W1A / SM400");
        Check(McAddress.Parse("D100.A", TagDataType.Bool) is { Bit: 10, Words: 1 }, "Bit of a word");
        Check(McAddress.Parse("D10", TagDataType.Float64).Words == 4 && McAddress.Parse("TN5", TagDataType.Word).Device.Code == 0xC2, "Float64 / TN");
        foreach (var (address, type, series) in new[]
                 {
                     ("X18", TagDataType.Bool, McSeries.FX5), ("M0", TagDataType.Word, McSeries.QLiQR), ("D100", TagDataType.Bool, McSeries.QLiQR),
                     ("M0.1", TagDataType.Bool, McSeries.QLiQR), ("DA", TagDataType.Word, McSeries.QLiQR), ("ZR100", TagDataType.Word, McSeries.QLiQR),
                     ("Q10", TagDataType.Word, McSeries.QLiQR), ("D100.1", TagDataType.Word, McSeries.QLiQR),
                 })
        {
            try { McAddress.Parse(address, type, series); throw new Exception($"Mitsubishi address {address} accepted for {type} on {series}."); }
            catch (ArgumentException) { }
        }
        var validator = new McTagValidator();
        var now = DateTimeOffset.UtcNow;
        var device = Guid.NewGuid();
        var float32 = Tag(device, "F", "D100", TagDataType.Float32, now);
        try { validator.Validate(Tag(device, "Clash", "D101", TagDataType.Word, now), [float32]); throw new Exception("Overlapping D101 accepted."); }
        catch (ArgumentException) { }
        validator.Validate(Tag(device, "Bit0", "D200.0", TagDataType.Bool, now), [Tag(device, "Bit1", "D200.1", TagDataType.Bool, now)]);
    }

    private static async Task CheckReadsAsync()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var memory = new FakePlc();
        memory.Words(0xA8, 1000, [1234]);
        memory.Words(0xA8, 1001, LowFirst(BitConverter.GetBytes((short)-1234)));
        memory.Words(0xA8, 1002, LowFirst(BitConverter.GetBytes(-123456)));
        memory.Words(0xA8, 1006, LowFirst(BitConverter.GetBytes(12.5f)));
        memory.Words(0xA8, 1010, LowFirst(BitConverter.GetBytes(230.75d)));
        memory.Words(0xA8, 1014, LowFirst(BitConverter.GetBytes(5_000_000_000UL)));
        memory.Words(0xA8, 1020, [0b1010]);
        memory.Bits[(0x90, 0)] = true;
        memory.Bits[(0x9C, 0x1F)] = true;
        memory.Bits[(0x9C, 15)] = true; // X17 on FX5 (octal)
        var server = memory.ServeAsync(listener, stop.Token);

        var now = DateTimeOffset.UtcNow;
        var client = new McClient();
        var reader = new McTagReader(client);
        var plc = new DeviceDefinition(Guid.NewGuid(), "iQ-R", McTagReader.Protocol, "Configured", "127.0.0.1", port, (int)McSeries.QLiQR, 1000, now, now);
        var tags = new[]
        {
            Tag(plc.Id, "Word", "D1000", TagDataType.Word, now),
            Tag(plc.Id, "Int16", "D1001", TagDataType.Int16, now),
            Tag(plc.Id, "Int32", "D1002", TagDataType.Int32, now),
            Tag(plc.Id, "Float", "D1006", TagDataType.Float32, now),
            Tag(plc.Id, "Double", "D1010", TagDataType.Float64, now),
            Tag(plc.Id, "Energy", "D1014", TagDataType.UInt64, now),
            Tag(plc.Id, "Bit1", "D1020.1", TagDataType.Bool, now),
            Tag(plc.Id, "Bit2", "D1020.2", TagDataType.Bool, now),
            Tag(plc.Id, "Relay", "M0", TagDataType.Bool, now),
            Tag(plc.Id, "Relay1", "M1", TagDataType.Bool, now),
            Tag(plc.Id, "Input", "X1F", TagDataType.Bool, now),
            Tag(plc.Id, "Refused", "D25000", TagDataType.Word, now),
        };
        var values = await reader.ReadAsync(plc, tags, CancellationToken.None);
        var byName = values.ToDictionary(v => tags.Single(t => t.Id == v.TagId).Name);
        Check(byName["Word"].Value == 1234 && byName["Int16"].Value == -1234 && byName["Int32"].Value == -123456 &&
              byName["Float"].Value == 12.5 && byName["Double"].Value == 230.75 && byName["Energy"].Value == 5_000_000_000d &&
              byName["Bit1"].Value == 1 && byName["Bit2"].Value == 0 && byName["Relay"].Value == 1 && byName["Relay1"].Value == 0 &&
              byName["Input"].Value == 1,
            "Mitsubishi values wrong: " + string.Join(", ", byName.Select(x => $"{x.Key}={x.Value.Value} {x.Value.Quality} {x.Value.Error}")));
        Check(byName["Refused"].Quality == TagQuality.Bad && byName["Refused"].Error!.Contains("C056") &&
              byName.Where(x => x.Key != "Refused").All(x => x.Value.Quality == TagQuality.Good),
            "A refused device number must make only its own Tag Bad.");
        Check(memory.Requests <= 5, $"{memory.Requests} requests; D1000..D1020 should be one batch.");
        Check(memory.Connections == 1, "One persistent connection expected.");

        var fx5 = plc with { Id = Guid.NewGuid(), UnitId = (int)McSeries.FX5 };
        var fxInput = Tag(fx5.Id, "X17", "X17", TagDataType.Bool, now);
        Check((await reader.ReadAsync(fx5, [fxInput], CancellationToken.None)).Single().Value == 1, "FX5 octal X17 must read input 15.");
        Check(client.HasRecentSuccess("127.0.0.1", port, TimeSpan.FromSeconds(30)), "Health tracking missing.");

        stop.Cancel();
        try { await server; } catch (OperationCanceledException) { }
        try { await reader.ReadAsync(plc, [tags[0]], CancellationToken.None); throw new Exception("Read worked with the PLC gone."); }
        catch (IOException) { }
        await client.DisposeAsync();
    }

    private static ushort[] LowFirst(byte[] littleEndian) =>
        Enumerable.Range(0, littleEndian.Length / 2).Select(i => BinaryPrimitives.ReadUInt16LittleEndian(littleEndian.AsSpan(i * 2))).ToArray();

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant().Chunk(2).Select(c => new string(c)).Aggregate((a, b) => a + " " + b);

    private static TagDefinition Tag(Guid deviceId, string name, string address, TagDataType type, DateTimeOffset now) =>
        new(Guid.NewGuid(), deviceId, name, address, type, null, ModbusByteOrder.ABCD, "", 1, 0, 0, true, now, now);

    /// <summary>MC 3E binary responder: batch read in word and bit units; D/W/R at 20000 and above -> C056.</summary>
    private sealed class FakePlc
    {
        public Dictionary<(byte Code, int Number), ushort> WordMap { get; } = [];
        public Dictionary<(byte Code, int Number), bool> Bits { get; } = [];
        public int Requests;
        public int Connections;

        public void Words(byte code, int start, ushort[] words)
        {
            for (var i = 0; i < words.Length; i++) WordMap[(code, start + i)] = words[i];
        }

        public async Task ServeAsync(TcpListener listener, CancellationToken ct)
        {
            using var client = await listener.AcceptTcpClientAsync(ct);
            Interlocked.Increment(ref Connections);
            var stream = client.GetStream();
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var head = new byte[9];
                    await stream.ReadExactlyAsync(head, ct);
                    var body = new byte[head[7] | head[8] << 8];
                    await stream.ReadExactlyAsync(body, ct);
                    Interlocked.Increment(ref Requests);
                    Check(head[0] == 0x50 && head[1] == 0 && head[3] == 0xFF && head[4] == 0xFF && head[5] == 0x03 && body.Length == 12 &&
                          body[2] == 0x01 && body[3] == 0x04, "Unexpected MC request.");
                    var bitUnits = body[4] == 1;
                    var start = body[6] | body[7] << 8 | body[8] << 16;
                    var code = body[9];
                    var count = body[10] | body[11] << 8;
                    byte[] data;
                    ushort endCode = 0;
                    if (!bitUnits && start + count > 20000 && code is 0xA8 or 0xB4 or 0xAF) { endCode = 0xC056; data = new byte[9]; }
                    else if (bitUnits)
                    {
                        data = new byte[(count + 1) / 2];
                        for (var i = 0; i < count; i++)
                            if (Bits.GetValueOrDefault((code, start + i))) data[i / 2] |= (byte)(i % 2 == 0 ? 0x10 : 0x01);
                    }
                    else
                    {
                        data = new byte[count * 2];
                        for (var i = 0; i < count; i++) BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(i * 2), WordMap.GetValueOrDefault((code, start + i)));
                    }
                    var length = 2 + data.Length;
                    byte[] answer = [0xD0, 0x00, head[2], head[3], head[4], head[5], head[6], (byte)length, (byte)(length >> 8), (byte)endCode, (byte)(endCode >> 8), .. data];
                    await stream.WriteAsync(answer, ct);
                }
            }
            catch (Exception ex) when (ex is IOException or EndOfStreamException or OperationCanceledException) { }
        }
    }
}
