using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Prognode.Contracts.Devices;
using Prognode.Contracts.Tags;
using Prognode.Protocols.EtherNetIp;

/// <summary>
/// EtherNet/IP (Logix): symbolic paths equal to an independent encoder's (pycomm3), RegisterSession,
/// SendRRData, Unconnected Send to the CPU slot, Multiple Service Packet with per-reply status, data
/// types decoded by the controller's type code, type mismatches named, bits of integers, route errors.
/// </summary>
internal static class EtherNetIpContract
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static async Task RunAsync()
    {
        CheckPaths();
        await CheckReadsAsync();
        Console.WriteLine("PASS: EtherNet/IP Logix paths, session, routing, multiple-service reads, data types and refusals.");
    }

    private static void CheckPaths()
    {
        // Captured from pycomm3 1.2.16 tag_request_path (without its leading word count). pycomm3 encodes
        // 32-bit indexes as 0x2B; the CIP logical format for 32 bits is 0x2A, which cpppo answers correctly.
        foreach (var (tag, expected) in new[]
                 {
                     ("Speed", "91 05 53 70 65 65 64 00"),
                     ("Tank.Level", "91 04 54 61 6e 6b 91 05 4c 65 76 65 6c 00"),
                     ("Counters[3]", "91 08 43 6f 75 6e 74 65 72 73 28 03"),
                     ("Arr[300]", "91 03 41 72 72 00 29 00 2c 01"),
                     ("Arr[70000]", "91 03 41 72 72 00 2a 00 70 11 01 00"),
                     ("Grid[1,2]", "91 04 47 72 69 64 28 01 28 02"),
                     ("Program:MainProgram.Motor_1.Amps", "91 13 50 72 6f 67 72 61 6d 3a 4d 61 69 6e 50 72 6f 67 72 61 6d 00 91 07 4d 6f 74 6f 72 5f 31 00 91 04 41 6d 70 73"),
                 })
            Check(Hex(LogixTagPath.Parse(tag, TagDataType.Word).Path) == expected, $"Logix path {tag} differs from pycomm3.");
        var bit = LogixTagPath.Parse("Status.5", TagDataType.Bool);
        Check(bit.Bit == 5 && bit.Name == "Status", "Bit of an integer tag.");
        foreach (var (address, type) in new[]
                 {
                     ("Status.5", TagDataType.Word), ("1Tag", TagDataType.Word), ("Tag[1,2,3,4]", TagDataType.Word), ("Tag.64", TagDataType.Bool),
                     ("", TagDataType.Word), ("Tag-1", TagDataType.Word), ("Program:Main", TagDataType.Word),
                 })
        {
            try { LogixTagPath.Parse(address, type); throw new Exception($"Logix address '{address}' accepted for {type}."); }
            catch (ArgumentException) { }
        }
        Check(Hex(EnipClient.UnconnectedSend([0x4C, 0x01, 0x91, 0x00, 0x01], 2)) == "52 02 20 06 24 01 0a 05 05 00 4c 01 91 00 01 00 01 00 01 02",
            "Unconnected Send framing (odd message padded, route port 1 slot 2).");
    }

    private static async Task CheckReadsAsync()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var plc = new FakeLogix(slot: 0);
        plc.Tags["Word"] = (0xC7, BitConverter.GetBytes((ushort)1234));
        plc.Tags["Int16"] = (0xC3, BitConverter.GetBytes((short)-1234));
        plc.Tags["Int32"] = (0xC4, BitConverter.GetBytes(-123456));
        plc.Tags["UInt32"] = (0xC8, BitConverter.GetBytes(4_000_000_000u));
        plc.Tags["Speed"] = (0xCA, BitConverter.GetBytes(12.5f));
        plc.Tags["Lreal"] = (0xCB, BitConverter.GetBytes(230.75d));
        plc.Tags["Energy"] = (0xC9, BitConverter.GetBytes(5_000_000_000UL));
        plc.Tags["Status"] = (0xC4, BitConverter.GetBytes(0b1010));
        plc.Tags["Running"] = (0xC1, [0xFF]);
        plc.Tags["Small"] = (0xC2, [0xFE]);
        plc.Tags["Counters[3]"] = (0xC3, BitConverter.GetBytes((short)7));
        plc.Tags["Program:Main.Motor.Amps"] = (0xCA, BitConverter.GetBytes(3.25f));
        plc.Tags["Recipe"] = (0x02A0, [0x12, 0x34, 1, 2, 3, 4]);
        var server = plc.ServeAsync(listener, stop.Token);

        var now = DateTimeOffset.UtcNow;
        var client = new EnipClient();
        var reader = new LogixTagReader(client);
        var device = new DeviceDefinition(Guid.NewGuid(), "L33ER", LogixTagReader.Protocol, "Configured", "127.0.0.1", port, 0, 1000, now, now);
        var tags = new[]
        {
            Tag(device.Id, "Word", "Word", TagDataType.Word, now),
            Tag(device.Id, "Int16", "Int16", TagDataType.Int16, now),
            Tag(device.Id, "Int32", "Int32", TagDataType.Int32, now),
            Tag(device.Id, "UInt32", "UInt32", TagDataType.UInt32, now),
            Tag(device.Id, "Speed", "Speed", TagDataType.Float32, now),
            Tag(device.Id, "Lreal", "Lreal", TagDataType.Float64, now),
            Tag(device.Id, "Energy", "Energy", TagDataType.UInt64, now),
            Tag(device.Id, "Bit1", "Status.1", TagDataType.Bool, now),
            Tag(device.Id, "Bit2", "Status.2", TagDataType.Bool, now),
            Tag(device.Id, "Running", "Running", TagDataType.Bool, now),
            Tag(device.Id, "Small", "Small", TagDataType.Int16, now),
            Tag(device.Id, "Element", "Counters[3]", TagDataType.Int16, now),
            Tag(device.Id, "Scoped", "Program:Main.Motor.Amps", TagDataType.Float32, now),
            Tag(device.Id, "Missing", "Missing", TagDataType.Word, now),
            Tag(device.Id, "Mismatch", "Speed", TagDataType.Int32, now),
            Tag(device.Id, "Struct", "Recipe", TagDataType.Word, now),
            Tag(device.Id, "RealBit", "Speed.1", TagDataType.Bool, now),
        };
        var values = await reader.ReadAsync(device, tags, CancellationToken.None);
        var byName = values.ToDictionary(v => tags.Single(t => t.Id == v.TagId).Name);
        Check(byName["Word"].Value == 1234 && byName["Int16"].Value == -1234 && byName["Int32"].Value == -123456 &&
              byName["UInt32"].Value == 4_000_000_000d && byName["Speed"].Value == 12.5 && byName["Lreal"].Value == 230.75 &&
              byName["Energy"].Value == 5_000_000_000d && byName["Bit1"].Value == 1 && byName["Bit2"].Value == 0 &&
              byName["Running"].Value == 1 && byName["Small"].Value == -2 && byName["Element"].Value == 7 && byName["Scoped"].Value == 3.25,
            "Logix values wrong: " + string.Join(", ", byName.Select(x => $"{x.Key}={x.Value.Value} {x.Value.Quality} {x.Value.Error}")));
        Check(byName["Missing"].Quality == TagQuality.Bad && byName["Missing"].Error!.Contains("not found"), "Missing tag: " + byName["Missing"].Error);
        Check(byName["Mismatch"].Quality == TagQuality.Bad && byName["Mismatch"].Error!.Contains("REAL"), "Type mismatch must name the controller type: " + byName["Mismatch"].Error);
        Check(byName["Struct"].Quality == TagQuality.Bad && byName["Struct"].Error!.Contains("member"), "Structure: " + byName["Struct"].Error);
        Check(byName["RealBit"].Quality == TagQuality.Bad, "A bit of a REAL must be refused.");
        Check(plc.Requests == 1 && plc.MultipleService == 1 && plc.Sessions == 1 && plc.Routed == 1,
            $"17 reads should be one routed Multiple Service Packet in one session ({plc.Requests} requests, {plc.Sessions} sessions).");

        // 30 Tags do not fit one packet: two requests.
        var many = Enumerable.Range(0, 30).Select(i => Tag(device.Id, $"W{i}", "Word", TagDataType.Word, now)).ToArray();
        var manyValues = await reader.ReadAsync(device, many, CancellationToken.None);
        Check(manyValues.All(v => v.Value == 1234) && plc.Requests == 3, $"30 reads: {plc.Requests - 1} packets.");

        // A wrong CPU slot: the route error names the slot, the session stays.
        var wrongSlot = device with { Id = Guid.NewGuid(), UnitId = 3 };
        var routed = await reader.ReadAsync(wrongSlot, [tags[0], tags[4]], CancellationToken.None);
        Check(routed.All(v => v.Quality == TagQuality.Bad && v.Error!.Contains("slot")), "Wrong slot: " + routed[0].Error);
        Check(plc.Sessions == 1, "A route error must not drop the session.");

        // Direct (Micro800): no Unconnected Send.
        var direct = device with { Id = Guid.NewGuid(), UnitId = EnipClient.DirectRoute };
        Check((await reader.ReadAsync(direct, [tags[4]], CancellationToken.None)).Single().Value == 12.5 && plc.Direct == 1, "Direct read.");

        // The controller drops the connection: the next read opens a new session.
        plc.DropNext = true;
        Check((await reader.ReadAsync(device, [tags[0]], CancellationToken.None)).Single().Value == 1234 && plc.Sessions == 2, "Reconnect after a dropped session.");
        Check(client.HasRecentSuccess("127.0.0.1", port, TimeSpan.FromSeconds(30)), "Health tracking missing.");

        stop.Cancel();
        try { await server; } catch (OperationCanceledException) { }
        listener.Stop();
        try { await reader.ReadAsync(device, [tags[0]], CancellationToken.None); throw new Exception("Read worked with the controller gone."); }
        catch (IOException) { }
        await client.DisposeAsync();
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant().Chunk(2).Select(c => new string(c)).Aggregate((a, b) => a + " " + b);

    private static TagDefinition Tag(Guid deviceId, string name, string address, TagDataType type, DateTimeOffset now) =>
        new(Guid.NewGuid(), deviceId, name, address, type, null, ModbusByteOrder.ABCD, "", 1, 0, 0, true, now, now);

    /// <summary>Logix responder: RegisterSession, SendRRData, Unconnected Send (only to its slot), Multiple Service, Read Tag by name.</summary>
    private sealed class FakeLogix(int slot)
    {
        public Dictionary<string, (ushort Type, byte[] Data)> Tags { get; } = new(StringComparer.OrdinalIgnoreCase);
        public int Sessions, Requests, MultipleService, Routed, Direct;
        public volatile bool DropNext;

        public async Task ServeAsync(TcpListener listener, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                using var client = await listener.AcceptTcpClientAsync(ct);
                var stream = client.GetStream();
                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        var head = new byte[24];
                        await stream.ReadExactlyAsync(head, ct);
                        var body = new byte[BinaryPrimitives.ReadUInt16LittleEndian(head.AsSpan(2))];
                        await stream.ReadExactlyAsync(body, ct);
                        var command = BinaryPrimitives.ReadUInt16LittleEndian(head);
                        if (command == 0x65)
                        {
                            Sessions++;
                            BinaryPrimitives.WriteUInt32LittleEndian(head.AsSpan(4), 0x1000u + (uint)Sessions);
                            await stream.WriteAsync((byte[])[.. head, .. body], ct);
                            continue;
                        }
                        Check(command == 0x6F && BinaryPrimitives.ReadUInt32LittleEndian(head.AsSpan(4)) == 0x1000u + (uint)Sessions, "Unexpected encapsulation or session.");
                        if (DropNext) { DropNext = false; break; }
                        Requests++;
                        Check(BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(6)) == 2 && body[12] == 0xB2, "CPF items.");
                        var cip = body[16..(16 + BinaryPrimitives.ReadUInt16LittleEndian(body.AsSpan(14)))];
                        byte[] reply;
                        if (cip[0] == 0x52)
                        {
                            Routed++;
                            var length = BinaryPrimitives.ReadUInt16LittleEndian(cip.AsSpan(8));
                            var message = cip[10..(10 + length)];
                            var route = cip[(10 + length + length % 2)..];
                            Check(route[0] == 1 && route[2] == 1, "Route path must be one word: port 1, slot.");
                            reply = route[3] == slot ? Answer(message) : [0xD2, 0, 0x01, 0x01, 0x11, 0x03];
                        }
                        else { Direct++; reply = Answer(cip); }
                        byte[] data = [.. body[..6], 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0xB2, 0x00, (byte)reply.Length, (byte)(reply.Length >> 8), .. reply];
                        BinaryPrimitives.WriteUInt16LittleEndian(head.AsSpan(2), (ushort)data.Length);
                        await stream.WriteAsync((byte[])[.. head, .. data], ct);
                    }
                }
                catch (Exception ex) when (ex is IOException or EndOfStreamException or OperationCanceledException) { }
            }
        }

        private byte[] Answer(byte[] request)
        {
            if (request[0] == 0x0A)
            {
                MultipleService++;
                var services = request[6..];
                var count = BinaryPrimitives.ReadUInt16LittleEndian(services);
                var replies = new List<byte[]>();
                for (var i = 0; i < count; i++)
                {
                    int start = BinaryPrimitives.ReadUInt16LittleEndian(services.AsSpan(2 + 2 * i));
                    var end = i + 1 < count ? BinaryPrimitives.ReadUInt16LittleEndian(services.AsSpan(4 + 2 * i)) : services.Length;
                    replies.Add(ReadTag(services[start..end]));
                }
                var offsets = new List<byte> { (byte)count, (byte)(count >> 8) };
                var at = 2 + 2 * count;
                foreach (var r in replies) { offsets.Add((byte)at); offsets.Add((byte)(at >> 8)); at += r.Length; }
                var failed = replies.Any(r => r[2] != 0);
                return [0x8A, 0, (byte)(failed ? 0x1E : 0), 0, .. offsets, .. replies.SelectMany(r => r)];
            }
            return ReadTag(request);
        }

        private byte[] ReadTag(byte[] request)
        {
            Check(request[0] == 0x4C, "Read Tag expected.");
            var path = request[2..(2 + 2 * request[1])];
            var name = new StringBuilder();
            for (var i = 0; i < path.Length;)
            {
                if (path[i] == 0x91)
                {
                    if (name.Length > 0) name.Append('.');
                    name.Append(Encoding.ASCII.GetString(path, i + 2, path[i + 1]));
                    i += 2 + path[i + 1] + path[i + 1] % 2;
                }
                else if (path[i] == 0x28) { name.Append('[').Append(path[i + 1]).Append(']'); i += 2; }
                else throw new Exception("Unexpected path segment.");
            }
            if (!Tags.TryGetValue(name.ToString(), out var tag)) return [0xCC, 0, 0x05, 0];
            byte[] type = tag.Type == 0x02A0 ? [0xA0, 0x02] : [(byte)tag.Type, (byte)(tag.Type >> 8)];
            return [0xCC, 0, 0, 0, .. type, .. tag.Data];
        }
    }
}
