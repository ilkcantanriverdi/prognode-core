namespace Prognode.Contracts.Licensing;

/// <summary>
/// DEV12 signed license envelope. Payload is intentionally kept as an encoded/raw value by the
/// licensing implementation so the exact signed bytes can be verified before any trust is placed
/// in account, offline-auth, expiry, or entitlement fields.
/// </summary>
public sealed record SignedLicenseFileDocument(
    string FormatVersion,
    string KeyId,
    string Payload,
    string Signature);
