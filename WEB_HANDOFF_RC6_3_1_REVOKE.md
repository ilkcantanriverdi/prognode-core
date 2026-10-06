# PROGNODE Web / Control Center Handoff — RC6.3.1 Revoke Contract

Core RC6.3.1 now persists and enforces server-side license revocation. Web must expose that state through the normal Core activation/heartbeat contract.

## Required behavior

When Control Center revokes a license, `/api/core/activate` and `/api/core/heartbeat` must still return a successful JSON response to the authenticated/identified Core. Do not represent a commercial revoke only as HTTP 401/403.

Minimum payload:

```json
{
  "licenseId": "<same signed license id>",
  "licenseStatus": "REVOKED",
  "revoked": true,
  "revokedAtUtc": "2026-09-20T12:00:00Z"
}
```

Core also accepts common `data`, `license` and `installation` wrappers.

If the license is restored/unrevoked later, heartbeat should return the same `licenseId` with an ACTIVE/non-revoked state:

```json
{
  "licenseId": "...",
  "licenseStatus": "ACTIVE",
  "revoked": false
}
```

The signed `.pgnlicense` file itself is not rewritten. Server revocation is an authoritative server-side state layered on top of the offline signed entitlement after the first successful sync.
