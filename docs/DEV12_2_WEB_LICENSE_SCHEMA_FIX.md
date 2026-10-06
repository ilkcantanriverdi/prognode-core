# PROGNODE V0.7.2 DEV12.2 — Web license schema alignment

DEV12.2 aligns Core with the real `.pgnlicense` file emitted by the web license service on 2026-09-16.

Observed production envelope:

- `formatVersion`: `2.0`
- `keyId`: `prognode-license-2026-01`
- `signatureAlgorithm`: `Ed25519`
- `canonicalization`: `PGN_CANONICAL_JSON_1`
- payload schema: `prognode.license.payload/v2`
- product and expiry are under `payload.subscription`
- account identity is under `payload.account`
- offline verifier is under `payload.offlineAuth`
- `entitlements.alarmMonitoring` is the Alarm entitlement flag

Core changes:

1. Reads product from `payload.subscription.product` (while preserving legacy DEV aliases).
2. Reads expiry from `payload.subscription.expiresAtUtc` and requires expiry for formatVersion 2.0.
3. Recognizes `entitlements.alarmMonitoring`.
4. Validates Ed25519 / `PGN_CANONICAL_JSON_1` envelope markers when present.
5. Validates the v2 payload schema marker when present.
6. Canonical JSON writer uses relaxed UTF-8 escaping so names such as `İlkcan Tanrıverdi` are not changed into a different signed byte representation.
7. Validates Argon2 version 19, declared hash length, and supported binary encodings when supplied.
8. Keeps email normalization, Argon2id verification, constant-time compare, portal-role separation, and no-cloud local session behavior from DEV12.

## Remaining trust input

The signed license intentionally does **not** contain its own trusted public key. Core must be provisioned with the real 32-byte Ed25519 public key for keyId `prognode-license-2026-01` under:

`Prognode:Licensing:TrustedPublicKeys:prognode-license-2026-01`

Leaving the value empty is fail-closed and import will report that the signing key is not trusted. Do not put the private key in Core.
