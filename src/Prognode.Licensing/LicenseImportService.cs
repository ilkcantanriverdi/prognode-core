namespace Prognode.Licensing;

public sealed class LicenseImportService(
    LocalLicenseStore store,
    LicenseSignatureVerifier verifier,
    LicenseEntitlementService entitlementService)
{
    private const int MaxLicenseBytes = 1024 * 1024;

    public async Task ImportAsync(Stream input, CancellationToken cancellationToken = default)
    {
        if (input is null || !input.CanRead)
            throw new InvalidOperationException("License file cannot be read.");

        await using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        var total = 0;
        while (true)
        {
            var read = await input.ReadAsync(chunk.AsMemory(0, chunk.Length), cancellationToken);
            if (read == 0) break;
            total += read;
            if (total > MaxLicenseBytes)
                throw new InvalidOperationException("License file is too large.");
            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        var rawBytes = buffer.ToArray();

        // Import trust order: exact envelope validation -> canonicalization -> Ed25519 verify ->
        // signed claim parsing -> lifecycle/entitlement validation. ACTIVE, EXPIRING_SOON and GRACE
        // imports are accepted; EXPIRED/INVALID licenses are rejected. No signed claim is trusted before verify.
        var incoming = verifier.Verify(rawBytes);
        entitlementService.ValidateAndGetModules(incoming.Payload, rejectExpired: true);

        PreventCredentialRollback(incoming);

        // Preserve the exact signed bytes. Never parse/re-serialize an imported signed license.
        await store.WriteAtomicAsync(rawBytes, cancellationToken);
    }

    private void PreventCredentialRollback(PgnLicenseFileV2 incoming)
    {
        if (!store.Exists)
            return;

        PgnLicenseFileV2 existing;
        try
        {
            existing = verifier.Verify(store.ReadAllBytes());
        }
        catch
        {
            // A corrupted/invalid installed file must not prevent recovery by importing a valid file.
            return;
        }

        if (!string.Equals(existing.Payload.LicenseId, incoming.Payload.LicenseId, StringComparison.Ordinal))
            return;

        if (incoming.Payload.LicenseRevision < existing.Payload.LicenseRevision)
            throw new InvalidOperationException("Older licenseRevision cannot replace the currently installed license.");

        if (incoming.Payload.LicenseRevision == existing.Payload.LicenseRevision &&
            incoming.Payload.OfflineAuth.CredentialRevision < existing.Payload.OfflineAuth.CredentialRevision)
            throw new InvalidOperationException("Older credentialRevision cannot replace the currently installed offline credential.");
    }
}
