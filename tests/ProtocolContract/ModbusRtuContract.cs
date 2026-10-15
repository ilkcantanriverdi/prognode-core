using System.Buffers.Binary;
using System.IO.Ports;
using Prognode.Contracts.Devices;
using Prognode.Contracts.Tags;
using Prognode.Protocols.Modbus;

/// <summary>
/// Modbus RTU over an in-memory RS-485 line: CRC, two devices sharing one port, exception answers,
/// a silent device, a corrupted answer, and the same Tag decoding as Modbus TCP.
/// </summary>
internal static class ModbusRtuContract
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static async Task RunAsync()
    {
        CheckSettingsAndCrc();
        await CheckReadsAsync();
        Console.WriteLine("PASS: Modbus RTU serial settings, CRC, shared line, exceptions, timeouts and Tag decoding.");
    }

    private static void CheckSettingsAndCrc()
    {
        var settings = ModbusSerialSettings.Parse("com3;9600;8e1");
        Check(settings is { PortName: "COM3", BaudRate: 9600, DataBits: 8, Parity: Parity.Even, StopBits: StopBits.One } &&
              settings.ToString() == "COM3;9600;8E1", "Serial settings not parsed or not canonical.");
        Check(ModbusSerialSettings.Parse("COM12;19200;8N2").ToString() == "COM12;19200;8N2", "8N2 not kept.");
        Check(Math.Abs(settings.FrameGap.TotalMilliseconds - 3.5 * 11 * 1000 / 9600) < 0.01 &&
              ModbusSerialSettings.Parse("COM3;115200;8N1").FrameGap.TotalMilliseconds == 1.75, "RTU frame gap wrong.");
        foreach (var bad in new[] { "COM3;9601;8E1", "COM3;9600;9E1", "COM3;9600;8X1", "192.168.0.1;502;8E1", "COM3" })
        {
            try { ModbusSerialSettings.Parse(bad); throw new Exception($"Serial settings {bad} accepted."); }
            catch (ArgumentException) { }
        }
        // Reference frame from the Modbus spec examples: 01 03 00 00 00 01 -> CRC 84 0A.
        var crc = ModbusRtuRegisterClient.Crc([0x01, 0x03, 0x00, 0x00, 0x00, 0x01]);
        Check((byte)crc == 0x84 && (byte)(crc >> 8) == 0x0A, $"CRC is {crc:X4}, expected 0A84.");
    }

    private static async Task CheckReadsAsync()
    {
        var line = new FakeLine();
        line.Units[1] = new Slave();
        line.Units[2] = new Slave();
        // Unit 1: Float32 at 40001, Float64 at holding register 19000 (419000), coil 1 on, input register 30001 = 27.
        Put(line.Units[1].Holding, 0, BitConverter.GetBytes(12.5f).Reverse().ToArray());
        Put(line.Units[1].Holding, 18999, BitConverter.GetBytes(230.75d).Reverse().ToArray());
        line.Units[1].Coils[0] = true;
        line.Units[1].Input[0] = 27;
        // Unit 2 on the same line: a different value at the same address.
        Put(line.Units[2].Holding, 0, BitConverter.GetBytes(-3.5f).Reverse().ToArray());

        var opened = 0;
        var client = new ModbusRtuRegisterClient(_ => { Interlocked.Increment(ref opened); return line; });
        var reader = new ModbusTagReader(client, ModbusTagReader.RtuProtocol);
        var now = DateTimeOffset.UtcNow;
        DeviceDefinition Device(int unit) => new(Guid.NewGuid(), $"Meter {unit}", "Modbus RTU", "Configured", "COM9;9600;8E1", null, unit, 1000, now, now);
        TagDefinition Tag(DeviceDefinition device, string name, string address, TagDataType type) =>
            new(Guid.NewGuid(), device.Id, name, address, type, null, ModbusByteOrder.ABCD, "", 1, 0, 0, true, now, now);

        Check(reader.CanHandle(Device(1)) && !reader.CanHandle(Device(1) with { Protocol = "Modbus TCP" }), "RTU reader protocol routing wrong.");
        var meter1 = Device(1);
        var meter2 = Device(2);
        var tags1 = new[]
        {
            Tag(meter1, "Power", "40001", TagDataType.Float32),
            Tag(meter1, "Energy", "419000", TagDataType.Float64),
            Tag(meter1, "Breaker", "00001", TagDataType.Bool),
            Tag(meter1, "Count", "30001", TagDataType.Word),
            Tag(meter1, "Undefined", "40150", TagDataType.Word),
        };
        var tags2 = new[] { Tag(meter2, "Power", "40001", TagDataType.Float32) };

        // Both meters polled at the same time: one port, serialized requests.
        var results = await Task.WhenAll(reader.ReadAsync(meter1, tags1, CancellationToken.None), reader.ReadAsync(meter2, tags2, CancellationToken.None));
        var byName = results[0].ToDictionary(v => tags1.Single(t => t.Id == v.TagId).Name);
        Check(byName["Power"] is { Quality: TagQuality.Good, Value: 12.5 } && byName["Energy"].Value == 230.75 &&
              byName["Breaker"].Value == 1 && byName["Count"].Value == 27,
            "RTU values wrong: " + string.Join(", ", byName.Select(x => $"{x.Key}={x.Value.Value} {x.Value.Quality} {x.Value.Error}")));
        Check(byName["Undefined"].Quality == TagQuality.Bad && byName["Undefined"].Error!.Contains("0x02"),
            $"An illegal address must make only its own Tag Bad: {byName["Undefined"].Quality} {byName["Undefined"].Error}");
        Check(results[1].Single() is { Quality: TagQuality.Good, Value: -3.5 }, "Second unit on the shared line read wrong.");
        Check(opened == 1, $"Serial port opened {opened} times; one line must be shared.");
        Check(!line.Overlapped, "Two requests were on the line at the same time.");
        Check(client.HasRecentSuccess("COM9;9600;8E1", 1, TimeSpan.FromSeconds(30)) &&
              !client.HasRecentSuccess("COM9;9600;8E1", 5, TimeSpan.FromSeconds(30)), "Per-unit health tracking wrong.");

        var silent = Device(3);
        try { await reader.ReadAsync(silent, [Tag(silent, "X", "40001", TagDataType.Word)], CancellationToken.None); throw new Exception("Silent unit read succeeded."); }
        catch (IOException ex) { Check(ex.Message.Contains("did not answer"), $"Timeout message unclear: {ex.Message}"); }

        line.CorruptNext = true;
        try { await reader.ReadAsync(meter2, tags2, CancellationToken.None); throw new Exception("Corrupted answer accepted."); }
        catch (IOException ex) { Check(ex.Message.Contains("CRC"), $"CRC failure message unclear: {ex.Message}"); }
        Check((await reader.ReadAsync(meter2, tags2, CancellationToken.None)).Single().Quality == TagQuality.Good && opened == 1,
            "The line must keep working (without reopening) after a timeout or a corrupted answer.");

        line.Fail = true;
        try { await reader.ReadAsync(meter2, tags2, CancellationToken.None); throw new Exception("Unplugged adapter not reported."); }
        catch (IOException) { }
        line.Fail = false;
        Check((await reader.ReadAsync(meter2, tags2, CancellationToken.None)).Single().Quality == TagQuality.Good && opened == 2,
            "After the adapter failed the port must be reopened once.");
    }

    private static void Put(ushort[] registers, int offset, byte[] bigEndian)
    {
        for (var i = 0; i < bigEndian.Length / 2; i++)
            registers[offset + i] = (ushort)((bigEndian[i * 2] << 8) | bigEndian[i * 2 + 1]);
    }

    private sealed class Slave
    {
        public ushort[] Holding { get; } = new ushort[65536];
        public ushort[] Input { get; } = new ushort[65536];
        public bool[] Coils { get; } = new bool[65536];
    }

    /// <summary>An RS-485 line with Modbus slaves behind it. Registers above 99 except 18999+ are undefined.</summary>
    private sealed class FakeLine : IModbusSerialLine
    {
        private readonly Queue<byte> _pending = new();
        private int _busy;
        public Dictionary<int, Slave> Units { get; } = [];
        public bool CorruptNext { get; set; }
        public bool Fail { get; set; }
        public bool Overlapped { get; private set; }

        public void Write(byte[] frame)
        {
            if (Fail) throw new IOException("The device does not recognize the command.");
            if (Interlocked.Increment(ref _busy) > 1) Overlapped = true;
            try
            {
                var crc = ModbusRtuRegisterClient.Crc(frame.AsSpan(0, 6));
                Check(frame.Length == 8 && frame[6] == (byte)crc && frame[7] == (byte)(crc >> 8), "Request CRC wrong.");
                Thread.Sleep(2);
                if (!Units.TryGetValue(frame[0], out var slave)) return; // nobody answers
                var function = frame[1];
                var start = (frame[2] << 8) | frame[3];
                var quantity = (frame[4] << 8) | frame[5];
                byte[] pdu;
                if (function is 0x03 or 0x04 && start + quantity > 100 && start < 18999)
                    pdu = [(byte)(function | 0x80), 0x02];
                else if (function is 0x01 or 0x02)
                {
                    var bytes = new byte[(quantity + 7) / 8];
                    for (var i = 0; i < quantity; i++) if (slave.Coils[start + i]) bytes[i / 8] |= (byte)(1 << (i % 8));
                    pdu = [function, (byte)bytes.Length, .. bytes];
                }
                else
                {
                    var map = function == 0x03 ? slave.Holding : slave.Input;
                    var data = new byte[quantity * 2];
                    for (var i = 0; i < quantity; i++) BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(i * 2), map[start + i]);
                    pdu = [function, (byte)data.Length, .. data];
                }
                byte[] answer = [frame[0], .. pdu, 0, 0];
                var answerCrc = ModbusRtuRegisterClient.Crc(answer.AsSpan(0, answer.Length - 2));
                answer[^2] = (byte)answerCrc;
                answer[^1] = (byte)(answerCrc >> 8);
                if (CorruptNext) { answer[^1] ^= 0xFF; CorruptNext = false; }
                lock (_pending) foreach (var b in answer) _pending.Enqueue(b);
            }
            finally { Interlocked.Decrement(ref _busy); }
        }

        public byte[] Read(int count, int timeoutMs)
        {
            if (Fail) throw new IOException("The port is closed.");
            lock (_pending)
            {
                if (_pending.Count < count) throw new TimeoutException();
                return Enumerable.Range(0, count).Select(_ => _pending.Dequeue()).ToArray();
            }
        }

        public void DiscardInput() { lock (_pending) _pending.Clear(); }
        public void Dispose() { }
    }
}
