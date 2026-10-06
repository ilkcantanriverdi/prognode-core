# PROGNODE V0.7.2 DEV12.4 — Real production public key integration

DEV12.4 integrates the actual production Ed25519 public verification key for:

- keyId: `prognode-license-2026-01`
- algorithm: `Ed25519`
- SPKI SHA-256: `3e902011b79009ec3b1e66fb7fbfddb24567eb438479802c36cb8d4db6c3e402`

The production trust root is compiled into `Prognode.Licensing` via `PrognodeTrustedLicenseKeys`.
It is no longer replaceable through `appsettings.json`.
The PEM is also retained under `src/Prognode.Host/trusted-keys/` as an audit/reference copy and is public material only.

## Real uploaded artifact verification

Verified against the uploaded real artifacts:

- `PGN-7652F-33BCA-290B7-585FE.pgnlicense`
- `prognode-license-ed25519-public.pem`

Observed results:

- SPKI fingerprint matches frozen contract: PASS
- Ed25519 signature verification: PASS
- signature length = 64 bytes: PASS
- offlineAuth salt length = 16 bytes: PASS
- offlineAuth verifier length = 32 bytes: PASS
- one-bit signature tamper rejected: PASS
- signed payload mutation rejected: PASS
- intentionally wrong password derives a non-matching Argon2id verifier: PASS
- signed ALARM_HISTORIAN entitlement claims are internally consistent: PASS

Artifact hashes used for this check:

- license SHA-256: `7e96c914014bbcbb911b7e4b464320532dbab3e8a7f6f16343cb8e0403dfa97d`
- PEM file SHA-256: `7471e310bb5551fbb982223b5f7ec3e6125f697a5401f5dad6f5c25a70fb5b31`

## Runtime test boundary

The test environment used to prepare this package does not contain the .NET SDK, so a real `dotnet build` / ASP.NET Core login request could not be executed here.
The user's real plaintext password was neither requested nor available, therefore the real-license `correct password accepted` case must be exercised locally through the Core UI.

Use `PROGNODE Server — License Test` so `AllowDevelopmentFallback=false` while testing the commercial license path.
