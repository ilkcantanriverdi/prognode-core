# Older PROGNODE release notes (archive)


# PROGNODE RC6.4.2 — Trend Studio Analysis

Based on RC6.4.1. See `RC6_4_2_CHANGELOG.md` for the new feedback-driven improvements and test instructions.

# RC6.4 TREND STUDIO — FEEDBACK PREVIEW

**NEW: open `TREND_STUDIO_RC6_4_2_OFFLINE_DEMO.html` to try the standalone interactive design using explicitly simulated sample values, without installation.**

For a real Core build, run `START_PROGNODE_RC6_4_2_PREVIEW.cmd` on your Windows development PC. Confirm `v0.7.2-rc6.4.2-trend-analysis` in the sidebar. Run `TEST_PROGNODE_RC6_4_TREND.ps1` for a read-only API smoke test. Full scope, known preview limits and precise acceptance instructions: `docs/RC6_4_TREND_STUDIO_PREVIEW.md`.

> This is a feedback preview; .NET compilation and real PLC/Windows integration validation remain outstanding. Older RC6.3.1-HF2 notes below are included for historical compatibility only.

---

# PROGNODE V0.7.2 RC6.3.1-HF2 — IMPORT / SIGN-IN HOTFIX

RC6.3.1 is built directly on the complete RC6.3 Developer Test Bench Core. It does **not** remove or redesign the RC6.3 numeric-alarm, FC01/FC02/FC03/FC04, Batch/Lot, Historian or Alarm-Batch work.

## RC6.3.1-HF2 import/sign-in hotfix

- A successful `.pgnlicense` import now immediately marks the local access UI as license-installed.
- The access popup immediately removes the import step, keeps the signed account name/email hint, and enables **Sign in**.
- The UI then reconciles with `/api/access/status`, `/api/license` and `/api/license/usage`; a stale/redacted poll can no longer re-disable Sign in after a successful import.
- `/api/license/import` explicitly returns `licenseInstalled=true` after the signed file passes verification and atomic persistence.
- `app.js` / `styles.css` use an RC6.3.1-HF2 cache-busting version so an older browser asset cannot survive the upgrade.


## RC6.3.1 additions

- persisted Control Center `REVOKED` state now overrides the matching local signed license for paid runtime
- 60-second Cloud license heartbeat + 10-second browser effective-license refresh
- re-importing the same revoked file cannot clear the persisted revoke while offline
- signed-out License page/API metadata is redacted
- successful import pulls the signed assigned user name/email; email is prefilled for offline login
- successful import removes the import step from the access popup; Import/Replace remains on the License page
- Manual/Internal signed licenses accept custom `maxTags` from 1 to 1,000,000
- Commercial `CUSTOMER + paymentRequired=true` keeps the fixed 100 / 250 / 500 rule

Detailed patch notes: `docs/RC6_3_1_LICENSE_SESSION_INTERNAL_QA.md`.

### Windows acceptance

```bat
BUILD_PROGNODE_CORE.cmd
```

Then, with a Manual/Internal license for example 5000 Tags:

```powershell
.\TEST_PROGNODE_RC6_3_1_LICENSE.ps1 `
  -LicensePath ".\your-internal-license.pgnlicense" `
  -Email "your@email" `
  -Password "your-password" `
  -ExpectedMaxTags 5000
```

After revoking that license in Control Center, run:

```powershell
.\TEST_PROGNODE_RC6_3_1_LICENSE.ps1 `
  -Email "your@email" `
  -Password "your-password" `
  -ExpectRevoked
```

> The generation environment does not contain the .NET SDK, so final compilation still must be run on the Windows development PC.

---


RC6.3 builds directly on **RC6.2 UI + Web V1.8 license sync** and adds the three production-Core capabilities requested by the Developer Test Bench.

## RC6.3 additions

### 1. Digital + numeric alarm engine

The existing AlarmDefinition / AlarmEngine was extended; there is no second analog alarm engine.

Conditions:

- `DigitalEquals`
- `GreaterThan`
- `GreaterThanOrEqual`
- `LessThan`
- `LessThanOrEqual`

Numeric source datatypes:

- UInt16 / Int16 / UInt32 / Int32 / Float32

Numeric definitions add `Threshold` and `Deadband`. Existing priority, ON/OFF delays, ACK and notification behavior is retained. Hysteresis affects the clear boundary; for `>= 80` with deadband `2`, activation is `>=80` and clear is `<=78` after DelayOff.

BAD/STALE quality freezes process-alarm evaluation; it cannot falsely activate or clear a process alarm. Communication/System Alarm remains responsible for communication faults.

Existing BOOL/WORD alarm rows migrate automatically to `DigitalEquals`.

### 2. Standard Modbus TCP read areas

Classic references are supported through one explicit parser/plan:

- `00001..09999` -> Coil -> FC01 -> BOOL
- `10001..19999` -> Discrete Input -> FC02 -> BOOL
- `30001..39999` -> Input Register -> FC04
- `40001..49999` -> Holding Register -> FC03

The parser exposes `ModbusArea`, `ZeroBasedOffset`, `Width` and `FunctionCode`. Batch reads never mix areas/functions. Bit requests are capped at 2000 bits; register requests at 125 registers. Existing 40001 Holding Register configurations remain unchanged.

### 3. Minimum Batch / Lot Core contract

Persisted `BatchRun` fields:

- Id
- BatchNo
- optional RecipeName
- StartedAt / nullable EndedAt
- Running / Completed / Aborted
- optional Operator / Note

API:

- `POST /api/batches/start`
- `GET /api/batches/current`
- `GET /api/batches?limit=50`
- `GET /api/batches/{id}`
- `POST /api/batches/{id}/complete`
- `POST /api/batches/{id}/abort`
- `GET /api/batches/{id}/historian`
- `GET /api/batches/{id}/alarms`

Historian rows persist `batch_id` at sample time. Alarm occurrences use a persistent many-to-many Batch mapping so an alarm that was already ACTIVE when a new Batch starts is still part of that Batch report.

SQLite schema is upgraded automatically to **version 8**. RC6.1/RC6.2 data is retained.

## RC6.2 behavior retained

- Web V1.8 signed `.pgnlicense` / Ed25519 production trust root
- commercial Tag capacities 100 / 250 / 500 (RC6.3.1 adds signed Manual/Internal custom capacity)
- Remote Access capacities 5 / 10 / 25
- Remote Access signed expiry from `payload.addons.remoteAccess.expiresAtUtc`
- 7-day signed grace lifecycle
- Tag usage shown on Tags page
- simplified Overview/header
- one-time Setup Assistant dismissal
- Alarm Historical `.xlsx` export with real columns
- best-effort Web installation activation/heartbeat

## Build / smoke test on Windows

```bat
BUILD_PROGNODE_CORE.cmd
```

Then start Core and run the existing regression checks:

```powershell
.\TEST_PROGNODE_WEB_V18_CONTRACT.ps1
.\TEST_PROGNODE_COMMERCIAL_LICENSE.ps1
.\TEST_PROGNODE_CLOUD_INSTALLATION.ps1
```

For the new Batch/Lot API, after importing the signed license:

```powershell
.\TEST_PROGNODE_RC6_3_BATCH_API.ps1 -Email "your@email" -Password "your-password"
```

Full FC01/FC02/FC03/FC04 and numeric-alarm timing acceptance belongs in the Developer Test Bench because those checks must pass through a real Modbus TCP simulator rather than fake internal values.

> This generation environment does not contain the .NET SDK, so a real `dotnet build` cannot be executed here. JavaScript syntax, SQLite schema/migration SQL, package integrity and source-level contract checks are performed before packaging. Final compilation must be run with `BUILD_PROGNODE_CORE.cmd` on the Windows development PC.

Detailed handoff: `docs/RC6_3_DEVELOPER_TEST_BENCH_CORE.md` and `DEVELOPER_TEST_BENCH_HANDOFF_RC6_3.md`.

## RC6.3.1-HF2 import/sign-in hotfix
If a previous PROGNODE Windows Service or developer Core is still listening on port 5080, the browser can silently keep showing the older UI even after building a newer package. For this hotfix use `START_PROGNODE_HF2_CLEAN.cmd`; it builds HF2, stops the old PROGNODE service/listener for the developer run, starts this package, verifies `/api/health` reports `0.7.2-rc6.3.1-hf2`, then opens the browser.

HF2 also removes the frontend license-state deadlock: while signed out, **Sign in is never disabled by UI state**. `/api/access/login` is the authoritative validation boundary. After successful import, the import block hides and the account hint is retained for the browser session.
