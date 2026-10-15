# PROGNODE Monitor protocol connectors (field-test scope)

Current customer-facing catalog: Modbus TCP, Modbus RTU (RS-485), Siemens S7 TCP, OPC UA and MQTT.
PROFINET is not offered or planned in the active catalog. The RC2 product-decision
document records an older, superseded catalog proposal.

This is an additive local API contract. `TagDefinition` and `TagValueSnapshot` JSON shapes are
unchanged apart from the new data types; `DeviceDefinition` gained an optional `username` (shown
for MQTT / OPC UA devices with a credential, never the password). Web cloud and mobile clients do
not need new fields to consume live values. These connectors are not yet production-certified.

## Data types

`Bool`, `Word`, `UInt16`, `Int16`, `UInt32`, `Int32`, `Float32`, and the 64-bit `Int64`, `UInt64`,
`Float64`. Values are stored as double, so integers above 2^53 lose their last digits. Siemens S7
reads up to 32 bits (64-bit types are rejected for S7 Tags).

## Modbus TCP and Modbus RTU

- Addresses: five digits `00001..09999` (coils), `10001..19999` (discrete inputs), `30001..39999`
  (input registers), `40001..49999` (holding registers), or six digits up to `065536`, `165536`,
  `365536`, `465536` for registers above 9999 (`419000` = holding register 19000).
- 32-bit types read two registers, 64-bit types four. Byte order per Tag: `ABCD` (as sent),
  `CDAB` (register order reversed), `BADC` (bytes swapped in each register), `DCBA` (both); the
  same meaning for 32- and 64-bit values.
- Contiguous Tags are read as one block; an address the device rejects (exception 0x02) makes only
  its own Tag `Bad` and is re-read alone for 60 s.
- Modbus TCP: `POST /api/devices/modbus-tcp`; one persistent socket per endpoint.
- Modbus RTU: `POST /api/devices/modbus-rtu` accepts `{ "name", "serialPort": "COM3", "baudRate": 9600,
  "dataBits": 8, "parity": "E", "stopBits": 1, "unitId": 1, "pollIntervalMs": 1000 }`. The device
  stores its serial settings in `host` as `COM3;9600;8E1` (`port` is null). Unit ID 1..247; baud
  rates 1200..115200. `GET /api/serial-ports` lists the COM ports of the PC.
  - One open line per COM port; requests on a line are serialized, so several devices (unit IDs)
    can share an RS-485 line. CRC-16 is checked; the 3.5-character silent interval is kept.
  - A silent device times out without closing the line; a corrupted answer (CRC) or a late answer
    to an earlier request is skipped or rejected without losing the line. Adapters that echo the
    request are handled. The port is reopened only after the port itself failed (unplugged adapter,
    port in use).
  - Device CSV: `Host` = `COM3` with a `Serial` column such as `9600 8E1` (default `9600 8E1`), or
    the quoted `"COM3;9600;8E1"` a device export writes.

## Siemens S7

- Absolute addresses only. DBs: `DB1.DBX0.0`, `DB1.DBW2`, `DB1.DBD4` (non-optimized DBs: turn
  off "optimized block access"). Inputs `I0.0` / `IW64` / `ID100`, outputs `Q0.1` / `QW2` / `QD4`,
  bit memory `M10.0` / `MW20` / `MD30`; `%` prefix and German `E` / `A` accepted. I/Q/M need only
  PUT/GET on S7-1200/1500. Optimized (symbolic) DB access is not supported.

## MQTT

- `POST /api/devices/mqtt` accepts `{ "name": "Broker", "host": "mqtts://broker.example.com", "port": 8883, "pollIntervalMs": 1000, "username": "plant", "password": "…" }` (user name and password optional).
- `host` may be a bare DNS/IP name (treated as `mqtts://`) or an explicit `mqtt://` / `mqtts://` URL. The port is always a separate field. URL credentials, paths, query strings and fragments are rejected.
- A Tag `address` is one exact topic, e.g. `plant/temperature`, or a topic and the field of a JSON message: `plant/env#temp`, `gw/01#sensors[0].value` (`$.` prefix allowed). `+` wildcards are not accepted. Numeric values use invariant decimal text (also as JSON strings); BOOL accepts `true`, `false`, `1`, or `0`. Messages over 64 KB, malformed UTF-8 or JSON, a missing / null / object field, non-finite values and out-of-range values become `Bad`.
- Core maintains a read-only subscription. No publish capability exists. `mqtts://` uses normal OS certificate validation; there is no insecure-certificate override. `mqtt://` is plaintext and should only be used on an explicitly trusted local network.
- Retained messages are `Uncertain` because source freshness is unknown. A missing initial message is `Uncertain`; after `max(10 seconds, 3 × pollIntervalMs)` without a new message (capped at 5 minutes), the last value becomes `Stale`. Disconnect/reconnect clears cached values, then subscribes again. A changed credential reconnects.
- No client certificate, wildcard or publish configuration.

## OPC UA

- `POST /api/devices/opc-ua` accepts `{ "name": "PLC", "endpointUrl": "opc.tcp://plc.example.com:4840/", "pollIntervalMs": 1000, "username": "…", "password": "…" }` (credential optional; anonymous without it).
- A Tag `address` is an OPC UA NodeId, e.g. `ns=2;s=Temperature` or `ns=2;i=1234`. The connector polls the Value attribute through a persistent session and maps scalar BOOL/numeric results into existing Tag values and quality states. It never writes, calls methods, browses, or creates monitored-item subscriptions.
- Only SignAndEncrypt endpoints with Basic256Sha256 or AES/SHA-256 policies are accepted. Unknown server certificates are **not** accepted automatically; certificate domain checking is enabled. The application certificate and PKI are under `<DataRoot>/opc-ua/pki/` (`own`, `trusted`, `issuers`, `rejected`). An on-site administrator must independently verify and trust the server certificate, and the OPC UA server must trust the PROGNODE Monitor application certificate.
- User name / password login is sent on the encrypted session. Clear errors: the server needs a login, rejects the login, or the endpoint offers no user-name token.
- Backup excludes private-key files. After a restore onto a new installation, the OPC UA application certificate may be regenerated; the server must explicitly trust the new certificate before data resumes.
- A failed secure session or read marks the affected poll `Bad`; a bad UA status is `Bad`, uncertain UA status is `Uncertain`, and a value/type mismatch is `ConfigError`.

## Device credentials

- Stored in table `device_credentials` (cascade on device delete), the password encrypted with
  Windows DPAPI (LocalMachine) plus entropy. The API returns only `username`.
- `PUT /api/devices/{id}`: `username` null keeps the credential, `""` removes it, a name saves it;
  an empty `password` keeps the stored one.
- A database restored on another PC keeps the user name; the password reads as missing and must be
  entered again.

## Integration and acceptance

- `PUT /api/devices/{id}` edits host and poll settings (and the credential / RTU serial settings).
  `/api/tags` create/update validate by the device's protocol.
- `/api/protocols` reports all five as `Available`. The local commissioning UI exposes their
  device and Tag fields.
- `pwsh -File scripts/Validate-Baseline.ps1` performs locked restore, Release build and the console
  contracts. The protocol contracts cover: Modbus FC01/02/03/04 and six-digit / 64-bit values in all
  byte orders against isolated TCP responders; Modbus RTU over an in-memory RS-485 line (CRC, two
  units, exception, timeout, corrupted and late answers, echo, unplugged adapter); S7 DB / I / Q / M
  ReadVar; MQTT login and JSON fields against an in-process broker; OPC UA URL/NodeId validation and
  application-certificate generation; the five-protocol catalog and Tag dispatch.
- `tests/ProtocolSoak` runs long tests against simulators (`tools/protocol-soak`, all requiring
  logins) or real devices (`--config`). Modbus RTU was soaked over a real Windows COM port (com0com
  pair, pymodbus RTU slaves, two units, outages) with 0 wrong values.
- A passing automated suite means zero **detected test failures**, not zero protocol defects. The
  tests do **not** prove interoperability with physical PLCs, RS-485 wiring and adapters, site broker
  TLS/certificates, or field performance. Capture physical-device results in a separate field
  acceptance report.
