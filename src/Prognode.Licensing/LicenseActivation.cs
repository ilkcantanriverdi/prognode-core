using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Win32;

namespace Prognode.Licensing;

/// <summary>
/// Signed proof from PROGNODE Cloud that one license is activated on one Core installation on one
/// machine. Issued by Control with the license signing key; verified with the compiled trust root.
/// </summary>
public sealed record ActivationCertificateV1(
    string KeyId,
    string LicenseId,
    string LicenseKey,
    string InstallationId,
    Guid ServerId,
    string MachineFingerprint,
    DateTimeOffset ActivatedAtUtc,
    DateTimeOffset IssuedAtUtc)
{
    public const string Schema = "prognode.activation/v1";
}

/// <summary>Fingerprint of the physical/virtual machine Core runs on.</summary>
public interface IMachineFingerprintProvider
{
    /// <summary>64 lowercase hex characters, stable for this machine.</summary>
    string GetFingerprint();
}

public static class MachineFingerprint
{
    public static bool IsWellFormed(string? value) =>
        value is { Length: 64 } && value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');

    /// <summary>SHA-256 of a namespaced machine identifier; the raw OS identifier never leaves the PC.</summary>
    public static string FromMachineId(string machineId)
    {
        var normalized = machineId.Trim().ToLowerInvariant();
        if (normalized.Length == 0)
            throw new InvalidOperationException("The machine identifier is empty.");
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes("PROGNODE-MACHINE-V1|" + normalized))).ToLowerInvariant();
    }
}

/// <summary>
/// Windows: HKLM\SOFTWARE\Microsoft\Cryptography\MachineGuid (created at OS install, unchanged by
/// copying PROGNODE's data folder). Linux: /etc/machine-id. Hashed before use.
/// </summary>
public sealed class OsMachineFingerprintProvider : IMachineFingerprintProvider
{
    private readonly Lazy<string> _fingerprint = new(Compute);

    public string GetFingerprint() => _fingerprint.Value;

    private static string Compute()
    {
        if (OperatingSystem.IsWindows())
        {
            using var hive = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = hive.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            if (key?.GetValue("MachineGuid") is string guid && !string.IsNullOrWhiteSpace(guid))
                return MachineFingerprint.FromMachineId(guid);
            throw new InvalidOperationException("Windows MachineGuid is not available.");
        }
        foreach (var path in new[] { "/etc/machine-id", "/var/lib/dbus/machine-id" })
            if (File.Exists(path) && File.ReadAllText(path).Trim() is { Length: > 0 } id)
                return MachineFingerprint.FromMachineId(id);
        throw new InvalidOperationException("No machine identifier is available on this system.");
    }
}

/// <summary>Stores the exact signed .pgnact bytes next to the installed license.</summary>
public sealed class LicenseActivationStore(string path)
{
    public string Path { get; } = path;
    public bool Exists => File.Exists(Path);
    public byte[] ReadAllBytes() => File.ReadAllBytes(Path);

    public void WriteAtomic(byte[] bytes)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Path)!);
        var temp = Path + ".tmp";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, Path, overwrite: true);
    }

    public void Clear()
    {
        if (File.Exists(Path))
            File.Delete(Path);
    }
}

public sealed record LicenseActivationState(
    bool Activated,
    string Reason,
    ActivationCertificateV1? Certificate,
    Guid ServerId,
    string? MachineFingerprint);

/// <summary>Unsigned request a customer uploads to PROGNODE Account when Core has no internet.</summary>
public sealed record ActivationRequestV1(
    string Schema,
    string LicenseId,
    string LicenseKey,
    Guid ServerId,
    string ServerName,
    string MachineFingerprint,
    string CoreVersion,
    DateTimeOffset CreatedAtUtc)
{
    public const string SchemaName = "prognode.activation-request/v1";
}

/// <summary>
/// Decides whether the installed license is activated on this Core and this machine. A license is
/// only operational with a certificate signed for exactly this licenseId, serverId and machine.
/// </summary>
public sealed class LicenseActivationService(
    LicenseSignatureVerifier verifier,
    LicenseActivationStore store,
    IMachineFingerprintProvider machine,
    Func<Guid> serverId)
{
    public LicenseActivationState Evaluate(string licenseId, string licenseKey)
    {
        var localServer = serverId();
        string fingerprint;
        try
        {
            fingerprint = machine.GetFingerprint();
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return new(false, "machine_identity_unavailable", null, localServer, null);
        }

        if (!store.Exists)
            return new(false, "not_activated", null, localServer, fingerprint);

        ActivationCertificateV1 certificate;
        try
        {
            certificate = verifier.VerifyActivation(store.ReadAllBytes());
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException)
        {
            return new(false, "activation_invalid", null, localServer, fingerprint);
        }

        var reason = Mismatch(certificate, licenseId, licenseKey, localServer, fingerprint);
        return reason is null
            ? new(true, "activated", certificate, localServer, fingerprint)
            : new(false, reason, certificate, localServer, fingerprint);
    }

    /// <summary>Verifies and stores a certificate; refuses one that is not for this license, Core and machine.</summary>
    public ActivationCertificateV1 Import(byte[] certificateBytes, string licenseId, string licenseKey)
    {
        if (certificateBytes.Length is 0 or > 64 * 1024)
            throw new InvalidOperationException("The activation file is empty or too large.");
        var certificate = verifier.VerifyActivation(certificateBytes);
        var reason = Mismatch(certificate, licenseId, licenseKey, serverId(), machine.GetFingerprint());
        if (reason is not null)
            throw new InvalidOperationException(reason switch
            {
                "license_mismatch" => "This activation file belongs to a different license.",
                "server_mismatch" => "This activation file was issued for a different PROGNODE Core installation.",
                _ => "This activation file was issued for a different computer.",
            });
        store.WriteAtomic(certificateBytes);
        return certificate;
    }

    public void Clear() => store.Clear();

    public ActivationRequestV1 CreateRequest(string licenseId, string licenseKey, string serverName, string coreVersion) =>
        new(ActivationRequestV1.SchemaName, licenseId, licenseKey, serverId(), serverName,
            machine.GetFingerprint(), coreVersion, DateTimeOffset.UtcNow);

    public static byte[] SerializeRequest(ActivationRequestV1 request) =>
        JsonSerializer.SerializeToUtf8Bytes(request, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });

    private static string? Mismatch(ActivationCertificateV1 certificate, string licenseId, string licenseKey, Guid localServer, string fingerprint)
    {
        if (!string.Equals(certificate.LicenseId, licenseId, StringComparison.Ordinal) ||
            !string.Equals(certificate.LicenseKey, licenseKey, StringComparison.Ordinal))
            return "license_mismatch";
        if (certificate.ServerId != localServer)
            return "server_mismatch";
        if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(certificate.MachineFingerprint), Encoding.ASCII.GetBytes(fingerprint)))
            return "machine_mismatch";
        return null;
    }
}

/// <summary>Version of the running Core, reported to Cloud and written into activation requests.</summary>
public static class CoreVersionInfo
{
    public static string Current { get; } =
        System.Reflection.Assembly.GetEntryAssembly()
            ?.GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>().FirstOrDefault()?.InformationalVersion
        ?? System.Reflection.Assembly.GetEntryAssembly()?.GetName().Version?.ToString()
        ?? "unknown";
}
