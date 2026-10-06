using Microsoft.Data.Sqlite;
using Prognode.Contracts.Devices;
using Prognode.Contracts.Licensing;
using Prognode.Contracts.Tags;
using Prognode.Core.Devices;
using Prognode.Core.Tags;
using Prognode.Data.Sqlite;
using Prognode.Protocols.Abstractions;

var root = Path.Combine(Path.GetTempPath(), "pgn-capacity-contract-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var options = new SqliteDatabaseOptions(Path.Combine(root, "prognode.db"));
    await new DatabaseInitializer(options).InitializeAsync();
    var repository = new SqliteDeviceRepository(options);
    var policy = new TestCapacityPolicy { DeviceLimit = CapacityLimit.Limited(1) };
    var devices = new DeviceService(new DelayedCountDeviceRepository(repository), policy,
        new SqliteTagRepository(options));

    var createRequests = Enumerable.Range(0, 15).Select(index => (index % 5) switch
    {
        0 => (Func<Task<DeviceDefinition>>)(() => devices.CreateMockAsync($"Mock {index}")),
        1 => () => devices.CreateModbusTcpAsync($"Modbus {index}", "127.0.0.1", 502, 1, 1000),
        2 => () => devices.CreateS7TcpAsync($"S7 {index}", "127.0.0.1", 102, 0, 1, 1000),
        3 => () => devices.CreateMqttAsync($"MQTT {index}", "mqtt://127.0.0.1", 1883, 1000),
        _ => () => devices.CreateOpcUaAsync($"OPC UA {index}", "opc.tcp://127.0.0.1:4840/", 1000)
    }).ToArray();
    var outcomes = await Task.WhenAll(createRequests.Select(request => Task.Run(() => TryCreateAsync(request))));
    Check(outcomes.Count(x => x) == 1 && await repository.CountAsync() == 1,
        "Concurrent device creation exceeded the limit or rejected every request.");
    Console.WriteLine("PASS concurrent Mock/Modbus/S7/MQTT/OPC UA creation admits exactly one device");

    var first = (await repository.GetAllAsync()).Single();
    await devices.DeleteAsync(first.Id);
    var modbus = await devices.CreateModbusTcpAsync("Modbus", "127.0.0.1", 502, 1, 1000);
    Check(!await TryCreateAsync(() => devices.CreateS7TcpAsync("Blocked S7", "127.0.0.1", 102, 0, 1, 1000)),
        "S7 bypassed device capacity.");
    await devices.DeleteAsync(modbus.Id);
    var s7 = await devices.CreateS7TcpAsync("S7", "127.0.0.1", 102, 0, 1, 1000);
    Check(!await TryCreateAsync(() => devices.CreateMockAsync("Blocked Mock")),
        "Mock bypassed device capacity.");

    policy.DeviceLimit = CapacityLimit.Unlimited;
    var second = await devices.CreateMockAsync("Existing second device");
    Check(await repository.CountAsync() == 2, "Unlimited commercial device capacity was restricted.");
    policy.DeviceLimit = CapacityLimit.Limited(1);
    Check(!await TryCreateAsync(() => devices.CreateMockAsync("Blocked third device")) &&
          await repository.CountAsync() == 2,
        "Over-capacity existing devices were changed or a new device was accepted.");
    await devices.DeleteAsync(second.Id);
    Check(!await TryCreateAsync(() => devices.CreateMockAsync("Still blocked")),
        "A new device was accepted while the configured count already met the limit.");
    Console.WriteLine("PASS unlimited license and over-capacity data preservation");

    var tagRepository = new SqliteTagRepository(options);
    policy.UniqueTagLimit = CapacityLimit.Limited(10);
    var tags = new TagService(tagRepository, repository, new CurrentTagValueStore(),
        [new AcceptTestTags()], policy);
    var tagOutcomes = await Task.WhenAll(Enumerable.Range(0, 11).Select(index => Task.Run(async () =>
    {
        try
        {
            await tags.CreateAsync(s7.Id, $"Tag {index}", $"4000{index + 1}", TagDataType.UInt16,
                null, ModbusByteOrder.ABCD, null, 0, 0);
            return true;
        }
        catch (TagCapacityExceededException) { return false; }
    })));
    Check(tagOutcomes.Count(x => x) == 10 && await tagRepository.CountAsync() == 10,
        "Concurrent tag creation exceeded the configured limit.");
    Console.WriteLine("PASS concurrent tag creation stays within the configured limit");

    policy.DeviceLimit = CapacityLimit.Unlimited;
    policy.UniqueTagLimit = CapacityLimit.Unlimited;
    var firstTag = (await tagRepository.GetAllAsync()).First();
    Check(await RejectsDuplicateName(() => devices.CreateMqttAsync("s7", "mqtt://127.0.0.1", 1883, 1000)),
        "Case-insensitive Device name collision was accepted.");
    Check(await RejectsDuplicateName(() => devices.CreateOpcUaAsync(firstTag.Name,
        "opc.tcp://127.0.0.1:4840/", 1000)), "Device used an existing Tag name.");
    Check(await RejectsDuplicateName(() => tags.CreateAsync(s7.Id, "S7", "40050",
        TagDataType.UInt16, null, ModbusByteOrder.ABCD, null, 0, 0)),
        "Tag used an existing Device name.");
    Check(await RejectsDuplicateName(() => tags.CreateAsync(s7.Id, firstTag.Name.ToUpperInvariant(),
        "40051", TagDataType.UInt16, null, ModbusByteOrder.ABCD, null, 0, 0)),
        "Case-insensitive Tag name collision was accepted.");
    Check(await RejectsDuplicateName(() => devices.UpdateAsync(s7.Id, firstTag.Name,
        s7.Host, s7.Port, s7.UnitId, s7.PollIntervalMs)),
        "Device rename to a Tag name was accepted.");
    Check(await RejectsDuplicateName(() => tags.UpdateAsync(firstTag.Id, s7.Id, "S7",
        firstTag.Address, firstTag.DataType, firstTag.BitIndex, firstTag.ByteOrder,
        firstTag.Unit, firstTag.Offset, firstTag.DecimalPlaces)),
        "Tag rename to a Device name was accepted.");
    Console.WriteLine("PASS Device/Tag names are globally unique on create and update");
    await ProcessDataContract.RunAsync(root);
}
finally
{
    SqliteConnection.ClearAllPools();
    try { Directory.Delete(root, recursive: true); } catch { }
}

static async Task<bool> TryCreateAsync(Func<Task<DeviceDefinition>> create)
{
    try { await create(); return true; }
    catch (DeviceCapacityExceededException) { return false; }
}

static async Task<bool> RejectsDuplicateName(Func<Task> action)
{
    try { await action(); return false; }
    catch (ArgumentException ex) when (ex.Message.Contains("already used", StringComparison.OrdinalIgnoreCase))
    { return true; }
}

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

sealed class TestCapacityPolicy : IDeviceCapacityPolicy, ITagCapacityPolicy
{
    public CapacityLimit DeviceLimit { get; set; } = CapacityLimit.Unlimited;
    public CapacityLimit UniqueTagLimit { get; set; } = CapacityLimit.Unlimited;
    public bool AllowsDeviceCount(int resultingCount) => Allows(DeviceLimit, resultingCount);
    public bool AllowsUniqueTagCount(int resultingCount) => Allows(UniqueTagLimit, resultingCount);
    public bool IsOverCapacity(int currentCount) =>
        !UniqueTagLimit.IsUnlimited && currentCount > UniqueTagLimit.Value!.Value;
    private static bool Allows(CapacityLimit limit, int count) =>
        count >= 0 && (limit.IsUnlimited || count <= limit.Value!.Value);
}

sealed class AcceptTestTags : ITagDefinitionValidator
{
    public bool CanHandle(DeviceDefinition device) => true;
    public void Validate(TagDefinition candidate, IReadOnlyList<TagDefinition> existingTags) { }
}

sealed class DelayedCountDeviceRepository(IDeviceRepository inner) : IDeviceRepository
{
    public Task<IReadOnlyList<DeviceDefinition>> GetAllAsync(CancellationToken ct = default) =>
        inner.GetAllAsync(ct);
    public Task<DeviceDefinition?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
        inner.GetByIdAsync(id, ct);
    public Task AddAsync(DeviceDefinition device, CancellationToken ct = default) =>
        inner.AddAsync(device, ct);
    public Task UpdateAsync(DeviceDefinition device, CancellationToken ct = default) =>
        inner.UpdateAsync(device, ct);
    public Task DeleteAsync(Guid id, CancellationToken ct = default) =>
        inner.DeleteAsync(id, ct);
    public async Task<int> CountAsync(CancellationToken ct = default)
    {
        var count = await inner.CountAsync(ct);
        await Task.Delay(50, ct);
        return count;
    }
}
