using System.Text;
using NSec.Cryptography;

namespace Prognode.Licensing;

/// <summary>
/// Ed25519 signature of a published installer (installer/sign-release.cjs). The private key is kept offline,
/// so a compromised PROGNODE Account or release store cannot make PROGNODE Monitor install anything else.
/// </summary>
public static class ReleaseSignature
{
    /// <summary>Raw Ed25519 release public key (base64). Empty: no update can be verified, so none installs.</summary>
    public const string PublicKey = "";

    public static byte[] Message(string product, string version, string sha256) =>
        Encoding.UTF8.GetBytes($"PROGNODE-RELEASE-V1\n{product}\n{version}\n{sha256}");

    public static bool Verify(string publicKeyBase64, string product, string version, string sha256, string? signatureBase64)
    {
        try
        {
            var key = NSec.Cryptography.PublicKey.Import(SignatureAlgorithm.Ed25519, Convert.FromBase64String(publicKeyBase64), KeyBlobFormat.RawPublicKey);
            return signatureBase64 is not null &&
                SignatureAlgorithm.Ed25519.Verify(key, Message(product, version, sha256), Convert.FromBase64String(signatureBase64));
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException) { return false; }
    }
}
