# PROGNODE DEVELOPER TEST BENCH — RC6.3 HANDOFF

Use **PROGNODE V0.7.2 RC6.3** as the Core under test.

Production boundary remains mandatory:

`PLC Simulator -> Modbus TCP -> PROGNODE Core API/runtime -> Alarm/Historian/Batch query`

Do not inject fake CurrentTagValueStore values, call internal services, or write SQLite directly.

## Acceptance areas now IMPLEMENTED in Core

### A. Numeric alarm
Create a Float32 Tag and alarm through normal Core API/UI:

- `condition = GreaterThanOrEqual`
- `threshold = 80.0`
- `deadband = 2.0`
- `delayOnMs = 1000`
- `delayOffMs = 2000`

Expected simulator sequence:

1. Hold `<80` -> inactive.
2. Hold `>=80` for less than 1 s -> still inactive.
3. Hold `>=80` for at least 1 s -> ACTIVE.
4. Set `79.9` -> remains ACTIVE.
5. Hold `<=78.0` for less than 2 s -> remains ACTIVE.
6. Hold `<=78.0` for at least 2 s -> CLEARED.
7. Force communication/BAD/STALE while process alarm is active -> process alarm must not fake-clear; communication/system alarm owns the link problem.

### B. Modbus data areas
Simulator must expose and verify independently:

- `00001` Coil -> FC01 BOOL
- `10001` Discrete Input -> FC02 BOOL
- `30001` Input Register -> FC04 numeric
- `40001` Holding Register -> FC03 numeric / register bit

Also verify that adjacent addresses of the same area are batched and that `30001` + `40001` never share one request.

### C. Batch/Lot
Use Core API:

- `POST /api/batches/start`
- `GET /api/batches/current`
- `POST /api/batches/{id}/complete`
- `POST /api/batches/{id}/abort`
- `GET /api/batches`
- `GET /api/batches/{id}`
- `GET /api/batches/{id}/historian`
- `GET /api/batches/{id}/alarms`

Start `ABC123`, run simulated Temperature / Pressure / Flow, create at least one alarm, complete the Batch, then verify persisted historian rows and alarm occurrences are returned by Batch ID after a Core restart.

Also test an alarm that is already ACTIVE before `ABC124` starts. It must appear in `ABC124` alarm history without being re-triggered.

## Compatibility regression

Before marking acceptance PASS, re-test:

- legacy `40001..49999` Holding Register Tags
- BOOL / WORD DigitalEquals alarms
- Historian / Trend
- signed Web V1.8 license import + offline sign-in
- Tag capacity
- Remote Access status

SQLite schema upgrades automatically to schema version 8; existing alarm rows default to `DigitalEquals`.
