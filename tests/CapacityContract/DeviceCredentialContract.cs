using Microsoft.Data.Sqlite;
using Prognode.Contracts.Devices;
using Prognode.Data.Sqlite;

/// <summary>
/// Device credentials: encrypted at rest, never stored in plain text, replaced and removed on
/// request, gone with their device, and unreadable (not wrong) on another PC.
/// </summary>
internal static class DeviceCredentialContract
{
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    public static async Task RunAsync(string root)
    {
        var options = new SqliteDatabaseOptions(Path.Combine(root, "credential-contract.db"));
        await new DatabaseInitializer(options).InitializeAsync();
        var now = DateTimeOffset.UtcNow;
        var devices = new SqliteDeviceRepository(options);
        var device = new DeviceDefinition(Guid.NewGuid(), "Broker", "MQTT", "Configured", "mqtt://127.0.0.1", 1883, null, 1000, now, now);
        await devices.AddAsync(device);

        var store = new SqliteDeviceCredentialStore(options);
        Check(store.Get(device.Id) is null && store.GetUsername(device.Id) is null, "A new device must have no credential.");
        store.Save(device.Id, new DeviceCredential("plant", "Pässwörd-1"));
        var fresh = new SqliteDeviceCredentialStore(options); // no cache: read back from the database
        Check(fresh.Get(device.Id) is { Username: "plant", Password: "Pässwörd-1" }, "Credential did not round-trip.");
        Check(fresh.GetUsername(device.Id) == "plant", "User name not readable on its own.");

        await using (var connection = new SqliteConnection(options.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT secret FROM device_credentials;";
            var secret = (byte[])(await command.ExecuteScalarAsync())!;
            var bytes = Convert.ToHexString(secret);
            Check(!bytes.Contains(Convert.ToHexString(System.Text.Encoding.UTF8.GetBytes("Pässwörd-1"))) &&
                  !bytes.Contains(Convert.ToHexString(System.Text.Encoding.Unicode.GetBytes("Pässwörd-1"))),
                "Password is stored in plain text.");

            // A database copied to another PC: DPAPI cannot decrypt, so the password must be asked again.
            command.CommandText = "UPDATE device_credentials SET secret=x'0102030405';";
            await command.ExecuteNonQueryAsync();
        }
        var moved = new SqliteDeviceCredentialStore(options);
        Check(moved.Get(device.Id) is null && moved.GetUsername(device.Id) == "plant",
            "Undecryptable password must read as missing, keeping the user name.");

        store.Save(device.Id, new DeviceCredential("operator", "second"));
        Check(new SqliteDeviceCredentialStore(options).Get(device.Id) is { Username: "operator", Password: "second" }, "Credential not replaced.");
        Check(!new DeviceCredential("a", "secret").ToString().Contains("secret"), "Credential ToString leaks the password.");

        store.Save(device.Id, null);
        Check(new SqliteDeviceCredentialStore(options).Get(device.Id) is null, "Credential not removed.");

        store.Save(device.Id, new DeviceCredential("plant", "x"));
        await devices.DeleteAsync(device.Id);
        Check(new SqliteDeviceCredentialStore(options).Get(device.Id) is null, "Credential survived its device.");
        Console.WriteLine("PASS device credentials: DPAPI-encrypted, replace/remove, deleted with the device");
    }
}
