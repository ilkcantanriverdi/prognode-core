# PROGNODE V0.7.2 RC6.2 — UI + Installation Sync

## Product UI decisions

- Setup Assistant is a first-run workflow, not persistent Overview chrome.
- Overview shows operational facts only; configuration authorization is available from the account chip, not repeated as a `Signed in` KPI.
- The header account chip shows the user identity only.
- Tag commercial capacity is shown where Tags are engineered, not only on the License page.
- Alarm History operational export uses XLSX to avoid locale-dependent one-column CSV imports in Excel.

## First-run persistence

Browser keys:

```text
prognode.quickStart.snoozed.session.rc6.2  -> session-only "Not now"
prognode.quickStart.hidden.v1              -> persistent "Don't show this again"
```

Completing the commissioning steps continues to suppress the assistant naturally.

## Cloud installation registration

RC6.1 had a Cloud adapter but shipped with an empty base URL. That meant no activation or heartbeat call was made even when the Server had internet access.

RC6.2 enables trusted production host discovery:

```text
https://prognode.io
https://account.prognode.io
https://control.prognode.io
```

The endpoint contract remains:

```text
POST /api/core/activate
POST /api/core/heartbeat
```

A missing route (`404/405`) or auth-gated wrong host (`401/403`) advances to the next trusted host. Contract/validation failures such as `400` remain visible as errors and are not hidden by fallback.

The background service checks local activation state every 10 seconds, but only sends heartbeat traffic when the configured heartbeat interval has elapsed. Importing a new/revised signed license clears the cached Cloud activation state so the next short check performs a fresh activation.

Cloud failure never disables a valid signed offline license, PLC polling, Alarm runtime or Historian runtime.
