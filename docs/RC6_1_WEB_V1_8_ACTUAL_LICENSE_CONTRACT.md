# PROGNODE Core RC6.1 — Actual Web V1.8 License Contract

This document supersedes the RC6 assumption that Remote Access expiry must live under `entitlements.remoteAccess.expiresAtUtc`.

## Source of truth

The current Web V1.8 signed license places base subscription data under `payload.subscription`, module/capacity entitlements under `payload.entitlements`, and Remote Access commercial lifecycle under `payload.addons.remoteAccess`.

Example shape:

```json
{
  "payload": {
    "subscription": {
      "product": "ALARM_HISTORIAN",
      "billingPeriod": "MONTHLY",
      "expiresAtUtc": "...",
      "graceUntilUtc": "...",
      "maxTags": 100,
      "pricingVersion": "V1.8"
    },
    "entitlements": {
      "alarmMonitoring": true,
      "historian": true,
      "maxTags": 100,
      "remoteAccess": {
        "enabled": true,
        "maxClients": 5
      }
    },
    "addons": {
      "remoteAccess": {
        "enabled": true,
        "maxClients": 5,
        "billingPeriod": "MONTHLY",
        "expiresAtUtc": "..."
      }
    }
  }
}
```

## Remote Access merge rule

Core builds one verified Remote Access entitlement after Ed25519 verification:

1. `enabled` and `maxClients` come from `entitlements.remoteAccess`.
2. Web V1.8 add-on expiry comes from `addons.remoteAccess.expiresAtUtc`.
3. RC6-style `entitlements.remoteAccess.expiresAtUtc` remains a compatibility fallback.
4. If both expiry locations exist, they must be identical.
5. When an `addons.remoteAccess` object exists, its `enabled` and `maxClients` must match the entitlement object.
6. An enabled V1.8 Remote Access add-on must have a signed expiry.
7. Remote Access expiry is independent from base-license grace.

## Numeric capacities only for V1.8

Current V1.8 commercial capacities are:

```text
Tags:          100 / 250 / 500
Remote clients: 5 / 10 / 25
```

There is no sellable V1.8 `unlimited` tier. Legacy pre-V1.8 licenses may still normalize to unlimited for backwards compatibility only.

The compatibility text claims emitted by Web, such as `tags: "unlimited"`, are not used as the V1.8 Tag limit when a numeric signed `maxTags` is present. The numeric field is authoritative.

## Signature safety

`payload.addons.remoteAccess` is inside the signed payload. Core still verifies the Ed25519 signature over the canonical envelope before parsing or trusting the add-on fields. Supporting the Web field location does not weaken signature verification.

## RC6.1 compile fix

`RemoteAccessService.GetStatus()` now declares:

```csharp
int? remaining = unlimited || max is null ? null : Math.Max(0, max.Value - used);
```

This removes the RC6 `CS0173` conditional-expression compile error while preserving nullable `RemainingClients` behavior.
