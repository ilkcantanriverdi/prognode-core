using System.Collections.Concurrent;
using System.Security.Cryptography;
using Prognode.Contracts.Licensing;

namespace Prognode.Licensing;

public sealed record LocalAccessLoginRequest(string Email, string Password);

public sealed record LocalAccessSessionResult(
    bool Authenticated,
    string? Token,
    DateTimeOffset? ExpiresAtUtc,
    string? UserId,
    string? UserName,
    string? UserEmail,
    string? Plan,
    string? Status,
    string? PortalRole = null,
    string? IndustrialRole = null);

/// <summary>
/// Local-only session boundary. Cloud activation is deliberately absent from this service.
/// </summary>
public sealed class LocalAccessSessionService(
    FileBackedLicenseProvider provider,
    OfflineCredentialVerifier credentialVerifier,
    LicenseEntitlementService entitlementService)
{
    private sealed record Session(
        string Token,
        DateTimeOffset ExpiresAtUtc,
        string LicenseId,
        int LicenseRevision,
        int CredentialRevision,
        string UserId,
        string Email);

    private readonly ConcurrentDictionary<string, Session> _sessions = new(StringComparer.Ordinal);

    public LocalAccessSessionResult GetStatus(string? token)
    {
        var snapshot = provider.GetCurrent();

        if (string.IsNullOrWhiteSpace(token) || !_sessions.TryGetValue(token, out var session))
            return Anonymous(snapshot);

        if (session.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            _sessions.TryRemove(token, out _);
            return Anonymous(snapshot);
        }

        try
        {
            var current = provider.GetVerifiedCurrent();
            entitlementService.ValidateAndGetModules(current.Payload, rejectExpired: true);
            var p = current.Payload;

            if (!string.Equals(p.LicenseId, session.LicenseId, StringComparison.Ordinal) ||
                p.LicenseRevision != session.LicenseRevision ||
                p.OfflineAuth.CredentialRevision != session.CredentialRevision ||
                !string.Equals(p.Account.UserId, session.UserId, StringComparison.Ordinal) ||
                !string.Equals(p.Account.Email, session.Email, StringComparison.Ordinal))
            {
                _sessions.TryRemove(token, out _);
                return Anonymous(provider.GetCurrent());
            }

            var lifecycle = entitlementService.GetLicenseStatus(p);
            return new LocalAccessSessionResult(true, session.Token, session.ExpiresAtUtc,
                p.Account.UserId, p.Account.DisplayName, p.Account.Email,
                p.Subscription.Product, lifecycle, p.Account.PortalRole, null);
        }
        catch
        {
            _sessions.TryRemove(token, out _);
            return Anonymous(provider.GetCurrent());
        }
    }

    public LocalAccessSessionResult Login(LocalAccessLoginRequest request)
    {
        var snapshot = provider.GetCurrent();
        if (!snapshot.IsValid)
            throw new InvalidOperationException("Import a valid signed PROGNODE license before signing in.");


        // Exact offline decision chain. There is deliberately no HTTP/API/cloud call here.
        // 1-7 parse/validate/canonicalize/Ed25519 verify happen inside GetVerifiedCurrent().
        var file = provider.GetVerifiedCurrent();

        // 8-9 ACTIVE/EXPIRING_SOON/GRACE lifecycle + product entitlement validation.
        entitlementService.ValidateAndGetModules(file.Payload, rejectExpired: true);

        // 10-15 email normalization + Argon2id + constant-time compare.
        if (!credentialVerifier.Verify(file.Payload, request.Email, request.Password))
            throw new InvalidOperationException("Email or password is incorrect.");

        // 16 local session only.
        var token = Base64UrlNoPadding.Encode(RandomNumberGenerator.GetBytes(32));
        var expires = DateTimeOffset.UtcNow.AddHours(12);
        var p = file.Payload;
        _sessions[token] = new Session(token, expires, p.LicenseId, p.LicenseRevision,
            p.OfflineAuth.CredentialRevision, p.Account.UserId, p.Account.Email);
        CleanupExpired();

        // Portal role is informational only. No OWNER -> Administrator mapping exists.
        return new LocalAccessSessionResult(true, token, expires,
            p.Account.UserId, p.Account.DisplayName, p.Account.Email,
            p.Subscription.Product, entitlementService.GetLicenseStatus(p), p.Account.PortalRole, null);
    }

    public bool Validate(string? token) => GetStatus(token).Authenticated;

    public bool VerifyCurrentPassword(string? token, string password)
    {
        var session = GetStatus(token);
        if (!session.Authenticated || string.IsNullOrWhiteSpace(session.UserEmail) ||
            string.IsNullOrEmpty(password)) return false;
        try
        {
            var current = provider.GetVerifiedCurrent();
            entitlementService.ValidateAndGetModules(current.Payload, rejectExpired: true);
            return credentialVerifier.Verify(current.Payload, session.UserEmail, password);
        }
        catch { return false; }
    }

    public void Logout(string? token)
    {
        if (!string.IsNullOrWhiteSpace(token))
            _sessions.TryRemove(token, out _);
    }

    private static LocalAccessSessionResult Anonymous(LicenseSnapshot snapshot)
    {
        var installed = snapshot.LicenseId is not ("NONE" or "INVALID");
        return new(false, null, null, null, null, null,
            null, installed ? "SIGN_IN_REQUIRED" : snapshot.Status, null, null);
    }

    private void CleanupExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var item in _sessions)
            if (item.Value.ExpiresAtUtc <= now)
                _sessions.TryRemove(item.Key, out _);
    }
}
