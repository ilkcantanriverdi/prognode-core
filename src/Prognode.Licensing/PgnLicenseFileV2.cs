namespace Prognode.Licensing;

/// <summary>
/// A PROGNODE license envelope after its Ed25519 signature has been verified.
/// No payload claim is exposed by LicenseSignatureVerifier until verification succeeds.
/// </summary>
public sealed record PgnLicenseFileV2(
    string FormatVersion,
    string KeyId,
    string SignatureAlgorithm,
    string Canonicalization,
    LicensePayloadV2 Payload);
