# RC5 — V1 Commercial Licensing Contract

> **Historical note:** RC5 was provisional. `docs/RC6_WEB_V1_8_FINAL_LICENSING.md` supersedes its lifecycle/grace/post-expiry rules.


## Products

- `ALARM_MONITORING`: Alarm enabled, Historian disabled.
- `HISTORIAN`: Alarm disabled, Historian enabled.
- `ALARM_HISTORIAN`: Alarm + Historian enabled.
- Trends are a product function over already licensed Tags and are not a separate paid SKU.

## Unique Tag capacity

Commercial capacity is measured only from the central configured Tag Registry.

A single process Tag referenced by Alarm, Historian and Trends consumes one Tag. Alarm definitions, Historian configurations and Trend membership do not consume additional capacity.

New licenses carry:

```json
"entitlements": {
  "alarmMonitoring": true,
  "historian": true,
  "maxTags": 250,
  "remoteAccess": {
    "enabled": true,
    "maxClients": 10
  }
}
```

Standard `maxTags` values are 100, 250 and 500. Enterprise/custom files must still contain an explicit positive numeric `maxTags` (for example 1000, 2500 or a negotiated benchmarked value). Core does not interpret an Enterprise label as unlimited.

Already-issued v2 files with `tags: "unlimited"` remain readable only for migration/backward compatibility.

At capacity, existing Tags and process runtime continue. A new unique Tag is rejected with:

```text
TAG_LIMIT_REACHED
Tag limit reached. 250 / 250
```

## Subscription period and offline lifecycle

V1 sales periods are `MONTHLY` and `YEARLY`. The parser still accepts legacy `SIX_MONTHS` signed files so existing customers are not broken.

Preferred signed subscription shape:

```json
"subscription": {
  "product": "ALARM_HISTORIAN",
  "billingPeriod": "YEARLY",
  "validFromUtc": "2026-10-01T00:00:00Z",
  "validUntilUtc": "2027-10-01T00:00:00Z",
  "gracePeriodDays": 14
}
```

For backward compatibility `expiresAtUtc` is accepted as an alias for `validUntilUtc`. If both are present, they must match.

States:

- `ACTIVE`: now is inside signed validity window.
- `GRACE`: validity ended but grace has not ended; local operation and local configuration login remain available.
- `EXPIRED`: grace also ended.
- `INVALID`: signature/schema is invalid or the license is not yet valid / has impossible validity dates.

The process runtime is not owned by Cloud/subscription checks. PLC communication, alarm evaluation and Historian hosted services are not stopped by an expiry transition. RC5 makes post-grace configuration unavailable while preserving monitoring; final V1 post-expiry commercial restrictions can be refined later without turning Cloud availability into a process dependency.

## Offline-first refresh

Manual `.pgnlicense` import remains authoritative and does not require internet. `ILicenseRefreshSource` and `LicenseRefreshCoordinator` provide a future adapter boundary for optional Cloud auto-refresh. Any fetched bytes must still pass the same local Ed25519 verification/import path.

## Remote Access

Remote Access keeps the RC4 contract:

- LAN access consumes 0 remote seats.
- Remote Access is server-bound and Cloud-authoritative.
- Android/iOS/tablet/Windows Client share the same remote capacity.
- Remote failure/expiry never stops local process runtime.
