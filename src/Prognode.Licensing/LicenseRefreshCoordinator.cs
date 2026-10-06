namespace Prognode.Licensing;

public sealed class LicenseRefreshCoordinator(
    LicenseImportService importer,
    IEnumerable<ILicenseRefreshSource> sources)
{
    public async Task<bool> TryRefreshAsync(
        PgnLicenseFileV2 current,
        CancellationToken cancellationToken = default)
    {
        foreach (var source in sources)
        {
            var bytes = await source.TryGetNewerLicenseAsync(
                current.Payload.LicenseId,
                current.Payload.LicenseRevision,
                cancellationToken);
            if (bytes is null || bytes.Length == 0)
                continue;

            await using var stream = new MemoryStream(bytes, writable: false);
            await importer.ImportAsync(stream, cancellationToken);
            return true;
        }

        return false;
    }
}
