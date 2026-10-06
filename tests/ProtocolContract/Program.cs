using System.Net;
using System.Net.Sockets;
using MQTTnet;
using MQTTnet.Server;
using Prognode.Contracts.Devices;
using Prognode.Contracts.Tags;
using Prognode.Protocols.Modbus;
using Prognode.Protocols.Mqtt;
using Prognode.Protocols.OpcUa;
using Prognode.Protocols.S7;
using Opc.Ua;

if (args is ["--opc-probe", var probeUrl, var probeDataRoot, var probeNode])
{
    using var probeTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
    var probe = new OpcUaConnectionFactory(probeDataRoot);
    var certificate = await probe.InspectAsync(probeUrl, probeTimeout.Token);
    Console.WriteLine($"OPC UA certificate: {certificate.Subject}; SHA-256={certificate.Sha256}; trusted={certificate.Trusted}");
    using var session = await probe.OpenAsync(probeUrl, probeTimeout.Token);
    var node = OpcUaNodeAddress.Resolve(probeNode, session.NamespaceUris);
    var response = await session.ReadAsync(null, 0, TimestampsToReturn.Both,
        new ReadValueIdCollection { new() { NodeId = node, AttributeId = Attributes.Value } },
        probeTimeout.Token);
    Console.WriteLine($"OPC UA read: status={response.Results[0].StatusCode}; type={response.Results[0].Value?.GetType().Name ?? "null"}; node={node}");
    return;
}

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

var now = DateTimeOffset.UtcNow;
await IndustrialProtocolContract.RunAsync();
await TagDispatchContract.RunAsync();
var deviceId = Guid.NewGuid();
var tag = new TagDefinition(Guid.NewGuid(), deviceId, "Temperature", "plant/temp",
    TagDataType.Float32, null, ModbusByteOrder.ABCD, "C", 1, 0, 1,
    true, now, now);
var badTopic = tag with { Address = "plant/+" };
var mqttValidator = new MqttTagValidator();
var mqttDevice = new DeviceDefinition(deviceId, "Broker", "MQTT", "Configured",
    "mqtt://127.0.0.1", 1883, null, 250, now, now);
mqttValidator.Validate(tag, []);
try { mqttValidator.Validate(badTopic, []); throw new Exception("Wildcard topic accepted."); }
catch (ArgumentException) { }
Check(MqttEndpoint.Parse("mqtts://broker.example", 8883).UseTls, "MQTTS flag lost.");
Check(MqttEndpoint.Parse("broker.example", 8883).UseTls, "Bare broker must default to TLS.");
try { MqttEndpoint.Parse("mqtt://user:pass@broker.example", 1883); throw new Exception("URL secret accepted."); }
catch (ArgumentException) { }

var fresh = MqttPayloadMapper.Map(deviceId, tag, "12.3"u8.ToArray(), false,
    now, now, 250);
Check(fresh.Quality == TagQuality.Good && Math.Abs(fresh.Value!.Value - 1.23) < 0.0001,
    "MQTT numeric mapping failed.");
Check(MqttPayloadMapper.Map(deviceId, tag, "12.3"u8.ToArray(), true,
    now, now, 250).Quality == TagQuality.Uncertain, "Retained value freshness not marked uncertain.");
Check(MqttPayloadMapper.Map(deviceId, tag, "12.3"u8.ToArray(), false,
    now.AddMinutes(-1), now, 250).Quality == TagQuality.Stale, "Old value not stale.");
Check(MqttPayloadMapper.Map(deviceId, tag, "bad"u8.ToArray(), false,
    now, now, 250).Quality == TagQuality.Bad, "Malformed payload not rejected.");

Check(OpcUaEndpoint.Parse("opc.tcp://127.0.0.1:4840/PLC").StartsWith("opc.tcp://"),
    "OPC UA URL parsing failed.");
try { OpcUaEndpoint.Parse("http://127.0.0.1:4840"); throw new Exception("HTTP OPC UA URL accepted."); }
catch (ArgumentException) { }
var opcValidator = new OpcUaTagValidator();
opcValidator.Validate(tag with { Address = "ns=2;s=Temperature" }, []);
opcValidator.Validate(tag with { Address = "http://Server interface_1;i=13" }, []);
var namespaces = new Opc.Ua.NamespaceTable();
var interfaceNamespace = namespaces.GetIndexOrAppend("http://Server interface_1");
var resolvedNode = OpcUaNodeAddress.Resolve("http://Server interface_1;i=13", namespaces);
Check(resolvedNode.NamespaceIndex == interfaceNamespace &&
      resolvedNode.Identifier is uint nodeNumber && nodeNumber == 13,
    "Siemens server-interface namespace URI was not resolved to the runtime index.");
try { opcValidator.Validate(tag with { Address = "not-a-node-id" }, []); throw new Exception("Invalid NodeId accepted."); }
catch (ArgumentException) { }

using var listener = new TcpListener(IPAddress.Loopback, 0);
listener.Start();
var port = ((IPEndPoint)listener.LocalEndpoint).Port;
listener.Stop();
var opcDataRoot = Path.Combine(Path.GetTempPath(), "prognode-opc-contract-" + Guid.NewGuid().ToString("N"));
try
{
    var connections = new OpcUaConnectionFactory(opcDataRoot);
    try
    {
        using var session = await connections.OpenAsync($"opc.tcp://127.0.0.1:{port}/", CancellationToken.None);
        throw new Exception("Unexpected OPC UA session on an unopened test port.");
    }
    catch (Exception ex) when (ex is not OperationCanceledException &&
                               ex.Message != "Unexpected OPC UA session on an unopened test port.") { }
    var ownStore = Path.Combine(opcDataRoot, "opc-ua", "pki", "own");
    Check(Directory.Exists(ownStore) &&
          Directory.EnumerateFiles(ownStore, "*", SearchOption.AllDirectories).Any(),
        "OPC UA application certificate was not created in the isolated PKI.");
    var privateFiles = Directory.EnumerateFiles(Path.Combine(ownStore, "private"),
        "*", SearchOption.AllDirectories).ToArray();
    Check(privateFiles.Length > 0 && privateFiles.All(path =>
        new[] { ".pfx", ".pem", ".key" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)),
        "OPC UA private-key files must have an extension excluded from Core backups.");
}
finally
{
    if (Path.GetFileName(opcDataRoot).StartsWith("prognode-opc-contract-", StringComparison.Ordinal) &&
        Directory.Exists(opcDataRoot))
        Directory.Delete(opcDataRoot, recursive: true);
}
var serverFactory = new MqttServerFactory();
var serverOptions = serverFactory.CreateServerOptionsBuilder()
    .WithDefaultEndpoint().WithDefaultEndpointPort(port).Build();
using var server = serverFactory.CreateMqttServer(serverOptions);
await server.StartAsync();
try
{
    var localDevice = mqttDevice with { Port = port };
    using var reader = new MqttTagReader();
    var initial = await reader.ReadAsync(localDevice, [tag], CancellationToken.None);
    Check(initial.Count == 1 && initial[0].Quality == TagQuality.Uncertain,
        "Subscriber must wait for first message.");
    using var publisher = new MqttClientFactory().CreateMqttClient();
    await publisher.ConnectAsync(new MqttClientOptionsBuilder()
        .WithTcpServer("127.0.0.1", port).Build());
    await publisher.PublishAsync(new MqttApplicationMessageBuilder()
        .WithTopic(tag.Address).WithPayload("25.0").Build());
    var deadline = DateTimeOffset.UtcNow.AddSeconds(3);
    TagValueSnapshot latest;
    do
    {
        latest = (await reader.ReadAsync(localDevice, [tag], CancellationToken.None))[0];
        if (latest.Quality == TagQuality.Good) break;
        await Task.Delay(50);
    } while (DateTimeOffset.UtcNow < deadline);
    Check(latest.Quality == TagQuality.Good && Math.Abs(latest.Value!.Value - 2.5) < 0.0001,
        "MQTT broker-to-Tag subscription failed.");
    await publisher.DisconnectAsync();
}
finally { await server.StopAsync(); }

Console.WriteLine("PASS: Modbus TCP, S7 TCP, MQTT and OPC UA protocol contracts.");
