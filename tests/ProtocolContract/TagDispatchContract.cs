using Prognode.Contracts.Devices;
using Prognode.Contracts.Licensing;
using Prognode.Contracts.Tags;
using Prognode.Core.Devices;
using Prognode.Core.Protocols;
using Prognode.Core.Tags;
using Prognode.Protocols.Modbus;
using Prognode.Protocols.Mqtt;
using Prognode.Protocols.OpcUa;
using Prognode.Protocols.S7;

internal static class TagDispatchContract
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static async Task RunAsync()
    {
        var protocols = new ProtocolCatalog().GetAll();
        Check(protocols.Count == 4 && protocols.All(x => x.Enabled) &&
              protocols.Select(x => x.Id).ToHashSet().SetEquals(
                  ["modbus-tcp", "siemens-s7-tcp", "mqtt", "opc-ua"]),
            "Customer catalog must contain exactly the four active protocols.");

        var now = DateTimeOffset.UtcNow;
        var devices = new MemoryDevices();
        foreach (var protocol in new[] { "Modbus TCP", "Siemens S7 TCP", "MQTT", "OPC UA" })
            await devices.AddAsync(new DeviceDefinition(Guid.NewGuid(), protocol, protocol,
                "Configured", "127.0.0.1", 502, 1, 1000, now, now));

        var tags = new MemoryTags();
        var service = new TagService(tags, devices, new CurrentTagValueStore(),
            [new ModbusTagDefinitionValidator(), new S7TagValidator(),
                new MqttTagValidator(), new OpcUaTagValidator()], new UnlimitedTags());
        var byProtocol = (await devices.GetAllAsync()).ToDictionary(d => d.Protocol);
        var modbus = await Create("Modbus TCP", "40001", TagDataType.Word);
        var s7 = await Create("Siemens S7 TCP", "DB1.DBW2", TagDataType.Word);
        var mqtt = await Create("MQTT", "plant/temperature", TagDataType.Float32);
        var opc = await Create("OPC UA", "ns=2;s=Temperature", TagDataType.Float32);
        Check((await service.GetAllAsync()).Count == 4, "Four protocol-specific Tags were not created.");
        await Update(s7, "DB1.DBW4");
        await Update(mqtt, "plant/temperature2");
        await Update(opc, "ns=2;s=Temperature2");
        Check((await tags.GetByIdAsync(modbus.Id))?.Address == "40001" &&
              (await tags.GetByIdAsync(s7.Id))?.Address == "DB1.DBW4" &&
              (await tags.GetByIdAsync(mqtt.Id))?.Address == "plant/temperature2" &&
              (await tags.GetByIdAsync(opc.Id))?.Address == "ns=2;s=Temperature2",
            "Protocol-specific Tag update or persistence failed.");

        async Task<TagDefinition> Create(string protocol, string address, TagDataType type) =>
            await service.CreateAsync(byProtocol[protocol].Id, $"{protocol} Tag", address, type,
                null, ModbusByteOrder.ABCD, null, 0, 0);

        async Task Update(TagDefinition tag, string address) =>
            await service.UpdateAsync(tag.Id, tag.DeviceId, tag.Name, address, tag.DataType,
                null, tag.ByteOrder, tag.Unit, tag.Offset, tag.DecimalPlaces);
    }

    private sealed class UnlimitedTags : ITagCapacityPolicy
    {
        public CapacityLimit UniqueTagLimit => CapacityLimit.Unlimited;
        public bool AllowsUniqueTagCount(int resultingCount) => true;
        public bool IsOverCapacity(int currentCount) => false;
    }

    private sealed class MemoryDevices : IDeviceRepository
    {
        private readonly Dictionary<Guid, DeviceDefinition> _items = new();
        public Task<IReadOnlyList<DeviceDefinition>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<DeviceDefinition>>(_items.Values.ToArray());
        public Task<DeviceDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_items.GetValueOrDefault(id));
        public Task AddAsync(DeviceDefinition device, CancellationToken ct = default)
        { _items.Add(device.Id, device); return Task.CompletedTask; }
        public Task UpdateAsync(DeviceDefinition device, CancellationToken ct = default)
        { _items[device.Id] = device; return Task.CompletedTask; }
        public Task DeleteAsync(Guid id, CancellationToken ct = default)
        { _items.Remove(id); return Task.CompletedTask; }
        public Task<int> CountAsync(CancellationToken ct = default) =>
            Task.FromResult(_items.Count);
    }

    private sealed class MemoryTags : ITagRepository
    {
        private readonly Dictionary<Guid, TagDefinition> _items = new();
        public Task<IReadOnlyList<TagDefinition>> GetAllAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TagDefinition>>(_items.Values.ToArray());
        public Task<IReadOnlyList<TagDefinition>> GetByDeviceIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<TagDefinition>>(_items.Values.Where(t => t.DeviceId == id).ToArray());
        public Task<TagDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
            Task.FromResult(_items.GetValueOrDefault(id));
        public Task AddAsync(TagDefinition tag, CancellationToken ct = default)
        { _items.Add(tag.Id, tag); return Task.CompletedTask; }
        public Task UpdateAsync(TagDefinition tag, CancellationToken ct = default)
        { _items[tag.Id] = tag; return Task.CompletedTask; }
        public Task DeleteAsync(Guid id, CancellationToken ct = default)
        { _items.Remove(id); return Task.CompletedTask; }
        public Task<int> CountAsync(CancellationToken ct = default) =>
            Task.FromResult(_items.Count);
    }
}
