using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Data.Sqlite;
using Prognode.Contracts.Devices;

namespace Prognode.Data.Sqlite;

/// <summary>
/// Device credentials in their own table, the password encrypted with Windows DPAPI for this PC
/// (the PROGNODE Monitor service account). A copy of the database restored on another PC keeps the
/// user names; the passwords then have to be entered again (<see cref="Get"/> returns null).
/// </summary>
public sealed class SqliteDeviceCredentialStore(SqliteDatabaseOptions options) : IDeviceCredentialStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("PROGNODE.DeviceCredential.v1");
    private readonly ConcurrentDictionary<Guid, (string Username, byte[] Secret)?> _cache = new();
    private readonly object _schemaGate = new();
    private bool _schemaReady;

    public DeviceCredential? Get(Guid deviceId)
    {
        if (Load(deviceId) is not { } row) return null;
        try
        {
            var password = Encoding.UTF8.GetString(Unprotect(row.Secret));
            return new DeviceCredential(row.Username, password);
        }
        catch (CryptographicException)
        {
            return null; // Encrypted on another PC (restored backup): ask for the password again.
        }
    }

    public string? GetUsername(Guid deviceId) => Load(deviceId)?.Username;

    public void Save(Guid deviceId, DeviceCredential? credential)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        if (credential is null || string.IsNullOrWhiteSpace(credential.Username))
        {
            command.CommandText = "DELETE FROM device_credentials WHERE device_id=$id;";
            command.Parameters.AddWithValue("$id", deviceId.ToString());
            command.ExecuteNonQuery();
            _cache[deviceId] = null;
            return;
        }

        var username = credential.Username.Trim();
        if (username.Length > 256 || credential.Password.Length > 1024)
            throw new ArgumentException("User name or password is too long.");
        var secret = Protect(Encoding.UTF8.GetBytes(credential.Password));
        command.CommandText = """
            INSERT INTO device_credentials(device_id,username,secret,updated_at)
            VALUES($id,$user,$secret,$now)
            ON CONFLICT(device_id) DO UPDATE SET username=excluded.username,secret=excluded.secret,updated_at=excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$id", deviceId.ToString());
        command.Parameters.AddWithValue("$user", username);
        command.Parameters.AddWithValue("$secret", secret);
        command.Parameters.AddWithValue("$now", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
        _cache[deviceId] = (username, secret);
    }

    private (string Username, byte[] Secret)? Load(Guid deviceId)
    {
        if (_cache.TryGetValue(deviceId, out var cached)) return cached;
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT username,secret FROM device_credentials WHERE device_id=$id;";
        command.Parameters.AddWithValue("$id", deviceId.ToString());
        using var reader = command.ExecuteReader();
        (string, byte[])? row = reader.Read() ? (reader.GetString(0), (byte[])reader.GetValue(1)) : null;
        _cache[deviceId] = row;
        return row;
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(options.ConnectionString);
        connection.Open();
        using (var pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys=ON;";
            pragma.ExecuteNonQuery();
        }
        if (!_schemaReady)
        {
            lock (_schemaGate)
            {
                using var create = connection.CreateCommand();
                create.CommandText = """
                    CREATE TABLE IF NOT EXISTS device_credentials(
                      device_id TEXT PRIMARY KEY REFERENCES devices(id) ON DELETE CASCADE,
                      username TEXT NOT NULL,secret BLOB NOT NULL,updated_at TEXT NOT NULL);
                    """;
                create.ExecuteNonQuery();
                _schemaReady = true;
            }
        }
        return connection;
    }

    private static byte[] Protect(byte[] plain) =>
        OperatingSystem.IsWindows()
            ? ProtectedData.Protect(plain, Entropy, DataProtectionScope.LocalMachine)
            : throw new PlatformNotSupportedException("Device credentials need Windows DPAPI.");

    private static byte[] Unprotect(byte[] secret) =>
        OperatingSystem.IsWindows()
            ? ProtectedData.Unprotect(secret, Entropy, DataProtectionScope.LocalMachine)
            : throw new PlatformNotSupportedException("Device credentials need Windows DPAPI.");
}
