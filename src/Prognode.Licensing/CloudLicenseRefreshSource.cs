namespace Prognode.Licensing;

/// <summary>
/// Downloads a newer signed license for the activated installation from PROGNODE Cloud. Only a
/// strictly higher licenseRevision is returned; LicenseImportService still verifies the signature,
/// lifecycle and rollback rules before anything is stored.
/// </summary>
public sealed class CloudLicenseRefreshSource(
    CoreCloudLicenseClient cloud,
    CoreCloudLicenseStateStore state) : ILicenseRefreshSource
{
    public async Task<byte[]?> TryGetNewerLicenseAsync(
        string licenseId,
        int currentLicenseRevision,
        CancellationToken cancellationToken = default)
    {
        if (!cloud.IsConfigured)
            return null;
        var token = state.Load(configured: true).ActivationToken;
        if (string.IsNullOrWhiteSpace(token))
            return null;
        var download = await cloud.DownloadLicenseAsync(token, licenseId, cancellationToken);
        return download is { } d && d.Revision > currentLicenseRevision ? d.Document : null;
    }
}
