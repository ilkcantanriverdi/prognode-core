namespace Prognode.Licensing;

public sealed class LicenseVerificationOptions
{
    public LicenseVerificationOptions(
        IReadOnlyDictionary<string, string>? trustedPublicKeys = null,
        IReadOnlyDictionary<string, string>? trustedPublicKeyFingerprintsSha256 = null,
        string? baseDirectory = null)
    {
        TrustedPublicKeys = trustedPublicKeys ?? new Dictionary<string, string>(StringComparer.Ordinal);
        TrustedPublicKeyFingerprintsSha256 = trustedPublicKeyFingerprintsSha256 ?? new Dictionary<string, string>(StringComparer.Ordinal);
        BaseDirectory = string.IsNullOrWhiteSpace(baseDirectory) ? AppContext.BaseDirectory : baseDirectory;
    }

    /// <summary>
    /// keyId -> Ed25519 SPKI PEM material or file:path to a PEM file.
    /// Only public keys belong in Core. Private signing keys must never be stored here.
    /// </summary>
    public IReadOnlyDictionary<string, string> TrustedPublicKeys { get; }

    /// <summary>
    /// Optional keyId -> SHA-256 fingerprint of the complete DER SubjectPublicKeyInfo bytes.
    /// When configured, a PEM with a different fingerprint is rejected.
    /// </summary>
    public IReadOnlyDictionary<string, string> TrustedPublicKeyFingerprintsSha256 { get; }

    public string BaseDirectory { get; }
}
