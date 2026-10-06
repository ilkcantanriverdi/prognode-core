# Core process-data reliability milestone v1 (2026-09-29)

Scope: commercial license policy, local Trend Studio, Alarm runtime and Historian.
This milestone does not decide trial duration/capacity/start time or change a signed
license format, Core API JSON shape, or SQLite schema.

## Implemented and automatically checked

- Historian long-range decimation reserves enough room for all current Tag quality
  states plus real first/last and Good high/low samples. A 12,000-sample isolated
  SQLite contract checks the 3,000-point budget, original timestamps, quality
  states and high/low spikes. Trend definition/enrollment persistence and
  Historian retention cleanup are checked.
- Fullscreen Trend Studio does not display a fabricated zero minimum/maximum or
  average when a series contains only invalid-quality samples. Genuine zero
  measurements still display as zero.
- Alarm contract checks activation from Good data, preservation of an active
  occurrence through Bad quality and a runtime restart, then clear and
  persistent history after Good recovery. This is an isolated runtime test,
  not a field notification test.
- Alarm and Historian **edit** routes now enforce the same module entitlement as
  their create routes; delete remains available for configuration cleanup. The
  license policy contract checks Tag capacity and that a Cloud revoke applies
  only to the matching local license ID. Commercial timestamp boundaries
  (ACTIVE, EXPIRING_SOON, GRACE, EXPIRED) are checked. The policy test uses a
  supplied license snapshot; cryptographic license-file verification is separate.

Run `pwsh -File scripts/Validate-Baseline.ps1` for the Release build and six
console contracts. CI also runs the Trend UI, fullscreen null-stat, license-route
and Historian SQL smoke tests.

## Still required before production claim

- Signed-license import/expiry/renewal and real Cloud activation/revoke tests on
  the installed Windows Core. Trial policy remains an explicit product decision.
- PLC/broker → Tag → Alarm/Historian → Trend UI end-to-end test with device loss,
  recovery, service restart and persisted settings.
- Alarm mobile notification/ACK and Historian retention/export acceptance with
  representative field volume; long-range query performance benchmark.

Passing the current suite means no detected automated-test failures, not that
every field scenario is defect-free.
