# PROGNODE V0.7.2 RC6.3 — Developer Test Bench Core Contract

RC6.3 builds on RC6.2. The signed Web V1.8 licensing, Remote Access, Tag capacity, alarm history XLSX export and UI cleanup from RC6.2 are retained.

The purpose of RC6.3 is to let **PROGNODE Developer Test Bench** exercise the production Core through the same network/API boundaries as a real installation:

`Modbus TCP PLC Simulator -> Protocol -> Tag Polling -> Current Value -> Alarm -> Historian -> Trend / Batch report`

The Test Bench must not write directly to SQLite and must not inject values into internal Core services.

## 1. Numeric / analog alarms

The existing alarm engine now supports both digital and numeric definitions. No second analog alarm engine was introduced.

Supported conditions:

- `DigitalEquals`
- `GreaterThan`
- `GreaterThanOrEqual`
- `LessThan`
- `LessThanOrEqual`

Digital sources remain `Bool` and register `Word` bit alarms. Numeric sources are:

- `UInt16`
- `Int16`
- `UInt32`
- `Int32`
- `Float32`

Numeric definitions use `threshold` and `deadband`. Existing `priority`, `delayOnMs`, `delayOffMs`, notification flags/mode/repeat and continue-after-clear behavior are shared by the same engine.

Hysteresis is applied only to the clear boundary while the occurrence is ACTIVE. Example:

- Condition `GreaterThanOrEqual`
- Threshold `80`
- Deadband `2`
- ACTIVE boundary: `value >= 80`
- CLEAR boundary: `value <= 78`

`BAD` / `STALE` Tag quality does not advance either ACTIVE or CLEAR process-alarm timers and therefore cannot create a false process transition. Device communication/system alarms remain responsible for communication failure.

SQLite migration adds the following alarm-definition columns with backward-compatible defaults:

- `condition TEXT NOT NULL DEFAULT 'DigitalEquals'`
- `threshold REAL NULL`
- `deadband REAL NOT NULL DEFAULT 0`

Therefore existing RC6.1/RC6.2 BOOL/WORD rows continue as `DigitalEquals` without manual migration.

## 2. Standard Modbus read areas

Classic reference notation remains the public configuration format.

| Reference | Area | Function | Allowed datatype |
|---|---|---:|---|
| `00001..09999` | Coil | FC01 | BOOL |
| `10001..19999` | Discrete Input | FC02 | BOOL |
| `30001..39999` | Input Register | FC04 | WORD / UInt16 / Int16 / UInt32 / Int32 / Float32 |
| `40001..49999` | Holding Register | FC03 | WORD / UInt16 / Int16 / UInt32 / Int32 / Float32 / BOOL bit |

`ModbusTagAddressParser` now returns an explicit:

- `ModbusArea`
- `ZeroBasedOffset`
- `Width`
- `FunctionCode`

Low-level client methods:

- `ReadCoilsAsync`
- `ReadDiscreteInputsAsync`
- `ReadInputRegistersAsync`
- `ReadHoldingRegistersAsync`

The MBAP/PDU validation continues to verify transaction ID, protocol ID, Unit ID, response function, exception response shape and payload length.

Read planning groups only Tags with the same `ModbusArea` / `FunctionCode`. Register blocks are capped at 125 registers and bit blocks at 2000 bits. Existing `40001..49999` configurations require no migration.

## 3. Minimum Batch / Lot runtime contract

`BatchRun` is intentionally small and is not an ISA-88/MES implementation.

Fields:

- `id`
- `batchNo` (required human-readable Batch/Lot number)
- `recipeName` (optional)
- `startedAt`
- `endedAt` (nullable)
- `state`: `Running`, `Completed`, `Aborted`
- `operator` (optional)
- `note` (optional)

Only one Batch may be `Running` at a time. SQLite enforces this with a partial unique index as a second line of defence.

### API

All mutation calls use the normal PROGNODE authentication/session middleware, exactly like the production client.

- `POST /api/batches/start`
- `GET /api/batches/current`
- `GET /api/batches?limit=50`
- `GET /api/batches/{id}`
- `POST /api/batches/{id}/complete`
- `POST /api/batches/{id}/abort`
- `GET /api/batches/{id}/historian?tagIds=<guid,guid>&maxRows=250000`
- `GET /api/batches/{id}/alarms?limit=200`

Example start body:

```json
{
  "batchNo": "ABC123",
  "recipeName": "Recipe-A",
  "operator": "Operator 1",
  "note": "Developer Test Bench run"
}
```

Complete/abort body may be `{}` or optionally:

```json
{ "note": "Completed by acceptance test" }
```

## Historian association

`historian_samples.batch_id` is persisted at sampling time. While one Batch is running, every historian sample (including BAD/STALE quality rows) carries that Batch ID. The relation survives Core restart because it is stored in SQLite.

## Alarm association

Alarm occurrence membership is persisted in `batch_alarm_occurrences(batch_id, alarm_key, active_at)` and the event row also carries `batch_id` when activation happens inside a running Batch.

The mapping is many-to-many so one long-running alarm can overlap sequential Lots. When a new Batch starts, Core links alarms that were already ACTIVE to that Batch as well. This satisfies the production-report requirement without changing alarm state.

## Explicitly not included in RC6.3

- PLC Recipe download
- sequence control
- ISA-88 state machine
- MES integration
- production scheduling
- OEE

Those remain outside the minimum Test Bench contract.
