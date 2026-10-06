namespace Prognode.Licensing;

/// <summary>
/// Future cloud refresh boundary. Local runtime never depends on an implementation of this interface.
/// A future cloud adapter may return newer signed .pgnlicense bytes; verification/import remains local.
/// </summary>
public interface ILicenseRefreshSource
{
    Task<byte[]?> TryGetNewerLicenseAsync(
        string licenseId,
        int currentLicenseRevision,
        CancellationToken cancellationToken = default);
}
