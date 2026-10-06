# PROGNODE V0.7.2 RC6.3.1 — License Session / Revocation / Internal QA

RC6.3.1 is a focused licensing/session patch on top of RC6.3. All Developer Test Bench capabilities from RC6.3 remain unchanged.

## 1. Control Center revoke becomes effective in Core

The signed `.pgnlicense` remains the offline entitlement source, but a successful Cloud response for the exact same `licenseId` can now persist a server-side revocation:

```json
{
  "licenseId": "...",
  "licenseStatus": "REVOKED",
  "revoked": true,
  "revokedAtUtc": "2026-09-20T12:00:00Z"
}
```

Core behavior after the first successful revoke sync:

- effective license status becomes `REVOKED`
- `IsValid=false`
- Alarm engine stops paid process-alarm evaluation
- Historian stops new paid recording
- configuration mutation is disabled
- Remote Access preflight becomes not entitled/invalid
- existing configuration/history remain readable after local sign-in
- persisted revoke survives Core restart and Internet loss
- re-importing the same revoked signed file does **not** clear the revoke cache
- importing a different `licenseId` starts a fresh Cloud activation
- a later successful ACTIVE response for the same license clears the revoke state

Cloud unavailability by itself never invents a revocation.

Heartbeat default is now 60 seconds. The browser refreshes effective license state every 10 seconds.

## 2. Signed-out license privacy

While signed out:

- License page hides identity, plan, expiry, remaining days, capacity and module entitlement details
- `/api/license` returns only a redacted `licenseInstalled / SIGN_IN_REQUIRED` summary
- `/api/license/usage` returns a redacted capacity response
- stale license details are cleared from the browser DOM immediately on sign-out

Import/replace remains available on the License page.

## 3. Import UX / assigned user

After successful import:

- Core returns only the signed assigned-user hint required for local login
- UI pulls `account.displayName` and `account.email` from the imported signed license
- email is prefilled in the sign-in field
- the access-popup import step disappears after successful import
- the dedicated License page keeps Import/Replace available

## 4. Manual/Internal custom maxTags

Commercial V1.8 behavior is unchanged:

```text
CUSTOMER + paymentRequired=true
maxTags = 100 | 250 | 500
```

For a signed Manual/Internal license, custom positive capacity is accepted when either:

```text
licenseType = INTERNAL_QA
```

or:

```text
paymentRequired = false
```

The markers may be signed at payload level; RC6.3.1 also accepts them under `subscription` for compatibility between Control Center revisions.

Validation:

```text
1 <= subscription.maxTags <= 1,000,000
1 <= entitlements.maxTags <= 1,000,000
subscription.maxTags == entitlements.maxTags
```

Runtime capacity always comes from the signed effective entitlement. `unlimited` remains legacy-only.

## 5. Web contract required for revoke

Control Center/Web must return a successful HTTP response from `/api/core/activate` or `/api/core/heartbeat` when a license is revoked. Do not turn revoke into only `401/403`, because Core must be able to distinguish a revoked commercial state from an authentication/route failure.

Minimum response fields:

```json
{
  "licenseId": "...",
  "licenseStatus": "REVOKED",
  "revoked": true,
  "revokedAtUtc": "..."
}
```

The response may be flat or wrapped under `data`, `license`, or `installation`; RC6.3.1 tolerates these common wrappers.
