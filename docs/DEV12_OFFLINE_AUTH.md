# PROGNODE V0.7.2 DEV12 — Signed Offline Authentication

DEV12 replaces the DEV11 PBKDF2/unsigned local-account verifier path with the signed offline contract.

## Trust order

Core now performs the trust gates in this order:

1. Parse `.pgnlicense` as an untrusted envelope.
2. Resolve `keyId` only against Core's locally configured trusted Ed25519 public-key ring.
3. Verify the Ed25519 signature over the signed payload bytes.
4. Only after signature success, parse expiry/status and product entitlements.
5. Normalize entered email with `Trim().ToLowerInvariant()` and compare it to signed `account.email`.
6. Derive the entered password with Argon2id using the signed `offlineAuth.salt` and signed parameters.
7. Compare the derived bytes to signed `offlineAuth.verifier` using `CryptographicOperations.FixedTimeEquals`.
8. Open an in-memory local session. No cloud request is used by this path.

## Roles

`account.portalRole` / `portalRole` is account metadata only. DEV12 does not map `OWNER`, `ADMIN`, or any cloud role to the industrial runtime roles:

- Administrator
- Engineer
- Operator
- Viewer
- Service

The industrial role is represented separately and remains unset until the local runtime-role milestone is implemented.

## Trusted Ed25519 public key

Core intentionally ships with no invented/fallback production public key. Add the real public key produced by the PROGNODE web-license signing system under:

```json
{
  "Prognode": {
    "Licensing": {
      "TrustedPublicKeys": {
        "<keyId>": "<32-byte Ed25519 public key as Base64Url or Base64>"
      }
    }
  }
}
```

For local License Test it can also be supplied through standard ASP.NET Core configuration, for example:

`Prognode__Licensing__TrustedPublicKeys__<keyId>=<public-key>`

Only the public key belongs in Core. The private signing key must remain server-side in the web/Vercel licensing system.

## Signed payload compatibility

The verifier accepts a signed JSON `payload` represented as either:

- Base64Url/Base64 encoded JSON bytes, or
- a JSON object.

For an encoded payload, verification first uses the decoded payload bytes. For an object payload, verification can use the raw object bytes or deterministic sorted-property JSON bytes. A signed top-level payload is also supported through deterministic JSON excluding envelope signature metadata.

Regardless of representation, `account`, `offlineAuth`, expiry/status, and entitlements are not trusted until Ed25519 verification succeeds.

## OfflineAuth fields consumed by Core

Required signed values:

- `account.email`
- `offlineAuth.salt`
- `offlineAuth.verifier`
- Argon2id `memoryCost` / `memoryCostKiB`
- Argon2id `iterations`
- Argon2id `parallelism`
- license expiry/status
- product/module entitlement

Salt/verifier/signature/public-key binary fields accept Base64Url or Base64.

## Development fallback

Normal Development profile still supports the existing DEV fallback. `PROGNODE Server — License Test` keeps `Prognode__AllowDevelopmentFallback=false`, so unsigned/missing/invalid production licenses fail closed.
