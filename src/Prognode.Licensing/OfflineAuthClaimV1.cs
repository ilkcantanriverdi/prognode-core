namespace Prognode.Licensing;

public sealed record OfflineAuthClaimV1(
    int Version,
    string Algorithm,
    int Argon2Version,
    byte[] Salt,
    byte[] Verifier,
    int MemoryCostKiB,
    int Iterations,
    int Parallelism,
    int HashLength,
    string Encoding,
    int CredentialRevision);
