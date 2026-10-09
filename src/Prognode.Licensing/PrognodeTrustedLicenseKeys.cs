namespace Prognode.Licensing;

/// <summary>
/// Production trusted PROGNODE license verification keys.
/// Public verification material only. Private signing keys must never be shipped with Core.
/// The production key is compiled into Core so changing appsettings cannot replace the trust root.
/// </summary>
public static class PrognodeTrustedLicenseKeys
{
    public const string ProductionKeyId = "prognode-license-2026-01";

    public const string ProductionPublicKeyPem = """
-----BEGIN PUBLIC KEY-----
MCowBQYDK2VwAyEAx4iraQxaLf283ALTshHFEZySBAPNcaln2RHZS3b6l4Q=
-----END PUBLIC KEY-----
""";

    public const string ProductionSpkiSha256 =
        "3e902011b79009ec3b1e66fb7fbfddb24567eb438479802c36cb8d4db6c3e402";

    /// <summary>Key id a local PROGNODE Cloud development stack signs with (Debug builds only).</summary>
    public const string LocalDevelopmentKeyId = "prognode-local-dev";

    public static LicenseVerificationOptions CreateVerificationOptions(string? baseDirectory = null)
    {
        var keys = new Dictionary<string, string>(StringComparer.Ordinal) { [ProductionKeyId] = ProductionPublicKeyPem };
#if DEBUG
        // End-to-end tests against a local Cloud: trust its throwaway public key under its own key id.
        // Compiled out of Release builds, so a customer Core trusts only the production key.
        var localKey = Environment.GetEnvironmentVariable("PROGNODE_DEV_LICENSE_PUBLIC_KEY");
        if (!string.IsNullOrWhiteSpace(localKey))
            keys[LocalDevelopmentKeyId] = localKey.Replace("\\n", "\n");
#endif
        return new(
            keys,
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ProductionKeyId] = ProductionSpkiSha256
            },
            baseDirectory);
    }
}
