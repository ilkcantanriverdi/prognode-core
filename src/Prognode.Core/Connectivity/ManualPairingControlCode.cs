using System.Security.Cryptography;
using System.Text;

namespace Prognode.Core.Connectivity;

/// <summary>Versioned 60-bit visual authentication string, NEVER a replacement for the full TLS pin.
/// Both Core and the mobile client independently compute this from the physical server ID and
/// the DER bytes of the actual HTTPS leaf certificate. It must be compared across two screens.</summary>
public static class ManualPairingControlCode
{
    public const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    private static readonly byte[] Domain = Encoding.UTF8.GetBytes("PROGNODE-MANUAL-PAIR-V1\0");

    public static string FromCertificate(Guid serverId, ReadOnlySpan<byte> certificateDer) =>
        FromDigest(serverId, SHA256.HashData(certificateDer));

    public static string FromDigest(Guid serverId, ReadOnlySpan<byte> certSha256)
    {
        if (serverId == Guid.Empty) throw new ArgumentException("A canonical server UUID is required.", nameof(serverId));
        if (certSha256.Length != 32) throw new ArgumentException("Certificate SHA-256 must have 32 raw bytes.", nameof(certSha256));
        var id = Encoding.UTF8.GetBytes(serverId.ToString("D").ToLowerInvariant());
        var buffer = new byte[Domain.Length + id.Length + 1 + 32];
        Domain.CopyTo(buffer, 0);
        id.CopyTo(buffer, Domain.Length);
        buffer[Domain.Length + id.Length] = 0;
        certSha256.CopyTo(buffer.AsSpan(Domain.Length + id.Length + 1));
        var hash = SHA256.HashData(buffer);
        // 60 high-order bits, 12 x 5 bits. Preserve leading zeroes.
        Span<char> digits = stackalloc char[12];
        for (var i = 0; i < 12; i++)
        {
            var bit = i * 5;
            var offset = bit / 8;
            var shift = 11 - bit % 8; // two bytes of available input for a 5-bit window
            var segment = (hash[offset] << 8) | hash[offset + 1];
            digits[i] = Alphabet[(segment >> shift) & 31];
        }
        return new string(digits[..4]) + "-" + new string(digits[4..8]) + "-" + new string(digits[8..12]);
    }
}
