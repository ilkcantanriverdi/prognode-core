namespace Prognode.Contracts.Devices;

/// <summary>User name and password a device (MQTT broker, OPC UA server) asks for.</summary>
public sealed record DeviceCredential(string Username, string Password)
{
    /// <summary>Changes when the user name or password changes, without exposing the password.</summary>
    public string Fingerprint => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(Username + "\n" + Password)));

    public override string ToString() => $"DeviceCredential {{ Username = {Username} }}";
}

/// <summary>
/// Keeps device credentials outside the device record, encrypted on the PROGNODE Monitor PC.
/// Passwords are never returned to the browser; only the user name is shown.
/// </summary>
public interface IDeviceCredentialStore
{
    DeviceCredential? Get(Guid deviceId);
    string? GetUsername(Guid deviceId);
    /// <summary>Saves or, with <c>null</c>, removes the credential of a device.</summary>
    void Save(Guid deviceId, DeviceCredential? credential);
}
