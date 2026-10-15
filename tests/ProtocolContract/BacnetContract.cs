using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using Prognode.Contracts.Devices;
using Prognode.Contracts.Tags;
using Prognode.Protocols.Bacnet;

/// <summary>
/// BACnet/IP: request bytes equal to an independent encoder's (bacpypes3), object addresses, Present_Value
/// of each application type, per-object errors, type mismatches named, fallback to ReadProperty when a
/// device rejects ReadPropertyMultiple or refuses a batch, late answers skipped, an absent device.
/// </summary>
internal static class BacnetContract
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static async Task RunAsync()
    {
        CheckFramesAndAddresses();
        await CheckReadsAsync();
        Console.WriteLine("PASS: BACnet/IP frames, object addresses, value types, per-object errors, fallbacks and late answers.");
    }

    private static void CheckFramesAndAddresses()
    {
        // APDUs captured from bacpypes3 0.0.110 (invoke 5 / 9, no segmentation, max APDU 1476).
        var rpm = BacnetClient.Frame(5, 0x0E, BacnetClient.ReadPropertyMultipleService(
            [BacnetObjectAddress.Parse("AV:1", TagDataType.Float32), BacnetObjectAddress.Parse("BI:7", TagDataType.Bool),
             BacnetObjectAddress.Parse("MSV:4194302", TagDataType.UInt16)]));
        Check(Hex(rpm[6..]) == "00 05 05 0e 0c 00 80 00 01 1e 09 55 1f 0c 00 c0 00 07 1e 09 55 1f 0c 04 ff ff fe 1e 09 55 1f",
            "ReadPropertyMultiple APDU differs from bacpypes3.");
        Check(Hex(rpm[..6]) == "81 0a 00 25 01 04", "BVLC / NPDU header.");
        var rp = BacnetClient.Frame(9, 0x0C, BacnetClient.ReadPropertyService(BacnetObjectAddress.Parse("analog-input,1", TagDataType.Float32)));
        Check(Hex(rp[6..]) == "00 05 09 0c 0c 00 00 00 01 19 55", "ReadProperty APDU differs from bacpypes3.");

        Check(BacnetObjectAddress.Parse("AI:1", TagDataType.Float32) is { Instance: 1, Type.Code: 0 }, "AI:1");
        Check(BacnetObjectAddress.Parse("analog-value:12", TagDataType.Float32).Type.Short == "AV", "full name");
        Check(BacnetObjectAddress.Parse("bv 3", TagDataType.Bool).Type.Code == 5 && BacnetObjectAddress.Parse("MSV2", TagDataType.UInt16).Instance == 2, "BV 3 / MSV2");
        Check(BacnetObjectAddress.Parse("multiStateValue,7", TagDataType.Word).Type.Code == 19, "camel-case name");
        foreach (var (address, type) in new[]
                 {
                     ("BI:1", TagDataType.Float32), ("AI:1", TagDataType.Bool), ("XX:1", TagDataType.Word), ("AI:4194303", TagDataType.Float32),
                     ("AI", TagDataType.Float32), ("40001", TagDataType.Word), ("device:1", TagDataType.Word),
                 })
        {
            try { BacnetObjectAddress.Parse(address, type); throw new Exception($"BACnet address {address} accepted for {type}."); }
            catch (ArgumentException) { }
        }
    }

    private static async Task CheckReadsAsync()
    {
        using var stop = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var device = new FakeDevice();
        device.Values[(2, 1)] = [0x44, .. BitConverter.GetBytes(12.5f).Reverse()];           // AV:1 REAL
        device.Values[(46, 1)] = [0x55, 0x08, .. BitConverter.GetBytes(230.75d).Reverse()];  // LAV:1 DOUBLE
        device.Values[(3, 1)] = [0x91, 0x01];                                                // BI:1 active
        device.Values[(5, 2)] = [0x91, 0x00];                                                // BV:2 inactive
        device.Values[(19, 1)] = [0x21, 0x03];                                               // MSV:1 3
        device.Values[(23, 1)] = [0x24, 0x00, 0x01, 0x86, 0xA0];                             // ACC:1 100000
        device.Values[(45, 1)] = [0x32, 0xFB, 0x2E];                                         // IV:1 -1234
        device.Values[(1, 1)] = [0x00];                                                      // AO:1 NULL
        var server = device.ServeAsync(stop.Token);

        var now = DateTimeOffset.UtcNow;
        var client = new BacnetClient();
        var reader = new BacnetTagReader(client);
        var plc = new DeviceDefinition(Guid.NewGuid(), "AHU-1", BacnetTagReader.Protocol, "Configured", "127.0.0.1", device.Port, 0, 1000, now, now);
        var tags = new[]
        {
            Tag(plc.Id, "Temp", "AV:1", TagDataType.Float32, now),
            Tag(plc.Id, "Energy", "LAV:1", TagDataType.Float64, now),
            Tag(plc.Id, "Fan", "BI:1", TagDataType.Bool, now),
            Tag(plc.Id, "Alarm", "BV:2", TagDataType.Bool, now),
            Tag(plc.Id, "Stage", "MSV:1", TagDataType.UInt16, now),
            Tag(plc.Id, "Count", "ACC:1", TagDataType.UInt32, now),
            Tag(plc.Id, "Offset", "IV:1", TagDataType.Int16, now),
            Tag(plc.Id, "Missing", "AV:99", TagDataType.Float32, now),
            Tag(plc.Id, "Mismatch", "AV:1", TagDataType.Int32, now),
            Tag(plc.Id, "Relinquished", "AO:1", TagDataType.Float32, now),
        };
        var values = await reader.ReadAsync(plc, tags, CancellationToken.None);
        var byName = values.ToDictionary(v => tags.Single(t => t.Id == v.TagId).Name);
        Check(byName["Temp"].Value == 12.5 && byName["Energy"].Value == 230.75 && byName["Fan"].Value == 1 && byName["Alarm"].Value == 0 &&
              byName["Stage"].Value == 3 && byName["Count"].Value == 100000 && byName["Offset"].Value == -1234,
            "BACnet values wrong: " + string.Join(", ", byName.Select(x => $"{x.Key}={x.Value.Value} {x.Value.Quality} {x.Value.Error}")));
        Check(byName["Missing"].Quality == TagQuality.Bad && byName["Missing"].Error!.Contains("unknown-object"), "Missing: " + byName["Missing"].Error);
        Check(byName["Mismatch"].Quality == TagQuality.Bad && byName["Mismatch"].Error!.Contains("REAL"), "Mismatch: " + byName["Mismatch"].Error);
        Check(byName["Relinquished"].Quality == TagQuality.Bad && byName["Relinquished"].Error!.Contains("NULL"), "NULL present value.");
        Check(device.Rpm == 1 && device.Rp == 0, $"Ten objects should be one ReadPropertyMultiple ({device.Rpm} RPM, {device.Rp} RP).");

        // 25 objects: two requests.
        var many = Enumerable.Range(0, 25).Select(i => Tag(plc.Id, $"T{i}", "AV:1", TagDataType.Float32, now)).ToArray();
        Check((await reader.ReadAsync(plc, many, CancellationToken.None)).All(v => v.Value == 12.5) && device.Rpm == 3, "25 objects: two requests.");

        // A late answer to an earlier request (wrong invoke id) is skipped.
        device.SendStaleFirst = true;
        Check((await reader.ReadAsync(plc, [tags[0]], CancellationToken.None)).Single().Value == 12.5, "Late answer must be skipped.");

        // A device that refuses the whole request when one object is missing: that batch is read object by object.
        device.RefuseBatchWithMissing = true;
        values = await reader.ReadAsync(plc, [tags[0], tags[7]], CancellationToken.None);
        Check(values[0].Value == 12.5 && values[1].Quality == TagQuality.Bad && device.Rp == 2, "Batch refusal fallback.");
        device.RefuseBatchWithMissing = false;

        // A device without ReadPropertyMultiple (Reject unrecognized-service): ReadProperty from then on.
        device.RejectRpm = true;
        var rpBefore = device.Rp;
        values = await reader.ReadAsync(plc, tags[..3], CancellationToken.None);
        Check(values.Select(v => v.Value).SequenceEqual([12.5, 230.75, 1]) && device.Rp - rpBefore == 3, "ReadProperty fallback.");
        var rpmBefore = device.Rpm;
        await reader.ReadAsync(plc, tags[..2], CancellationToken.None);
        Check(device.Rpm == rpmBefore, "ReadPropertyMultiple must not be retried after a reject.");
        Check(client.HasRecentSuccess("127.0.0.1", device.Port, TimeSpan.FromSeconds(30)), "Health tracking missing.");
        Check(await client.PingAsync("127.0.0.1", device.Port, 1000, CancellationToken.None) == "AHU-1 controller", "Ping returns the device name.");

        stop.Cancel();
        try { await server; } catch (OperationCanceledException) { }
        device.Dispose();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try { await reader.ReadAsync(plc, [tags[0]], CancellationToken.None); throw new Exception("Read worked with the device gone."); }
        catch (IOException) { }
        Check(watch.Elapsed < TimeSpan.FromSeconds(4), "An absent device must fail within the timeout.");
        await client.DisposeAsync();
    }

    private static string Hex(byte[] bytes) => Convert.ToHexString(bytes).ToLowerInvariant().Chunk(2).Select(c => new string(c)).Aggregate((a, b) => a + " " + b);

    private static TagDefinition Tag(Guid deviceId, string name, string address, TagDataType type, DateTimeOffset now) =>
        new(Guid.NewGuid(), deviceId, name, address, type, null, ModbusByteOrder.ABCD, "", 1, 0, 0, true, now, now);

    /// <summary>BACnet/IP device: ReadPropertyMultiple / ReadProperty of Present_Value, unknown objects as errors.</summary>
    private sealed class FakeDevice : IDisposable
    {
        private readonly UdpClient _socket = new(new IPEndPoint(IPAddress.Loopback, 0));
        public Dictionary<(int Type, int Instance), byte[]> Values { get; } = [];
        public int Port => ((IPEndPoint)_socket.Client.LocalEndPoint!).Port;
        public int Rpm, Rp;
        public volatile bool RejectRpm, SendStaleFirst, RefuseBatchWithMissing;

        public async Task ServeAsync(CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    var request = await _socket.ReceiveAsync(ct);
                    var b = request.Buffer;
                    Check(b[0] == 0x81 && b[1] == 0x0A && b[4] == 0x01 && b[5] == 0x04 && b[6] == 0x00 && b[7] == 0x05, "Unexpected BVLC/NPDU/APCI.");
                    var invoke = b[8];
                    var service = b[9];
                    var data = b[10..];
                    byte[] apdu;
                    if (service == 0x0E)
                    {
                        Rpm++;
                        var ids = Enumerable.Range(0, data.Length / 9).Select(i => BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(i * 9 + 1))).ToArray();
                        if (RejectRpm) apdu = [0x60, invoke, 9];
                        else if (RefuseBatchWithMissing && ids.Any(id => !Values.ContainsKey(Split(id)))) apdu = [0x50, invoke, 0x0E, 0x91, 1, 0x91, 31];
                        else
                        {
                            var body = new List<byte> { 0x30, invoke, 0x0E };
                            foreach (var id in ids)
                            {
                                body.AddRange([0x0C, .. BigEndian(id), 0x1E, 0x29, 0x55]);
                                if (Values.TryGetValue(Split(id), out var value)) body.AddRange([0x4E, .. value, 0x4F]);
                                else body.AddRange([0x5E, 0x91, 1, 0x91, 31, 0x5F]);
                                body.Add(0x1F);
                            }
                            apdu = [.. body];
                        }
                    }
                    else
                    {
                        Rp++;
                        var id = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(1));
                        var property = data[6];
                        if (Split(id) == (8, 4194303) && property == 77)
                            apdu = [0x30, invoke, 0x0C, 0x0C, .. BigEndian(8u << 22 | 2401), 0x19, 77, 0x3E, 0x75, 17, 0x00, .. "AHU-1 controller"u8, 0x3F];
                        else if (Values.TryGetValue(Split(id), out var value))
                            apdu = [0x30, invoke, 0x0C, 0x0C, .. BigEndian(id), 0x19, 0x55, 0x3E, .. value, 0x3F];
                        else apdu = [0x50, invoke, 0x0C, 0x91, 1, 0x91, 31];
                    }
                    if (SendStaleFirst)
                    {
                        // An answer to an earlier invoke id, with a different value (0.0): must be ignored.
                        SendStaleFirst = false;
                        await _socket.SendAsync(Datagram([0x30, (byte)(invoke - 1), service, 0x0C, 0, 0, 0, 0, 0x1E, 0x29, 0x55, 0x4E, 0x44, 0, 0, 0, 0, 0x4F, 0x1F]), request.RemoteEndPoint, ct);
                    }
                    await _socket.SendAsync(Datagram(apdu), request.RemoteEndPoint, ct);
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException) { }
        }

        private static (int, int) Split(uint id) => ((int)(id >> 22), (int)(id & 0x3FFFFF));

        private static byte[] BigEndian(uint v) => [(byte)(v >> 24), (byte)(v >> 16), (byte)(v >> 8), (byte)v];

        private static byte[] Datagram(byte[] apdu)
        {
            var length = 6 + apdu.Length;
            return [0x81, 0x0A, (byte)(length >> 8), (byte)length, 0x01, 0x00, .. apdu];
        }

        public void Dispose() => _socket.Dispose();
    }
}
