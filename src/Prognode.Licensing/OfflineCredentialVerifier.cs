using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace Prognode.Licensing;

public sealed class OfflineCredentialVerifier
{
    public bool Verify(LicensePayloadV2 payload, string? enteredEmail, string? enteredPassword)
    {
        var normalizedEnteredEmail = NormalizeEmail(enteredEmail);
        if (!string.Equals(payload.Account.Email, normalizedEnteredEmail, StringComparison.Ordinal))
            return false;

        // Password contract: exact .NET string -> UTF-8 bytes. No Trim and no Unicode normalization.
        var passwordBytes = Encoding.UTF8.GetBytes(enteredPassword ?? string.Empty);
        byte[]? actual = null;
        try
        {
            var auth = payload.OfflineAuth;
            using var argon2 = new Argon2id(passwordBytes)
            {
                Salt = auth.Salt,
                MemorySize = auth.MemoryCostKiB,
                Iterations = auth.Iterations,
                DegreeOfParallelism = auth.Parallelism
            };

            actual = argon2.GetBytes(auth.HashLength);
            return CryptographicOperations.FixedTimeEquals(auth.Verifier, actual);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or OutOfMemoryException)
        {
            throw new InvalidOperationException("The signed offline authentication parameters are invalid on this system.", ex);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(passwordBytes);
            if (actual is not null)
                CryptographicOperations.ZeroMemory(actual);
        }
    }

    public static string NormalizeEmail(string? value) => (value ?? string.Empty).Trim().ToLowerInvariant();
}
