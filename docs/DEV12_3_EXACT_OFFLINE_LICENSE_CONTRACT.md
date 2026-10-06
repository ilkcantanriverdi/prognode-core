# PROGNODE V0.7.2 DEV12.3 — Exact Offline License/Auth Contract

DEV12.3 removes the compatibility guesses from DEV12/DEV12.2 and implements the frozen Cloud/Web contract exactly.

## Trust chain

1. Parse the JSON envelope only.
2. Require exactly `formatVersion=2.0`, `signatureAlgorithm=Ed25519`, `canonicalization=PGN_CANONICAL_JSON_1` and a trusted `keyId`.
3. Build the signed object from `formatVersion`, `keyId`, `signatureAlgorithm`, `canonicalization`, `payload`; `signature` is excluded.
4. Canonicalize recursively with ascending ordinal property names, unchanged array ordering, compact UTF-8 JSON, no Unicode normalization, and `-0 -> 0`.
5. Decode the signature as Base64Url without padding and verify Ed25519.
6. Only after signature verification parse account, subscription, offlineAuth and entitlement claims.

The trusted key is an Ed25519 SubjectPublicKeyInfo PEM (`PUBLIC KEY`) with OID `1.3.101.112`. The configured SHA-256 fingerprint is calculated over the complete DER SPKI bytes.

Expected production key:

- keyId: `prognode-license-2026-01`
- SPKI SHA-256: `3e902011b79009ec3b1e66fb7fbfddb24567eb438479802c36cb8d4db6c3e402`
- file: `src/Prognode.Host/trusted-keys/prognode-license-ed25519-public.pem`

The private key never belongs in this repository or on a customer Core machine.

## Offline auth

`OfflineCredentialVerifier` enforces exactly:

- Argon2id version 19
- UTF-8 password bytes, no trim and no Unicode normalization
- 16-byte Base64Url-no-padding salt
- memoryCost 65536 KiB
- iterations 3
- parallelism 1
- hashLength 32
- 32-byte verifier
- constant-time comparison with `CryptographicOperations.FixedTimeEquals`

Email is normalized only as `Trim + lowercase`.

## Entitlements

Supported signed products:

- `ALARM_MONITORING`
- `HISTORIAN`
- `ALARM_HISTORIAN`

The signed module booleans and `alarms` / `historianSamples` claims must agree with the product. Devices and tags must be `unlimited` for this v2 contract.

## Roles

`account.portalRole` is account metadata only. `OWNER`, `ORGANIZATION_ADMIN`, `BILLING_ADMIN` and `MEMBER` are never mapped to `Administrator`, `Engineer`, `Operator`, `Viewer` or `Service`.

## Revision behavior

For the same `licenseId`, importing a lower `licenseRevision` is rejected. At the same `licenseRevision`, importing a lower `credentialRevision` is rejected. A license refresh invalidates an existing local session if either revision changes.

## Offline boundary

`LocalAccessSessionService.Login()` has no HTTP dependency. Cloud activation is represented by `CloudActivationClient` as a separate best-effort boundary and is not called by local login, license verification, PLC polling, alarms, historian or trends.
