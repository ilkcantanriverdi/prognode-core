namespace Prognode.Licensing;

public sealed class LocalLicenseStore(string licensePath)
{
    public string LicensePath { get; } = licensePath;

    public bool Exists => File.Exists(LicensePath);

    public byte[] ReadAllBytes()
    {
        if (!Exists)
            throw new InvalidOperationException("License file is not installed.");
        return File.ReadAllBytes(LicensePath);
    }

    public async Task WriteAtomicAsync(byte[] rawBytes, CancellationToken cancellationToken = default)
    {
        var folder = Path.GetDirectoryName(LicensePath)
            ?? throw new InvalidOperationException("License storage path is invalid.");
        Directory.CreateDirectory(folder);

        var temp = LicensePath + ".tmp";
        await File.WriteAllBytesAsync(temp, rawBytes, cancellationToken);
        File.Move(temp, LicensePath, overwrite: true);
    }
}
