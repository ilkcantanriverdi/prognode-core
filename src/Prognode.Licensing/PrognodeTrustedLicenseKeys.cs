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

    public static LicenseVerificationOptions CreateVerificationOptions(string? baseDirectory = null) =>
        new(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ProductionKeyId] = ProductionPublicKeyPem
            },
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [ProductionKeyId] = ProductionSpkiSha256
            },
            baseDirectory);
}
