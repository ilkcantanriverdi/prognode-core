using Prognode.Contracts.Devices;
using Prognode.Contracts.Equipment;
using Prognode.Contracts.Tags;
using Prognode.Data.Sqlite;
using Prognode.Trends;

/// <summary>Equipment cards: saved order and views, validation, and what happens when a Tag, device or card is deleted.</summary>
internal static class EquipmentContract
{
    public static async Task RunAsync(string root)
    {
        var options = new SqliteDatabaseOptions(Path.Combine(root, "equipment-contract.db"));
        await new DatabaseInitializer(options).InitializeAsync();
        var now = DateTimeOffset.UtcNow;
        var deviceRepository = new SqliteDeviceRepository(options);
        var device = new DeviceDefinition(Guid.NewGuid(), "S7-1500", "Siemens S7", "Configured", "192.168.0.10", 102, 1, 1000, now, now);
        await deviceRepository.AddAsync(device);
        var tags = new SqliteTagRepository(options);
        TagDefinition Tag(string name) => new(Guid.NewGuid(), device.Id, name, "DB1.DBD0", TagDataType.Float32, null, ModbusByteOrder.ABCD, "bar", 1, 0, 1, true, now, now);
        var pressure = Tag("Hydraulic pressure"); var temperature = Tag("Motor M3 temperature"); var cycle = Tag("Cycle time");
        foreach (var t in new[] { pressure, temperature, cycle }) await tags.AddAsync(t);
        var repository = new SqliteEquipmentRepository(options);
        var service = new EquipmentService(repository, tags, deviceRepository);

        var press = await service.CreateAsync("  Line 2 · Press ", device.Id,
            [new(temperature.Id, "chart"), new(pressure.Id, "VALUE"), new(cycle.Id, "VALUE"), new(pressure.Id, "CHART")]);
        var read = (await service.GetAllAsync()).Single();
        Require(read.Name == "Line 2 · Press" && read.DeviceId == device.Id, "name trimmed, device kept");
        Require(read.Items.Select(x => (x.TagId, x.View)).SequenceEqual([(temperature.Id, "CHART"), (pressure.Id, "VALUE"), (cycle.Id, "VALUE")]),
            "order kept, view normalized, a Tag shown once");

        await service.UpdateAsync(press.Id, "Line 2 · Press", null, [new(cycle.Id, "CHART")]);
        read = (await service.GetAllAsync()).Single();
        Require(read.DeviceId is null && read.Items.Single().TagId == cycle.Id, "update replaces device and Tags");

        await Throws(() => service.CreateAsync("", null, [new(cycle.Id, "VALUE")]), "name required");
        await Throws(() => service.CreateAsync("X", null, []), "at least one Tag");
        await Throws(() => service.CreateAsync("X", null, Enumerable.Range(0, 9).Select(_ => new EquipmentItem(Guid.NewGuid(), "VALUE")).ToArray()), "at most 8 Tags");
        await Throws(() => service.CreateAsync("X", null, [new(cycle.Id, "GAUGE")]), "known views only");
        await Throws(() => service.CreateAsync("X", null, [new(Guid.NewGuid(), "VALUE")]), "Tags must exist");
        await Throws(() => service.CreateAsync("X", Guid.NewGuid(), [new(cycle.Id, "VALUE")]), "device must exist");

        var boiler = await service.CreateAsync("Boiler 1", device.Id, [new(pressure.Id, "VALUE"), new(temperature.Id, "VALUE")]);
        await tags.DeleteAsync(pressure.Id);
        Require((await repository.GetByIdAsync(boiler.Id))!.Items.Single().TagId == temperature.Id, "a deleted Tag leaves the card");
        await deviceRepository.DeleteAsync(device.Id);
        Require((await repository.GetByIdAsync(boiler.Id))!.DeviceId is null, "a deleted device leaves the card without a device");
        Require(await service.DeleteAsync(boiler.Id) && await repository.GetByIdAsync(boiler.Id) is null && !await service.DeleteAsync(boiler.Id),
            "a card can be deleted once");
        Console.WriteLine("PASS: equipment (order and views, validation, Tag/device/card deletion)");
    }

    private static async Task Throws(Func<Task> action, string message)
    {
        try { await action(); }
        catch (ArgumentException) { return; }
        throw new Exception("Expected refusal: " + message);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
