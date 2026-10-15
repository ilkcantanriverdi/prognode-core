using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Prognode.Contracts.Devices;
using Prognode.Contracts.Tags;
using Prognode.Protocols.Omron;

/// <summary>
/// Omron FINS/TCP: request bytes equal to an independent client's (the `fins` package) apart from the
/// gateway count, the node-address handshake, address rules (D/CIO/W/H/A/EM, decimal bits, T/C PV and
/// completion flags), low-word-first 32/64-bit values, batching, and a refused address isolated to its own Tag.
/// </summary>
internal static class OmronFinsContract
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static async Task RunAsync()
    {
        CheckFramesAndAddresses();
        await CheckReadsAsync();
        Console.WriteLine("PASS: Omron FINS/TCP frames, handshake, addresses, values, batching and refused addresses.");
    }

    private static void CheckFramesAndAddresses()
    {
        // Captured from fins 1.0.5 (TCPFinsConnection, server node 1, client node 0xEF, SID 0x60) for D1000 x18;
        // the library sends gateway count 07, PROGNODE 02 (accepted by every CS/CJ/CP/NJ/NX unit version).
        var frame = FinsClient.BuildRead(1, 0xEF, 0x60, 0x82, 1000, 18);
        Check(Hex(frame[16..]) == "80 00 02 00 01 00 00 ef 00 60 01 01 82 03 e8 00 00 12", "D1000 x18 FINS frame differs from the fins library.");
        Check(Hex(frame[..16]) == "46 49 4e 53 00 00 00 1a 00 00 00 02 00 00 00 00", "FINS/TCP header wrong.");
        Check(Hex(FinsClient.BuildHandshake()) == "46 49 4e 53 00 00 00 0c 00 00 00 00 00 00 00 00 00 00 00 00", "Handshake wrong.");

        Check(FinsAddress.Parse("D100", TagDataType.Word) is { Word: 100, Words: 1, Bit: null, Code: 0x82 }, "D100");
        Check(FinsAddress.Parse("dm100.05", TagDataType.Bool) is { Word: 100, Bit: 5, Code: 0x82 }, "DM alias and bit");
        Check(FinsAddress.Parse("100.07", TagDataType.Bool).Code == 0xB0 && FinsAddress.Parse("CIO100", TagDataType.Word).Code == 0xB0, "CIO with and without prefix");
        Check(FinsAddress.Parse("W10", TagDataType.Int16).Code == 0xB1 && FinsAddress.Parse("H5", TagDataType.Word).Code == 0xB2 &&
              FinsAddress.Parse("A20", TagDataType.Word).Code == 0xB3, "W/H/A area codes");
        Check(FinsAddress.Parse("E2_100", TagDataType.Float32) is { Code: 0xA2, Words: 2 }, "EM bank 2");
        Check(FinsAddress.Parse("T5", TagDataType.Word) is { Code: 0x89, WireWord: 5 } && FinsAddress.Parse("C5", TagDataType.Word).WireWord == 0x8005, "T/C PV");
        Check(FinsAddress.Parse("C5", TagDataType.Bool) is { Code: 0x09, IsFlag: true, WireWord: 0x8005 }, "Counter completion flag");
        Check(FinsAddress.Parse("D10", TagDataType.Float64).Words == 4, "Float64 takes 4 words");
        foreach (var (address, type) in new[]
                 {
                     ("D100", TagDataType.Bool), ("D100.16", TagDataType.Bool), ("D100.1", TagDataType.Word), ("D32767", TagDataType.Int32),
                     ("T0", TagDataType.Int32), ("T0.1", TagDataType.Bool), ("X10", TagDataType.Word), ("DB1.DBW2", TagDataType.Word),
                     ("W512", TagDataType.Word), ("40001", TagDataType.Word),
                 })
        {
            try { FinsAddress.Parse(address, type); throw new Exception($"Omron address {address} accepted for {type}."); }
            catch (ArgumentException) { }
        }
        var validator = new FinsTagValidator();
        var now = DateTimeOffset.UtcNow;
        var device = Guid.NewGuid();
        var float32 = Tag(device, "F", "D100", TagDataType.Float32, now);
        try { validator.Validate(Tag(device, "Clash", "D101", TagDataType.Word, now), [float32]); throw new Exception("Overlapping D101 accepted."); }
        catch (ArgumentException) { }
        validator.Validate(Tag(device, "Bit0", "D200.00", TagDataType.Bool, now), [Tag(device, "Bit1", "D200.01", TagDataType.Bool, now)]);
        validator.Validate(Tag(device, "Done", "T0", TagDataType.Bool, now), [Tag(device, "PV", "T0", TagDataType.Word, now)]);
        validator.Validate(Tag(device, "W1", "W1", TagDataType.Word, now), [Tag(device, "D1", "D1", TagDataType.Word, now)]);
    }

    private static async Task CheckReadsAsync()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var memory = new FakePlc();
        memory.Words(0x82, 1000, [1234]);
        memory.Words(0x82, 1001, LowFirst(BitConverter.GetBytes((short)-1234)));
        memory.Words(0x82, 1002, LowFirst(BitConverter.GetBytes(-123456)));
        memory.Words(0x82, 1006, LowFirst(BitConverter.GetBytes(12.5f)));
        memory.Words(0x82, 1010, LowFirst(BitConverter.GetBytes(230.75d)));
        memory.Words(0x82, 1014, LowFirst(BitConverter.GetBytes(5_000_000_000UL)));
        memory.Words(0x82, 1020, [0b1010]);
        memory.Words(0xB0, 100, [0x0080]);
        memory.Words(0xA0, 100, [777]);
        memory.Words(0x89, 0x8000, [7]);
        memory.Flags[0] = true;
        var server = memory.ServeAsync(listener, stop.Token);

        var now = DateTimeOffset.UtcNow;
        var client = new FinsClient();
        var reader = new FinsTagReader(client);
        var plc = new DeviceDefinition(Guid.NewGuid(), "CJ2", FinsTagReader.Protocol, "Configured", "127.0.0.1", port, 0, 1000, now, now);
        var tags = new[]
        {
            Tag(plc.Id, "Word", "D1000", TagDataType.Word, now),
            Tag(plc.Id, "Int16", "D1001", TagDataType.Int16, now),
            Tag(plc.Id, "Int32", "D1002", TagDataType.Int32, now),
            Tag(plc.Id, "Float", "D1006", TagDataType.Float32, now),
            Tag(plc.Id, "Double", "D1010", TagDataType.Float64, now),
            Tag(plc.Id, "Energy", "D1014", TagDataType.UInt64, now),
            Tag(plc.Id, "Bit1", "D1020.01", TagDataType.Bool, now),
            Tag(plc.Id, "Bit2", "D1020.02", TagDataType.Bool, now),
            Tag(plc.Id, "Output", "CIO100.07", TagDataType.Bool, now),
            Tag(plc.Id, "EM", "E0_100", TagDataType.Word, now),
            Tag(plc.Id, "TimerDone", "T0", TagDataType.Bool, now),
            Tag(plc.Id, "Timer1Done", "T1", TagDataType.Bool, now),
            Tag(plc.Id, "CounterPV", "C0", TagDataType.Word, now),
            Tag(plc.Id, "Refused", "D25000", TagDataType.Word, now),
        };
        var values = await reader.ReadAsync(plc, tags, CancellationToken.None);
        var byName = values.ToDictionary(v => tags.Single(t => t.Id == v.TagId).Name);
        Check(byName["Word"].Value == 1234 && byName["Int16"].Value == -1234 && byName["Int32"].Value == -123456 &&
              byName["Float"].Value == 12.5 && byName["Double"].Value == 230.75 && byName["Energy"].Value == 5_000_000_000d &&
              byName["Bit1"].Value == 1 && byName["Bit2"].Value == 0 && byName["Output"].Value == 1 && byName["EM"].Value == 777 &&
              byName["TimerDone"].Value == 1 && byName["Timer1Done"].Value == 0 && byName["CounterPV"].Value == 7,
            "Omron values wrong: " + string.Join(", ", byName.Select(x => $"{x.Key}={x.Value.Value} {x.Value.Quality} {x.Value.Error}")));
        Check(byName["Refused"].Quality == TagQuality.Bad && byName["Refused"].Error!.Contains("1103") &&
              byName.Where(x => x.Key != "Refused").All(x => x.Value.Quality == TagQuality.Good),
            "A refused address must make only its own Tag Bad.");
        // D1000..D1020 batch (refused, then re-read per Tag), CIO, EM, T flags, C PV.
        Check(memory.Requests <= 7 + 10, $"{memory.Requests} requests; D1000..D1020 should be one batch.");
        Check(memory.Connections == 1 && memory.Handshakes == 1, "One persistent connection with one handshake expected.");
        Check(client.HasRecentSuccess("127.0.0.1", port, TimeSpan.FromSeconds(30)), "Health tracking missing.");

        var before = memory.Requests;
        values = await reader.ReadAsync(plc, tags, CancellationToken.None);
        Check(values.Count == tags.Length && memory.Requests - before <= 6, $"Quarantined Tag must not break the batch again ({memory.Requests - before} requests).");

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

    /// <summary>FINS/TCP responder: handshake (server node 1, client node 0xEF) and memory area read; D at 20000 and above -> 1103.</summary>
    private sealed class FakePlc
    {
        public Dictionary<(byte Code, int Word), ushort> WordMap { get; } = [];
        public Dictionary<int, bool> Flags { get; } = [];
        public int Requests;
        public int Connections;
        public int Handshakes;

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
                    var head = new byte[16];
                    await stream.ReadExactlyAsync(head, ct);
                    var body = new byte[BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(4)) - 8];
                    await stream.ReadExactlyAsync(body, ct);
                    var command = BinaryPrimitives.ReadUInt32BigEndian(head.AsSpan(8));
                    if (command == 0)
                    {
                        Interlocked.Increment(ref Handshakes);
                        await stream.WriteAsync(Tcp(1, [0, 0, 0, 0xEF, 0, 0, 0, 1]), ct);
                        continue;
                    }
                    Interlocked.Increment(ref Requests);
                    Check(command == 2 && body.Length == 18 && body[0] == 0x80 && body[4] == 1 && body[7] == 0xEF && body[10] == 1 && body[11] == 1,
                        "Unexpected FINS request.");
                    var code = body[12];
                    var start = body[13] << 8 | body[14];
                    var count = body[16] << 8 | body[17];
                    byte[] reply = [0xC0, 0, 2, 0, 0xEF, 0, 0, 1, 0, body[9], 1, 1];
                    byte[] data;
                    if (code == 0x82 && start + count > 20000) data = [0x11, 0x03];
                    else if (code == 0x09) data = [0, 0, .. Enumerable.Range(start, count).Select(i => (byte)(Flags.GetValueOrDefault(i) ? 1 : 0))];
                    else
                    {
                        data = new byte[2 + count * 2];
                        for (var i = 0; i < count; i++) BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(2 + i * 2), WordMap.GetValueOrDefault((code, start + i)));
                    }
                    await stream.WriteAsync(Tcp(2, [.. reply, .. data]), ct);
                }
            }
            catch (Exception ex) when (ex is IOException or EndOfStreamException or OperationCanceledException) { }
        }

        private static byte[] Tcp(uint command, byte[] data)
        {
            var frame = new byte[16 + data.Length];
            "FINS"u8.CopyTo(frame);
            BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(4), (uint)(8 + data.Length));
            BinaryPrimitives.WriteUInt32BigEndian(frame.AsSpan(8), command);
            data.CopyTo(frame, 16);
            return frame;
        }
    }
}
