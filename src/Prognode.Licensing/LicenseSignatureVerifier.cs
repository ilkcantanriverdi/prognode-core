using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NSec.Cryptography;

namespace Prognode.Licensing;

/// <summary>
/// Strict PROGNODE v2 signature verifier.
/// Security-sensitive payload claims are parsed only after the exact signed envelope has passed Ed25519 verification.
/// </summary>
public sealed class LicenseSignatureVerifier(LicenseVerificationOptions options)
{
    private const string ExpectedFormatVersion = "2.0";
    private const string ExpectedSignatureAlgorithm = "Ed25519";
    private const string ExpectedCanonicalization = "PGN_CANONICAL_JSON_1";
    private const string ExpectedPayloadSchema = "prognode.license.payload/v2";
    private const string Ed25519SpkiOid = "1.3.101.112";

    private static readonly SignatureAlgorithm Ed25519 = SignatureAlgorithm.Ed25519;
    private static readonly HashSet<string> AllowedEnvelopeFields = new(StringComparer.Ordinal)
    {
        "formatVersion", "keyId", "signatureAlgorithm", "canonicalization", "payload", "signature"
    };

    public PgnLicenseFileV2 Verify(ReadOnlySpan<byte> licenseBytes) =>
        VerifyEnvelope(licenseBytes, "License", (envelope, payload) => new PgnLicenseFileV2(
            envelope.FormatVersion, envelope.KeyId, envelope.SignatureAlgorithm, envelope.Canonicalization,
            ParseVerifiedPayload(payload)));

    /// <summary>
    /// Verifies a PROGNODE activation certificate (.pgnact) with exactly the same envelope, canonical
    /// JSON and trusted-key rules as a license. Claims are parsed only after the signature verifies.
    /// </summary>
    public ActivationCertificateV1 VerifyActivation(ReadOnlySpan<byte> certificateBytes) =>
        VerifyEnvelope(certificateBytes, "Activation", (envelope, payload) => ParseVerifiedActivation(payload, envelope.KeyId));

    private readonly record struct EnvelopeInfo(string FormatVersion, string KeyId, string SignatureAlgorithm, string Canonicalization);

    private T VerifyEnvelope<T>(ReadOnlySpan<byte> bytes, string kind, Func<EnvelopeInfo, JsonElement, T> parse)
    {
        var lower = kind.ToLowerInvariant();
        if (bytes.IsEmpty)
            throw new InvalidOperationException($"{kind} file is empty.");

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(bytes.ToArray());
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"{kind} file is not valid JSON.", ex);
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException($"{kind} file must contain a JSON object.");

            ValidateNoDuplicateProperties(root);
            ValidateNoUnexpectedEnvelopeFields(root);

            var formatVersion = RequiredExactString(root, "formatVersion");
            var keyId = RequiredExactString(root, "keyId");
            var signatureAlgorithm = RequiredExactString(root, "signatureAlgorithm");
            var canonicalization = RequiredExactString(root, "canonicalization");
            var signatureText = RequiredExactString(root, "signature");

            if (!string.Equals(formatVersion, ExpectedFormatVersion, StringComparison.Ordinal))
                throw new InvalidOperationException($"Unsupported {lower} formatVersion '{formatVersion}'.");
            if (!string.Equals(signatureAlgorithm, ExpectedSignatureAlgorithm, StringComparison.Ordinal))
                throw new InvalidOperationException($"Unsupported {lower} signatureAlgorithm '{signatureAlgorithm}'.");
            if (!string.Equals(canonicalization, ExpectedCanonicalization, StringComparison.Ordinal))
                throw new InvalidOperationException($"Unsupported {lower} canonicalization '{canonicalization}'.");

            if (!root.TryGetProperty("payload", out var payloadElement) || payloadElement.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException($"{kind} payload must be a JSON object.");

            var publicKey = LoadTrustedPublicKey(keyId);
            var signatureBytes = Base64UrlNoPadding.Decode(signatureText, "signature");
            if (signatureBytes.Length != Ed25519.SignatureSize)
                throw new InvalidOperationException($"Ed25519 signature must decode to exactly {Ed25519.SignatureSize} bytes.");

            // Signed bytes are EXACTLY the canonicalized object containing:
            // formatVersion, keyId, signatureAlgorithm, canonicalization, payload.
            var signedBytes = PgnCanonicalJsonV1.CanonicalizeSignedEnvelope(root);
            if (!Ed25519.Verify(publicKey, signedBytes, signatureBytes))
                throw new InvalidOperationException($"PROGNODE {lower} Ed25519 signature is invalid.");

            // Nothing below this line is trusted until the signature has verified.
            return parse(new EnvelopeInfo(formatVersion, keyId, signatureAlgorithm, canonicalization), payloadElement);
        }
    }

    private static ActivationCertificateV1 ParseVerifiedActivation(JsonElement payload, string keyId)
    {
        var schema = RequiredExactString(payload, "schema");
        if (!string.Equals(schema, ActivationCertificateV1.Schema, StringComparison.Ordinal))
            throw new InvalidOperationException($"Unsupported activation payload schema '{schema}'.");
        var issuer = RequiredExactString(payload, "issuer");
        if (!string.Equals(issuer, "PROGNODE", StringComparison.Ordinal))
            throw new InvalidOperationException($"Unsupported activation issuer '{issuer}'.");

        var serverIdText = RequiredExactString(payload, "serverId");
        if (!Guid.TryParse(serverIdText, out var serverId) || serverId == Guid.Empty)
            throw new InvalidOperationException("Activation serverId must be a non-empty GUID.");
        var fingerprint = RequiredExactString(payload, "machineFingerprint");
        if (!MachineFingerprint.IsWellFormed(fingerprint))
            throw new InvalidOperationException("Activation machineFingerprint must be 64 lowercase hex characters.");

        return new ActivationCertificateV1(
            keyId,
            RequiredExactString(payload, "licenseId"),
            RequiredExactString(payload, "licenseKey"),
            RequiredExactString(payload, "installationId"),
            serverId,
            fingerprint,
            RequiredUtcTimestamp(payload, "activatedAtUtc"),
            RequiredUtcTimestamp(payload, "issuedAtUtc"));
    }

    private PublicKey LoadTrustedPublicKey(string keyId)
    {
        if (!options.TrustedPublicKeys.TryGetValue(keyId, out var configured) || string.IsNullOrWhiteSpace(configured))
            throw new InvalidOperationException($"License signing key '{keyId}' is not trusted by this PROGNODE Core build.");

        var pem = ResolvePem(configured);
        var (spkiDer, rawPublicKey) = ParseEd25519SpkiPem(pem);

        if (options.TrustedPublicKeyFingerprintsSha256.TryGetValue(keyId, out var expectedFingerprint) &&
            !string.IsNullOrWhiteSpace(expectedFingerprint))
        {
            var actual = Convert.ToHexString(SHA256.HashData(spkiDer)).ToLowerInvariant();
            var expected = expectedFingerprint.Trim().Replace(":", string.Empty, StringComparison.Ordinal).ToLowerInvariant();
            if (!CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(actual), Encoding.ASCII.GetBytes(expected)))
                throw new InvalidOperationException($"Trusted public key fingerprint mismatch for keyId '{keyId}'.");
        }

        try
        {
            return PublicKey.Import(Ed25519, rawPublicKey, KeyBlobFormat.RawPublicKey);
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException or NotSupportedException)
        {
            throw new InvalidOperationException("Configured Ed25519 public key is invalid.", ex);
        }
    }

    private string ResolvePem(string configured)
    {
        var value = configured.Trim();
        if (value.Contains("-----BEGIN PUBLIC KEY-----", StringComparison.Ordinal))
            return value;

        var path = value.StartsWith("file:", StringComparison.OrdinalIgnoreCase) ? value[5..] : value;
        if (!Path.IsPathRooted(path))
            path = Path.GetFullPath(Path.Combine(options.BaseDirectory, path));
        if (!File.Exists(path))
            throw new InvalidOperationException($"Trusted public key PEM file was not found: {path}");

        return File.ReadAllText(path, Encoding.ASCII);
    }

    private static (byte[] SpkiDer, byte[] RawPublicKey) ParseEd25519SpkiPem(string pem)
    {
        const string begin = "-----BEGIN PUBLIC KEY-----";
        const string end = "-----END PUBLIC KEY-----";

        var start = pem.IndexOf(begin, StringComparison.Ordinal);
        var finish = pem.IndexOf(end, StringComparison.Ordinal);
        if (start < 0 || finish <= start)
            throw new InvalidOperationException("Trusted Ed25519 public key must be an SPKI PEM PUBLIC KEY.");

        start += begin.Length;
        var base64 = new string(pem[start..finish].Where(c => !char.IsWhiteSpace(c)).ToArray());
        byte[] der;
        try
        {
            der = Convert.FromBase64String(base64);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("Trusted public key PEM contains invalid Base64.", ex);
        }

        // RFC 8410 Ed25519 SubjectPublicKeyInfo DER is exactly:
        // 30 2A                         SEQUENCE
        //   30 05                       SEQUENCE
        //     06 03 2B 65 70            OID 1.3.101.112
        //   03 21 00                    BIT STRING, 0 unused bits
        //     <32-byte raw public key>
        ReadOnlySpan<byte> expectedPrefix =
        [
            0x30, 0x2A, 0x30, 0x05, 0x06, 0x03, 0x2B, 0x65, 0x70, 0x03, 0x21, 0x00
        ];
        if (der.Length != expectedPrefix.Length + 32 || !der.AsSpan(0, expectedPrefix.Length).SequenceEqual(expectedPrefix))
            throw new InvalidOperationException($"Trusted public key is not an Ed25519 SPKI key with OID {Ed25519SpkiOid}.");

        return (der, der.AsSpan(expectedPrefix.Length, 32).ToArray());
    }

    private static LicensePayloadV2 ParseVerifiedPayload(JsonElement payload)
    {
        var schema = RequiredExactString(payload, "schema");
        if (!string.Equals(schema, ExpectedPayloadSchema, StringComparison.Ordinal))
            throw new InvalidOperationException($"Unsupported signed license payload schema '{schema}'.");

        var issuer = RequiredExactString(payload, "issuer");
        if (!string.Equals(issuer, "PROGNODE", StringComparison.Ordinal))
            throw new InvalidOperationException($"Unsupported license issuer '{issuer}'.");

        var licenseId = RequiredExactString(payload, "licenseId");
        var licenseKey = RequiredExactString(payload, "licenseKey");
        var licenseRevision = RequiredPositiveInt(payload, "licenseRevision");
        var organizationId = RequiredExactString(payload, "organizationId");
        var organizationName = RequiredExactString(payload, "organizationName");
        var issuedAtUtc = RequiredUtcTimestamp(payload, "issuedAtUtc");

        var subscriptionElement = RequiredObject(payload, "subscription");

        // Commercial checkout licenses remain fixed to the V1.8 capacity tiers.
        // Control Center Manual/Internal licenses may carry a signed custom positive maxTags.
        // Accept the marker at payload level (canonical V1.8 contract) and also under subscription
        // for forward/backward compatibility between Control Center revisions.
        var licenseType = OptionalExactString(payload, "licenseType")
            ?? OptionalExactString(subscriptionElement, "licenseType")
            ?? "CUSTOMER";
        var paymentRequired = OptionalBool(payload, "paymentRequired")
            ?? OptionalBool(subscriptionElement, "paymentRequired")
            ?? true;
        var isTrial = string.Equals(licenseType, "TRIAL", StringComparison.OrdinalIgnoreCase);
        var customMaxTagsAllowed =
            isTrial || string.Equals(licenseType, "INTERNAL_QA", StringComparison.OrdinalIgnoreCase) ||
            !paymentRequired;
        var product = RequiredExactString(subscriptionElement, "product");
        if (product is not ("ALARM_MONITORING" or "HISTORIAN" or "ALARM_HISTORIAN"))
            throw new InvalidOperationException($"Unsupported subscription.product '{product}'.");

        var billingPeriod = RequiredExactString(subscriptionElement, "billingPeriod");
        var pricingVersion = OptionalExactString(subscriptionElement, "pricingVersion");
        var legacyV17 = string.IsNullOrWhiteSpace(pricingVersion) ||
            string.Equals(pricingVersion, "LEGACY_V1_7", StringComparison.Ordinal);
        pricingVersion = legacyV17 ? "LEGACY_V1_7" : pricingVersion!;

        // Web V1.8 sales are MONTHLY or YEARLY. SIX_MONTHS is readable only for
        // pre-V1.8 signed licenses so existing customers are not broken.
        if (billingPeriod is not ("MONTHLY" or "YEARLY") && !(legacyV17 && billingPeriod == "SIX_MONTHS") &&
            !(isTrial && billingPeriod == "TRIAL"))
            throw new InvalidOperationException($"Unsupported subscription.billingPeriod '{billingPeriod}'.");

        var validFromUtc = OptionalUtcTimestamp(subscriptionElement, "validFromUtc") ?? issuedAtUtc;
        var expiresAtUtc = ReadExpiresAtUtc(subscriptionElement, legacyV17);
        var graceUntilUtc = ReadGraceUntilUtc(subscriptionElement, expiresAtUtc, legacyV17);
        if (expiresAtUtc <= validFromUtc)
            throw new InvalidOperationException("License subscription.expiresAtUtc must be later than validFromUtc.");
        // Strict 14-day, zero-grace trial; a signed commercial license retains its own grace.
        if (isTrial && (billingPeriod != "TRIAL" || product != "ALARM_HISTORIAN" ||
            paymentRequired || expiresAtUtc != validFromUtc.AddDays(14) ||
            graceUntilUtc != expiresAtUtc || legacyV17))
            throw new InvalidOperationException("TRIAL requires both modules, exactly 14 days and no grace.");

        var subscriptionTags = ReadTagCapacity(subscriptionElement, "subscription.maxTags", legacyV17,
            legacyFallbackUnlimited: true,
            allowCustomPositiveInteger: customMaxTagsAllowed);

        var accountElement = RequiredObject(payload, "account");
        var userId = RequiredExactString(accountElement, "userId");
        // The contract normalizes only the entered email (Trim + lowercase) and compares it
        // to the signed payload.account.email value. Preserve the signed value exactly here.
        var email = RequiredExactString(accountElement, "email");
        if (email.Length == 0)
            throw new InvalidOperationException("Signed account.email is empty.");
        var displayName = RequiredExactString(accountElement, "displayName");
        var portalRole = RequiredExactString(accountElement, "portalRole");
        if (portalRole is not ("OWNER" or "ORGANIZATION_ADMIN" or "BILLING_ADMIN" or "MEMBER"))
            throw new InvalidOperationException($"Unsupported account.portalRole '{portalRole}'.");

        var offlineAuth = ParseOfflineAuth(RequiredObject(payload, "offlineAuth"));
        // Annual-only commercial Remote Access policy for NEW licenses. Older signed
        // documents remain verifiable: they cannot be silently changed/re-signed.
        var annualRemoteRequired = !legacyV17 && !customMaxTagsAllowed &&
            issuedAtUtc >= new DateTimeOffset(2026,9,26,19,0,0,TimeSpan.Zero);
        var remoteAccessAddon = ParseRemoteAccessAddon(payload, legacyV17, annualRemoteRequired);
        var entitlements = ParseEntitlements(
            RequiredObject(payload, "entitlements"),
            legacyV17,
            expiresAtUtc,
            remoteAccessAddon,
            customMaxTagsAllowed);

        if (subscriptionTags.Unlimited != entitlements.UnlimitedTags ||
            subscriptionTags.MaxTags != entitlements.MaxTags)
            throw new InvalidOperationException("Signed subscription.maxTags and entitlements.maxTags must match.");

        return new LicensePayloadV2(
            schema,
            issuer,
            licenseId,
            licenseKey,
            licenseRevision,
            organizationId,
            organizationName,
            new LicenseSubscriptionV2(
                product,
                billingPeriod,
                validFromUtc,
                expiresAtUtc,
                graceUntilUtc,
                subscriptionTags.MaxTags,
                subscriptionTags.Unlimited,
                pricingVersion),
            new LicenseAccountV2(userId, email, displayName, portalRole),
            offlineAuth,
            entitlements,
            issuedAtUtc,
            licenseType,
            paymentRequired);
    }

    private static OfflineAuthClaimV1 ParseOfflineAuth(JsonElement element)
    {
        var version = RequiredInt(element, "version");
        var algorithm = RequiredExactString(element, "algorithm");
        var argon2Version = RequiredInt(element, "argon2Version");
        var memoryCost = RequiredInt(element, "memoryCost");
        var iterations = RequiredInt(element, "iterations");
        var parallelism = RequiredInt(element, "parallelism");
        var hashLength = RequiredInt(element, "hashLength");
        var encoding = RequiredExactString(element, "encoding");
        var credentialRevision = RequiredPositiveInt(element, "credentialRevision");

        if (version != 1) throw new InvalidOperationException($"Unsupported offlineAuth.version '{version}'.");
        if (!string.Equals(algorithm, "ARGON2ID", StringComparison.Ordinal)) throw new InvalidOperationException($"Unsupported offlineAuth.algorithm '{algorithm}'.");
        if (argon2Version != 19) throw new InvalidOperationException($"Unsupported offlineAuth.argon2Version '{argon2Version}'.");
        if (memoryCost != 65536) throw new InvalidOperationException($"Unsupported offlineAuth.memoryCost '{memoryCost}'.");
        if (iterations != 3) throw new InvalidOperationException($"Unsupported offlineAuth.iterations '{iterations}'.");
        if (parallelism != 1) throw new InvalidOperationException($"Unsupported offlineAuth.parallelism '{parallelism}'.");
        if (hashLength != 32) throw new InvalidOperationException($"Unsupported offlineAuth.hashLength '{hashLength}'.");
        if (!string.Equals(encoding, "BASE64URL_NOPAD", StringComparison.Ordinal)) throw new InvalidOperationException($"Unsupported offlineAuth.encoding '{encoding}'.");

        var salt = Base64UrlNoPadding.Decode(RequiredExactString(element, "salt"), "offlineAuth.salt");
        var verifier = Base64UrlNoPadding.Decode(RequiredExactString(element, "verifier"), "offlineAuth.verifier");
        if (salt.Length != 16) throw new InvalidOperationException("offlineAuth.salt must decode to exactly 16 bytes.");
        if (verifier.Length != 32) throw new InvalidOperationException("offlineAuth.verifier must decode to exactly 32 bytes.");

        return new OfflineAuthClaimV1(version, algorithm, argon2Version, salt, verifier, memoryCost, iterations,
            parallelism, hashLength, encoding, credentialRevision);
    }

    private readonly record struct ParsedTagCapacity(int? MaxTags, bool Unlimited);

    private readonly record struct ParsedRemoteAccessAddon(
        bool Present,
        bool Enabled,
        int? MaxClients,
        bool UnlimitedClients,
        DateTimeOffset? ExpiresAtUtc);

    private static ParsedRemoteAccessAddon ParseRemoteAccessAddon(JsonElement payload, bool legacyV17, bool annualRequired)
    {
        if (!payload.TryGetProperty("addons", out var addonsElement) || addonsElement.ValueKind == JsonValueKind.Null)
            return new ParsedRemoteAccessAddon(false, false, null, false, null);

        if (addonsElement.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("License field 'payload.addons' must be an object.");

        if (!addonsElement.TryGetProperty("remoteAccess", out var remoteElement) || remoteElement.ValueKind == JsonValueKind.Null)
            return new ParsedRemoteAccessAddon(false, false, null, false, null);

        if (remoteElement.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("License field 'payload.addons.remoteAccess' must be an object.");

        var enabled = RequiredBool(remoteElement, "enabled");
        var unlimited = OptionalBool(remoteElement, "unlimitedClients") ?? false;
        if (unlimited && !legacyV17)
            throw new InvalidOperationException("Web V1.8 Remote Access does not support unlimitedClients; maxClients must be 5, 10 or 25.");

        int? maxClients = null;
        if (remoteElement.TryGetProperty("maxClients", out var maxElement) && maxElement.ValueKind != JsonValueKind.Null)
        {
            if (maxElement.ValueKind == JsonValueKind.String &&
                string.Equals(maxElement.GetString(), "unlimited", StringComparison.OrdinalIgnoreCase))
            {
                if (!legacyV17)
                    throw new InvalidOperationException("Web V1.8 Remote Access maxClients must be 5, 10 or 25; 'unlimited' is legacy-only.");
                unlimited = true;
            }
            else if (maxElement.ValueKind == JsonValueKind.Number && maxElement.TryGetInt32(out var parsedMax))
            {
                maxClients = parsedMax;
            }
            else
            {
                throw new InvalidOperationException("License field 'payload.addons.remoteAccess.maxClients' must be 5, 10 or 25.");
            }
        }

        if (enabled && !unlimited && maxClients is not (5 or 10 or 25))
            throw new InvalidOperationException("Remote Access add-on maxClients must be 5, 10 or 25.");

        var billingPeriod = OptionalExactString(remoteElement, "billingPeriod");
        if(enabled && annualRequired && billingPeriod != "YEARLY")
            throw new InvalidOperationException("New commercial Remote Access licenses require YEARLY billingPeriod; 5, 10 or 25 clients.");
        if (billingPeriod is not null &&
            billingPeriod is not ("MONTHLY" or "YEARLY") &&
            !(legacyV17 && billingPeriod == "SIX_MONTHS"))
            throw new InvalidOperationException($"Unsupported payload.addons.remoteAccess.billingPeriod '{billingPeriod}'.");

        var expiresAtUtc = OptionalUtcTimestamp(remoteElement, "expiresAtUtc");
        if (enabled && !legacyV17 && expiresAtUtc is null)
            throw new InvalidOperationException("Web V1.8 Remote Access add-on must include payload.addons.remoteAccess.expiresAtUtc.");

        return new ParsedRemoteAccessAddon(true, enabled, unlimited ? null : maxClients, unlimited, expiresAtUtc);
    }

    private static LicenseEntitlementClaimsV2 ParseEntitlements(
        JsonElement element,
        bool legacyV17,
        DateTimeOffset baseExpiresAtUtc,
        ParsedRemoteAccessAddon remoteAccessAddon,
        bool customMaxTagsAllowed)
    {
        LicenseRemoteAccessClaimV1? remoteAccess = null;
        if (element.TryGetProperty("remoteAccess", out var remoteElement))
        {
            if (remoteElement.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("License field 'entitlements.remoteAccess' must be an object.");

            var enabled = RequiredBool(remoteElement, "enabled");
            var unlimited = OptionalBool(remoteElement, "unlimitedClients") ?? false;
            if (unlimited && !legacyV17)
                throw new InvalidOperationException("Web V1.8 Remote Access does not support unlimitedClients; maxClients must be 5, 10 or 25.");

            int? maxClients = null;
            if (remoteElement.TryGetProperty("maxClients", out var maxElement) && maxElement.ValueKind != JsonValueKind.Null)
            {
                if (maxElement.ValueKind == JsonValueKind.String &&
                    string.Equals(maxElement.GetString(), "unlimited", StringComparison.OrdinalIgnoreCase))
                {
                    if (!legacyV17)
                        throw new InvalidOperationException("Web V1.8 Remote Access maxClients must be 5, 10 or 25; 'unlimited' is legacy-only.");
                    unlimited = true;
                }
                else if (maxElement.ValueKind == JsonValueKind.Number && maxElement.TryGetInt32(out var parsedMax))
                {
                    maxClients = parsedMax;
                }
                else
                {
                    throw new InvalidOperationException("License field 'entitlements.remoteAccess.maxClients' must be 5, 10 or 25.");
                }
            }

            if (enabled && !unlimited && maxClients is not (5 or 10 or 25))
                throw new InvalidOperationException("Remote Access entitlement maxClients must be 5, 10 or 25.");

            var entitlementExpiry = OptionalUtcTimestamp(remoteElement, "expiresAtUtc");

            if (remoteAccessAddon.Present)
            {
                if (enabled != remoteAccessAddon.Enabled)
                    throw new InvalidOperationException("Signed entitlements.remoteAccess.enabled and addons.remoteAccess.enabled must match.");

                if (enabled && (unlimited != remoteAccessAddon.UnlimitedClients ||
                                (!unlimited && maxClients != remoteAccessAddon.MaxClients)))
                    throw new InvalidOperationException("Signed entitlements.remoteAccess.maxClients and addons.remoteAccess.maxClients must match.");

                if (entitlementExpiry is not null && remoteAccessAddon.ExpiresAtUtc is not null &&
                    entitlementExpiry.Value != remoteAccessAddon.ExpiresAtUtc.Value)
                    throw new InvalidOperationException("Signed Remote Access expiry values in entitlements and addons must match when both are present.");
            }

            // Web V1.8 currently signs Remote Access commercial lifecycle under
            // payload.addons.remoteAccess. Older RC6-shaped files may still carry the
            // expiry directly under entitlements.remoteAccess, so accept that as a
            // compatibility fallback. The signed bytes are never rewritten.
            var remoteExpiresAtUtc = remoteAccessAddon.ExpiresAtUtc ?? entitlementExpiry;
            if (enabled && remoteExpiresAtUtc is null)
            {
                if (!legacyV17)
                    throw new InvalidOperationException("Web V1.8 Remote Access must include a signed expiresAtUtc in payload.addons.remoteAccess or entitlements.remoteAccess.");

                // Legacy signed licenses did not carry a separate add-on expiry.
                // They remain readable, but Remote Access receives no base-license grace.
                remoteExpiresAtUtc = baseExpiresAtUtc;
            }

            remoteAccess = new LicenseRemoteAccessClaimV1(enabled, unlimited ? null : maxClients, unlimited, remoteExpiresAtUtc);
        }
        else if (remoteAccessAddon.Present && remoteAccessAddon.Enabled)
        {
            throw new InvalidOperationException("Signed payload.addons.remoteAccess is enabled but entitlements.remoteAccess is missing.");
        }

        var tagCapacity = ReadTagCapacity(
            element,
            "entitlements.maxTags",
            legacyV17,
            legacyFallbackUnlimited: string.Equals(
                OptionalExactString(element, "tags"),
                "unlimited",
                StringComparison.OrdinalIgnoreCase),
            allowCustomPositiveInteger: customMaxTagsAllowed);

        return new LicenseEntitlementClaimsV2(
            RequiredBool(element, "alarmMonitoring"),
            RequiredBool(element, "historian"),
            OptionalExactString(element, "devices"),
            OptionalExactString(element, "tags"),
            OptionalExactString(element, "alarms"),
            OptionalExactString(element, "historianSamples"),
            tagCapacity.MaxTags,
            tagCapacity.Unlimited,
            remoteAccess);
    }

    private static ParsedTagCapacity ReadTagCapacity(
        JsonElement element,
        string fieldName,
        bool legacyV17,
        bool legacyFallbackUnlimited,
        bool allowCustomPositiveInteger)
    {
        const string propertyName = "maxTags";
        if (!element.TryGetProperty(propertyName, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            if (legacyV17 && legacyFallbackUnlimited)
                return new ParsedTagCapacity(null, true);
            throw new InvalidOperationException($"Signed license field '{fieldName}' is required by Web V1.8.");
        }

        if (value.ValueKind == JsonValueKind.String &&
            string.Equals(value.GetString(), "unlimited", StringComparison.OrdinalIgnoreCase))
        {
            if (legacyV17)
                return new ParsedTagCapacity(null, true);
            throw new InvalidOperationException($"Signed license field '{fieldName}' must be numeric in Web V1.8; 'unlimited' is legacy-only.");
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed))
        {
            if (allowCustomPositiveInteger)
            {
                if (parsed is >= 1 and <= 1_000_000)
                    return new ParsedTagCapacity(parsed, false);

                throw new InvalidOperationException($"Signed Manual/Internal license field '{fieldName}' must be between 1 and 1,000,000.");
            }

            if (parsed is 100 or 250 or 500)
                return new ParsedTagCapacity(parsed, false);
        }

        throw new InvalidOperationException($"Signed Commercial V1.8 license field '{fieldName}' must be 100, 250 or 500.");
    }

    private static DateTimeOffset ReadExpiresAtUtc(JsonElement subscription, bool legacyV17)
    {
        var modern = OptionalUtcTimestamp(subscription, "expiresAtUtc");
        var oldRc5Alias = OptionalUtcTimestamp(subscription, "validUntilUtc");

        if (modern is null && oldRc5Alias is null)
            throw new InvalidOperationException("License subscription must include expiresAtUtc.");

        if (!legacyV17 && modern is null)
            throw new InvalidOperationException("Web V1.8 licenses must include signed subscription.expiresAtUtc.");

        if (modern is not null && oldRc5Alias is not null && modern.Value != oldRc5Alias.Value)
            throw new InvalidOperationException("License subscription expiresAtUtc and validUntilUtc aliases do not match.");

        return modern ?? oldRc5Alias!.Value;
    }

    private static DateTimeOffset ReadGraceUntilUtc(
        JsonElement subscription,
        DateTimeOffset expiresAtUtc,
        bool legacyV17)
    {
        var signedGrace = OptionalUtcTimestamp(subscription, "graceUntilUtc");
        var expected = expiresAtUtc.AddDays(LicenseEntitlementService.GracePeriodDays);

        if (signedGrace is null)
        {
            if (legacyV17)
                return expected;
            throw new InvalidOperationException("Web V1.8 licenses must include signed subscription.graceUntilUtc.");
        }

        if (signedGrace.Value != expected)
            throw new InvalidOperationException("Web V1.8 graceUntilUtc must be exactly 7 days after expiresAtUtc.");

        return signedGrace.Value;
    }

    private static void ValidateNoDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name))
                    throw new InvalidOperationException($"Duplicate JSON property '{property.Name}' is not allowed in a license.");
                ValidateNoDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
                ValidateNoDuplicateProperties(item);
        }
    }

    private static void ValidateNoUnexpectedEnvelopeFields(JsonElement root)
    {
        foreach (var property in root.EnumerateObject())
            if (!AllowedEnvelopeFields.Contains(property.Name))
                throw new InvalidOperationException($"Unsupported unsigned envelope field '{property.Name}'.");
    }

    private static JsonElement RequiredObject(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException($"Signed license field '{name}' must be an object.");
        return value;
    }

    private static string RequiredExactString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException($"License field '{name}' is required and must be a string.");
        return value.GetString() ?? string.Empty;
    }

    private static string? OptionalExactString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind != JsonValueKind.String)
            throw new InvalidOperationException($"License field '{name}' must be a string when present.");
        return value.GetString();
    }

    private static int RequiredInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number))
            throw new InvalidOperationException($"License field '{name}' is required and must be an integer.");
        return number;
    }

    private static int? OptionalInt(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number))
            throw new InvalidOperationException($"License field '{name}' must be an integer when present.");
        return number;
    }

    private static int RequiredPositiveInt(JsonElement element, string name)
    {
        var value = RequiredInt(element, name);
        if (value < 1)
            throw new InvalidOperationException($"License field '{name}' must be >= 1.");
        return value;
    }

    private static bool? OptionalBool(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
            return null;
        if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidOperationException($"License field '{name}' must be boolean when present.");
        return value.GetBoolean();
    }

    private static bool RequiredBool(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var value) || value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            throw new InvalidOperationException($"License field '{name}' is required and must be boolean.");
        return value.GetBoolean();
    }

    private static DateTimeOffset RequiredUtcTimestamp(JsonElement element, string name)
    {
        var text = RequiredExactString(element, name);
        return ParseUtcTimestamp(text, name);
    }

    private static DateTimeOffset? OptionalUtcTimestamp(JsonElement element, string name)
    {
        var text = OptionalExactString(element, name);
        return text is null ? null : ParseUtcTimestamp(text, name);
    }

    private static DateTimeOffset ParseUtcTimestamp(string text, string name)
    {
        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp))
            throw new InvalidOperationException($"License field '{name}' is not a valid timestamp.");
        return timestamp.ToUniversalTime();
    }
}
