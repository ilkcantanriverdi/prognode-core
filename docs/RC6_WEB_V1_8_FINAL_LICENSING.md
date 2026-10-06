# PROGNODE Core RC6 — Web V1.8 FINAL Licensing Contract

> **Superseded by RC6.1:** Web V1.8 actually signs Remote Access expiry under `payload.addons.remoteAccess.expiresAtUtc`. See `RC6_1_WEB_V1_8_ACTUAL_LICENSE_CONTRACT.md`.

This document supersedes the provisional RC5 commercial-license lifecycle. Web/Cloud V1.8 is the authoritative commercial contract for Core V1 release preparation.

## Signed `.pgnlicense` V2 fields

Core consumes the following signed fields after Ed25519 verification:

```text
subscription.product
subscription.billingPeriod
subscription.expiresAtUtc
subscription.graceUntilUtc
subscription.maxTags
subscription.pricingVersion

entitlements.maxTags
entitlements.remoteAccess.enabled
entitlements.remoteAccess.maxClients
addons.remoteAccess.expiresAtUtc
```

`subscription.maxTags` and `entitlements.maxTags` must describe the same capacity. New Web V1.8 files must carry both. Current V1.8 capacity values are `100`, `250` or `500`. `unlimited` is legacy-only.

Pre-V1.8 signed licenses without `pricingVersion` are normalized locally as:

```text
pricingVersion = LEGACY_V1_7
maxTags = unlimited
graceUntilUtc = expiresAtUtc + 7 days
```

No signed bytes are rewritten. This normalization occurs only after the original signed document has passed Ed25519 verification.

## Lifecycle

The base subscription state is derived only from signed timestamps:

```text
ACTIVE         now < expiresAtUtc - 7 days
EXPIRING_SOON  expiresAtUtc - 7 days <= now < expiresAtUtc
GRACE          expiresAtUtc <= now < graceUntilUtc
EXPIRED        now >= graceUntilUtc
INVALID        signature/schema/contract invalid or not yet valid
```

Web V1.8 `graceUntilUtc` must be exactly seven days after `expiresAtUtc`. Core does not add any hidden tolerance after signed grace.

- ACTIVE: normal paid runtime.
- EXPIRING_SOON: normal paid runtime, yellow warning UI.
- GRACE: paid Alarm/Historian runtime continues, critical/red renewal UI.
- EXPIRED: paid Alarm evaluation and Historian sampling stop. PLC communication/Core hosting remains alive so expiry cannot crash the industrial process host.

Cloud loss or heartbeat loss never changes a still-valid signed offline entitlement.

## Tag capacity

The commercial unit is one unique configured process Tag in the central Tag Registry. Reuse of the same Tag by Alarm, Historian and Trend does not consume additional capacity.

Numeric capacity blocks creation/import that would exceed the signed limit. Existing Tags are never auto-deleted.

If a renewed/downgraded license is below the existing configured count, Core reports:

```text
capacityStatus = OVER_CAPACITY
```

The preserved configuration keeps running while the base license is operational, but new/edit configuration writes are blocked until Tags are reduced or a larger license is imported. Delete/reduction actions remain available.

## Remote Access

LAN access consumes no Remote Access seat.

Current V1.8 Remote capacities are `5`, `10` or `25`; `unlimited` is legacy-only. Remote Access has its own signed `expiresAtUtc`; base-license GRACE does not extend it. After the add-on expiry, remote publish/register/ACK-command polling fails closed while local Core and LAN access continue.

Cloud remains authoritative for server binding, active remote client registration, used seats and revoke.

## Web V1.8 Cloud API compatibility

Core includes a best-effort adapter for:

```text
POST /api/core/activate
POST /api/core/heartbeat
```

Responses consume:

```text
licenseStatus
expiresAtUtc     (legacy alias: expiresAt)
graceUntilUtc    (legacy alias: graceUntil)
maxTags
activationToken
```

Cloud status is cached for diagnostics only. It does not replace or extend the signed `.pgnlicense` offline entitlement. If Cloud is unavailable, local lifecycle derives from the verified signed file.

RC6.2 ships the Cloud license adapter with `https://prognode.io` as the primary production host and `https://account.prognode.io` / `https://control.prognode.io` as trusted route fallbacks. Cloud synchronization remains best-effort and never replaces the verified offline entitlement.
