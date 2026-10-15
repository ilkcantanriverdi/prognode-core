# PROGNODE Monitor protocol connectors (field-test scope)

Current customer-facing catalog: Modbus TCP, Modbus RTU (RS-485), Mitsubishi MC, Omron FINS, EtherNet/IP (Logix), Siemens S7 TCP, OPC UA and MQTT.
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

## Mitsubishi MC

- `POST /api/devices/mitsubishi-mc` accepts `{ "name", "host": "192.168.3.39", "port": 5007, "series": 0, "pollIntervalMs": 1000 }`.
  `series` (stored in `unitId`): 0 = MELSEC iQ-R / Q / L, 1 = iQ-F (FX5). The port is the SLMP / MC
  protocol connection opened on the PLC's Ethernet port in GX Works (TCP, binary code).
- MC protocol 3E frame, binary code, Q-compatible batch read (command 0401, subcommands 0000 words /
  0001 bits), which Q, L, iQ-R and FX5 accept. Read-only. One persistent connection per endpoint.
- Addresses: word devices D, W, R, SD, SW, TN (timer current value), CN (counter current value);
  bit devices X, Y, M, L, F, V, B, SM, SB, TS, CS; a bit of a word as D100.A (hex bit). X/Y/B/W/SB/SW
  numbers are hexadecimal; on FX5, X/Y are octal. 32- and 64-bit values use the next words, low word
  first. ZR is not offered yet: its numbering (decimal in GX Works, hexadecimal in MC client
  libraries) must be confirmed on a real PLC.
- Close Tags of one device type are read in one batch (up to 960 words / 3584 bits). A non-zero end
  code (e.g. 0xC056 device out of range) makes only the affected Tag `Bad`; that Tag is then read
  alone for 60 s. Device CSV: Protocol `Mitsubishi MC` with a `Series` column (`iQ-R`, `FX5`).
- Request bytes are checked against pymcprotocol 0.3.0; the soak simulator (`simulators.py mc`) is
  checked with pymcprotocol (`check_mc_simulator.py`).

## Omron FINS

- `POST /api/devices/omron-fins` accepts `{ "name", "host": "192.168.250.1", "port": 9600, "pollIntervalMs": 1000 }`.
  FINS/TCP on the CPU's built-in EtherNet/IP port or an Ethernet unit (CS, CJ, CP, NJ, NX). The FINS
  node addresses come from the FINS/TCP handshake, so no node numbers are entered. On NJ/NX, FINS must
  be allowed in the built-in port settings and the variables mapped to CIO, W, H, D or EM.
- FINS memory area read (command 0101), read-only. One persistent connection per endpoint; gateway
  count 02, destination unit 00 (CPU).
- Addresses as in CX-Programmer / Sysmac Studio: `D100` (also `DM100`), `CIO100` (or `100`), `W10`,
  `H5`, `A20`, `E0_100` (EM bank 0..F), `T0` / `C0`. BOOL is a bit of a word with a decimal bit
  `00..15` (`CIO0.05`, `D100.15`), or `T0` / `C0` for the completion flag; a 16-bit type on `T` / `C`
  reads the present value. 32- and 64-bit values use the next words, low word first (each word
  big-endian). BCD timers are read as raw words.
- Close Tags of one area are read in one batch (up to 500 words). A non-zero end code (e.g. 0x1103
  address out of range) makes only the affected Tag `Bad`; that Tag is then read alone for 60 s.
  FINS/TCP errors such as "all connections in use" are reported with the reason. Device CSV:
  Protocol `Omron FINS` (or `FINS`), default port 9600.
- Request bytes are checked against the `fins` package 1.0.5 (only the gateway count differs: the
  library sends 07); the soak simulator (`simulators.py fins`) is checked with that client
  (`check_fins_simulator.py`).

## EtherNet/IP (Logix)

- `POST /api/devices/ethernet-ip` accepts `{ "name", "host": "192.168.1.20", "port": 44818, "slot": 0, "pollIntervalMs": 1000 }`.
  `slot` (stored in `unitId`) is the CPU slot on the backplane (0..31; CompactLogix 0), or 255 to address
  the controller directly without a backplane route (Micro800). The host is the controller's Ethernet
  port or an Ethernet module in its chassis.
- Explicit messaging over TCP: RegisterSession, SendRRData (UCMM) with Read Tag (0x4C) by symbolic name,
  up to 24 per Multiple Service Packet (0x0A), wrapped in Unconnected Send to the CPU slot. Read-only.
  One persistent session per endpoint. The health probe uses ListIdentity and reports the product name.
- Addresses are Studio 5000 tag names: `Tank_Level`, `Motor.Speed` (UDT member), `Counters[3]`,
  `Grid[1,2]`, `Program:MainProgram.Pump.Amps` (program scope). BOOL: a BOOL tag or a bit of an integer
  tag `Status.5`. Whole structures and BOOL arrays are not read; read a member or element instead.
- The value is decoded by the type the controller reports (SINT, INT, DINT, LINT, USINT, UINT, UDINT,
  ULINT, REAL, LREAL, BOOL); a Tag whose data type does not fit is `Bad` and names the controller's type.
  Each reply carries its own status, so a missing tag (0x05) or a tag without External Access (0x0F)
  makes only its own Tag `Bad`; a wrong CPU slot makes the device's Tags `Bad` with a route hint.
  Device CSV: Protocol `EtherNet/IP` (or `Logix`, `ENIP`) with a `Slot` column (`0`..`31` or `direct`),
  default port 44818.
- Symbolic paths are checked against pycomm3 1.2.16 (indexes above 65535 use the CIP 32-bit member
  segment 0x2A; pycomm3 sends 0x2B). The soak simulator (`simulators.py enip`) is cpppo 5.2.5's Logix
  emulation (GPL, used only as a test tool).

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
- `/api/protocols` reports all eight as `Available`. The local commissioning UI exposes their
  device and Tag fields.
- `pwsh -File scripts/Validate-Baseline.ps1` performs locked restore, Release build and the console
  contracts. The protocol contracts cover: Modbus FC01/02/03/04 and six-digit / 64-bit values in all
  byte orders against isolated TCP responders; Modbus RTU over an in-memory RS-485 line (CRC, two
  units, exception, timeout, corrupted and late answers, echo, unplugged adapter); S7 DB / I / Q / M
  ReadVar; Mitsubishi MC, Omron FINS and EtherNet/IP frames, values and refusals against TCP responders;
  MQTT login and JSON fields against an in-process broker; OPC UA URL/NodeId validation and
  application-certificate generation; the eight-protocol catalog and Tag dispatch.
- `tests/ProtocolSoak` runs long tests against simulators (`tools/protocol-soak`, all requiring
  logins) or real devices (`--config`). Modbus RTU was soaked over a real Windows COM port (com0com
  pair, pymodbus RTU slaves, two units, outages) with 0 wrong values.
- A passing automated suite means zero **detected test failures**, not zero protocol defects. The
  tests do **not** prove interoperability with physical PLCs, RS-485 wiring and adapters, site broker
  TLS/certificates, or field performance. Capture physical-device results in a separate field
  acceptance report.
