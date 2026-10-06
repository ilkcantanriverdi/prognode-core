using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Prognode.Core.Connectivity;

namespace Prognode.Web;

// This check runs IN the Core process / Windows service identity, never in the elevated
// setup helper. A TCP listener or HasPrivateKey alone does not prove usable Schannel TLS.
public sealed record LanTlsHealthResult(bool Ready, bool PrivateKeyUsable, bool HandshakeSucceeded,
    string DiagnosticCode, string? DiagnosticMessage);

public static class LanTlsHealth
{
    public static async Task<LanTlsHealthResult> CheckAsync(
        X509Certificate2? configured, ServerIdentitySnapshot expected,
        CancellationToken cancellationToken = default)
    {
        if (configured is null || expected.SecureApiPort != 5443 ||
            string.IsNullOrWhiteSpace(expected.CertificateSha256))
            return new(false, false, false, "HTTPS_NOT_CONFIGURED", "Enable the configured HTTPS certificate first.");

        // This signing operation is deliberately executed by the actual Core identity.
        // It catches cases where Schannel can see a store certificate but cannot open
        // its CNG/CSP private key (Windows Schannel 36870 / 0x8009030D).
        try
        {
            var sample = RandomNumberGenerator.GetBytes(32);
            using var rsa = configured.GetRSAPrivateKey();
            if (rsa is not null)
            {
                var signature = rsa.SignData(sample, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                using var pub = configured.GetRSAPublicKey();
                if (pub is null || !pub.VerifyData(sample, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                    return new(false, false, false, "TLS_KEY_MISMATCH", "Private key does not match configured public certificate.");
            }
            else
            {
                using var ecdsa = configured.GetECDsaPrivateKey();
                if (ecdsa is null)
                    return new(false, false, false, "TLS_KEY_UNSUPPORTED", "Expected RSA or ECDSA TLS private key.");
                var signature = ecdsa.SignData(sample, HashAlgorithmName.SHA256);
                using var pub = configured.GetECDsaPublicKey();
                if (pub is null || !pub.VerifyData(sample, signature, HashAlgorithmName.SHA256))
                    return new(false, false, false, "TLS_KEY_MISMATCH", "Private key does not match configured public certificate.");
            }
        }
        catch (Exception ex) when (ex is CryptographicException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            return new(false, false, false, "TLS_PRIVATE_KEY_ACCESS_DENIED",
                "Core service identity cannot sign with the configured private key; request local UAC repair. " + ex.GetType().Name);
        }

        var expectedPin = expected.CertificateSha256.Replace(" ", "", StringComparison.Ordinal);
        byte[] expectedBytes;
        try { expectedBytes = Convert.FromHexString(expectedPin); }
        catch (FormatException) { return new(false, true, false, "INVALID_CERTIFICATE_PIN", "Configured SHA-256 pin is invalid."); }
        if (expectedBytes.Length != 32)
            return new(false, true, false, "INVALID_CERTIFICATE_PIN", "Configured SHA-256 pin must contain 32 bytes.");
        var mismatchedPin = false;
        using var handler = new HttpClientHandler { UseProxy = false, AllowAutoRedirect = false };
        handler.ServerCertificateCustomValidationCallback = (_, certificate, _, _) =>
        {
            if (certificate is null) { mismatchedPin = true; return false; }
            var actual = SHA256.HashData(certificate.RawData);
            var inValidity = certificate.NotBefore.ToUniversalTime() <= DateTime.UtcNow &&
                             certificate.NotAfter.ToUniversalTime() > DateTime.UtcNow;
            var matches = CryptographicOperations.FixedTimeEquals(actual, expectedBytes) && inValidity;
            if (!matches) mismatchedPin = true;
            // Accept a self-signed certificate ONLY when its immutable, independently
            // configured full SHA-256 pin matches. Never use an unconditional bypass.
            return matches;
        };
        using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(3) };
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        linked.CancelAfter(TimeSpan.FromSeconds(3));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://127.0.0.1:5443/api/server/identity");
            request.Headers.ConnectionClose = true; // New handshake for each health probe.
            using var response = await client.SendAsync(request, linked.Token).ConfigureAwait(false);
            if (response.StatusCode != HttpStatusCode.OK)
                return new(false, true, true, "TLS_IDENTITY_HTTP_ERROR", "TLS succeeded but /api/server/identity did not return HTTP 200.");
            await using var stream = await response.Content.ReadAsStreamAsync(linked.Token).ConfigureAwait(false);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: linked.Token).ConfigureAwait(false);
            if (!json.RootElement.TryGetProperty("serverId", out var value) || value.ValueKind != JsonValueKind.String ||
                !Guid.TryParse(value.GetString(), out var serverId) || serverId != expected.ServerId)
                return new(false, true, true, "TLS_SERVER_ID_MISMATCH", "Pinned TLS endpoint returned an unexpected Core identity.");
            return new(true, true, true, "TLS_OK", null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or System.IO.IOException)
        {
            return new(false, true, false, mismatchedPin ? "TLS_PIN_MISMATCH" :
                ex is TaskCanceledException ? "TLS_HANDSHAKE_TIMEOUT" :
                ex is JsonException ? "TLS_IDENTITY_INVALID" : "TLS_HANDSHAKE_FAILED",
                mismatchedPin ? "The server certificate did not match the configured SHA-256 pin." :
                "Pinned localhost TLS handshake failed; check Schannel 36870, service key access and Kestrel binding. " + ex.GetType().Name);
        }
    }
}
