using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;
using MQTTnet;
using MQTTnet.Server;
using Prognode.Contracts.Devices;
using Prognode.Contracts.Tags;
using Prognode.Protocols.Modbus;
using Prognode.Protocols.Mqtt;

/// <summary>
/// Modbus registers above 9999 (six-digit addresses) and 64-bit values; MQTT broker credentials and
/// JSON messages read field by field.
/// </summary>
internal static class ProtocolGapsContract
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static async Task RunAsync()
    {
        await CheckModbusWideAsync();
        CheckMqttJsonMapping();
        await CheckMqttCredentialsAsync();
        Console.WriteLine("PASS: Modbus six-digit / 64-bit, MQTT JSON fields and broker credentials.");
    }

    private static async Task CheckModbusWideAsync()
    {
        var now = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid();
        var tags = new[]
        {
            Tag(id, "Energy", "419000", TagDataType.Float64, ModbusByteOrder.ABCD, now),
            Tag(id, "EnergyLowWordFirst", "419004", TagDataType.Float64, ModbusByteOrder.CDAB, now),
            Tag(id, "Counter", "419008", TagDataType.UInt64, ModbusByteOrder.ABCD, now),
            Tag(id, "Signed", "319012", TagDataType.Int64, ModbusByteOrder.DCBA, now),
            Tag(id, "Classic", "40010", TagDataType.Float32, ModbusByteOrder.ABCD, now),
        };
        var validator = new ModbusTagDefinitionValidator();
        foreach (var tag in tags) validator.Validate(tag, tags.Where(x => x.Id != tag.Id).ToArray());
        Check(ModbusTagAddressParser.Parse("419000", TagDataType.Float64) is { Area: ModbusArea.HoldingRegister, ZeroBasedOffset: 18999, Width: 4 },
            "Six-digit holding register 419000 must be register 19000 (offset 18999), four registers wide.");
        Check(ModbusTagAddressParser.Parse("000017", TagDataType.Bool).Area == ModbusArea.Coil &&
              ModbusTagAddressParser.Parse("465533", TagDataType.Float64).ZeroBasedOffset == 65532,
            "Six-digit coil or last holding registers rejected.");
        foreach (var (address, type) in new[] { ("465534", TagDataType.Float64), ("465537", TagDataType.Word),
                     ("49999", TagDataType.Int64), ("200001", TagDataType.Word), ("-40001", TagDataType.Word) })
        {
            try { ModbusTagAddressParser.Parse(address, type); throw new Exception($"Modbus address {address} accepted for {type}."); }
            catch (ArgumentException) { }
        }
        try
        {
            validator.Validate(Tag(id, "Overlap", "419002", TagDataType.Word, ModbusByteOrder.ABCD, now), tags);
            throw new Exception("A register inside a 64-bit value was accepted as a separate Tag.");
        }
        catch (ArgumentException) { }

        var holding = new Dictionary<int, ushort>();
        var input = new Dictionary<int, ushort>();
        Put(holding, 18999, BitConverter.GetBytes(123456.789d), wordSwap: false, byteSwap: false);
        Put(holding, 19003, BitConverter.GetBytes(-0.5d), wordSwap: true, byteSwap: false);
        Put(holding, 19007, BitConverter.GetBytes(5_000_000_000UL), wordSwap: false, byteSwap: false);
        Put(input, 19011, BitConverter.GetBytes(-7L), wordSwap: true, byteSwap: true);
        Put(holding, 9, BitConverter.GetBytes(12.5f), wordSwap: false, byteSwap: false);

        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(8));
        var server = ServeRegistersAsync(listener, holding, input, timeout.Token);
        var device = new DeviceDefinition(id, "Meter", "Modbus TCP", "Configured", "127.0.0.1", port, 1, 250, now, now);
        IReadOnlyList<TagValueSnapshot> values;
        try { values = await new ModbusTagReader(new ModbusTcpRegisterClient()).ReadAsync(device, tags, timeout.Token); }
        finally { timeout.Cancel(); await server; }
        var byName = values.ToDictionary(v => tags.Single(t => t.Id == v.TagId).Name);
        Check(byName.Values.All(v => v.Quality == TagQuality.Good), "64-bit Modbus Tags did not read Good: " +
            string.Join(", ", byName.Where(x => x.Value.Quality != TagQuality.Good).Select(x => $"{x.Key} {x.Value.Error}")));
        Check(byName["Energy"].Value == 123456.789 && byName["EnergyLowWordFirst"].Value == -0.5 &&
              byName["Counter"].Value == 5_000_000_000d && byName["Signed"].Value == -7 && byName["Classic"].Value == 12.5,
            "64-bit Modbus values or byte orders decoded wrong: " +
            string.Join(", ", byName.Select(x => $"{x.Key}={x.Value.Value}")));
    }

    /// <summary>Stores a little-endian value as Modbus registers in the given byte order.</summary>
    private static void Put(Dictionary<int, ushort> registers, int offset, byte[] littleEndian, bool wordSwap, bool byteSwap)
    {
        var bigEndian = littleEndian.Reverse().ToArray();
        var words = Enumerable.Range(0, bigEndian.Length / 2)
            .Select(i => (ushort)((bigEndian[i * 2] << 8) | bigEndian[i * 2 + 1])).ToArray();
        if (wordSwap) Array.Reverse(words);
        for (var i = 0; i < words.Length; i++)
            registers[offset + i] = byteSwap ? BinaryPrimitives.ReverseEndianness(words[i]) : words[i];
    }

    private static async Task ServeRegistersAsync(TcpListener listener, Dictionary<int, ushort> holding,
        Dictionary<int, ushort> input, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                using var client = await listener.AcceptTcpClientAsync(ct);
                var stream = client.GetStream();
                try
                {
                    while (!ct.IsCancellationRequested)
                    {
                        var request = new byte[12];
                        await stream.ReadExactlyAsync(request, ct);
                        var function = request[7];
                        Check(function is 0x03 or 0x04, $"Unexpected Modbus FC{function:X2}.");
                        var start = (request[8] << 8) | request[9];
                        var quantity = (request[10] << 8) | request[11];
                        var map = function == 0x03 ? holding : input;
                        var data = new byte[quantity * 2];
                        for (var i = 0; i < quantity; i++)
                            BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(i * 2), map.GetValueOrDefault(start + i));
                        var length = 3 + data.Length;
                        byte[] response = [request[0], request[1], 0, 0, (byte)(length >> 8), (byte)length,
                            request[6], function, (byte)data.Length, .. data];
                        await stream.WriteAsync(response, ct);
                    }
                }
                catch (EndOfStreamException) { }
                catch (IOException) { }
            }
        }
        catch (OperationCanceledException) { }
    }

    private static void CheckMqttJsonMapping()
    {
        var now = DateTimeOffset.UtcNow;
        var deviceId = Guid.NewGuid();
        var payload = Encoding.UTF8.GetBytes("""{"temp":21.5,"ok":true,"name":"line 1","sensors":[{"value":"7.25"},{"value":null}],"big":123456789012}""");
        TagValueSnapshot Read(string address, TagDataType type) =>
            MqttPayloadMapper.Map(deviceId, Tag(deviceId, address, address, type, ModbusByteOrder.ABCD, now), payload, false, now, now, 1000);
        Check(Read("env#temp", TagDataType.Float32) is { Quality: TagQuality.Good, Value: 21.5 }, "JSON number field not read.");
        Check(Read("env#$.temp", TagDataType.Float32).Value == 21.5, "JSON $. prefix not accepted.");
        Check(Read("env#ok", TagDataType.Bool) is { Quality: TagQuality.Good, Value: 1 }, "JSON boolean field not read.");
        Check(Read("env#sensors[0].value", TagDataType.Float32).Value == 7.25, "JSON array item / numeric string not read.");
        Check(Read("env#big", TagDataType.Int64).Value == 123456789012d, "JSON 64-bit integer not read.");
        Check(Read("env#missing", TagDataType.Float32) is { Quality: TagQuality.Bad, Error: { } missing } && missing.Contains("missing"),
            "Missing JSON field must be Bad with its name.");
        Check(Read("env#sensors[1].value", TagDataType.Float32).Quality == TagQuality.Bad, "JSON null must be Bad.");
        Check(Read("env#name", TagDataType.Float32).Quality == TagQuality.Bad, "Non-numeric JSON string must be Bad.");
        Check(Read("env", TagDataType.Float32).Quality == TagQuality.Bad, "A JSON message without #field must not be read as a number.");
        Check(MqttPayloadMapper.Map(deviceId, Tag(deviceId, "t", "env#temp", TagDataType.Float32, ModbusByteOrder.ABCD, now),
            "not json"u8.ToArray(), false, now, now, 1000).Error == "MQTT message is not valid JSON.", "Invalid JSON not reported.");
        var validator = new MqttTagValidator();
        validator.Validate(Tag(deviceId, "ok", "plant/env#sensors[0].value", TagDataType.Float32, ModbusByteOrder.ABCD, now), []);
        foreach (var address in new[] { "plant/+/env", "plant/env#", "#temp", "plant/env#a..b" })
        {
            try { validator.Validate(Tag(deviceId, "bad", address, TagDataType.Float32, ModbusByteOrder.ABCD, now), []); throw new Exception($"MQTT address {address} accepted."); }
            catch (ArgumentException) { }
        }
    }

    private static async Task CheckMqttCredentialsAsync()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var factory = new MqttServerFactory();
        using var server = factory.CreateMqttServer(factory.CreateServerOptionsBuilder()
            .WithDefaultEndpoint().WithDefaultEndpointPort(port).Build());
        server.ValidatingConnectionAsync += args =>
        {
            args.ReasonCode = args.UserName == "plant" && args.Password == "s3cret!"
                ? MQTTnet.Protocol.MqttConnectReasonCode.Success
                : MQTTnet.Protocol.MqttConnectReasonCode.BadUserNameOrPassword;
            return Task.CompletedTask;
        };
        await server.StartAsync();
        try
        {
            var now = DateTimeOffset.UtcNow;
            var device = new DeviceDefinition(Guid.NewGuid(), "Broker", "MQTT", "Configured", "mqtt://127.0.0.1", port, null, 250, now, now);
            var tag = Tag(device.Id, "Temp", "plant/env#temp", TagDataType.Float32, ModbusByteOrder.ABCD, now);
            var store = new MemoryCredentialStore();

            using (var anonymous = new MqttTagReader(store))
            {
                try { await anonymous.ReadAsync(device, [tag], CancellationToken.None); throw new Exception("Broker accepted a reader without credentials."); }
                catch (IOException ex) { Check(ex.Message.Contains("user name or password"), $"Unexpected rejection message: {ex.Message}"); }
            }

            store.Save(device.Id, new DeviceCredential("plant", "s3cret!"));
            using var reader = new MqttTagReader(store);
            await reader.ReadAsync(device, [tag], CancellationToken.None);
            using var publisher = new MqttClientFactory().CreateMqttClient();
            await publisher.ConnectAsync(new MqttClientOptionsBuilder().WithTcpServer("127.0.0.1", port)
                .WithCredentials("plant", "s3cret!").Build());
            await publisher.PublishAsync(new MqttApplicationMessageBuilder().WithTopic("plant/env")
                .WithPayload("""{"temp":19.75,"hum":40}""").Build());
            TagValueSnapshot latest;
            var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
            do
            {
                latest = (await reader.ReadAsync(device, [tag], CancellationToken.None))[0];
                if (latest.Quality == TagQuality.Good) break;
                await Task.Delay(50);
            } while (DateTimeOffset.UtcNow < deadline);
            Check(latest is { Quality: TagQuality.Good, Value: 19.75 }, "Authenticated JSON subscription did not read the field.");

            store.Save(device.Id, new DeviceCredential("plant", "wrong"));
            try { await reader.ReadAsync(device, [tag], CancellationToken.None); throw new Exception("Changed (wrong) password did not reconnect."); }
            catch (IOException) { }
            await publisher.DisconnectAsync();
        }
        finally { await server.StopAsync(); }
    }

    private sealed class MemoryCredentialStore : IDeviceCredentialStore
    {
        private readonly Dictionary<Guid, DeviceCredential> _items = new();
        public DeviceCredential? Get(Guid deviceId) => _items.GetValueOrDefault(deviceId);
        public string? GetUsername(Guid deviceId) => Get(deviceId)?.Username;
        public void Save(Guid deviceId, DeviceCredential? credential)
        {
            if (credential is null) _items.Remove(deviceId); else _items[deviceId] = credential;
        }
    }

    private static TagDefinition Tag(Guid deviceId, string name, string address, TagDataType type,
        ModbusByteOrder order, DateTimeOffset now) =>
        new(Guid.NewGuid(), deviceId, name, address, type, null, order, string.Empty, 1, 0, 0, true, now, now);
}
