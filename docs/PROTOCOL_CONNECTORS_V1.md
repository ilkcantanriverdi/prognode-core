# Core protocol connectors v1 (field-test scope)

Current customer-facing Core catalog: Modbus TCP, Siemens S7 TCP, OPC UA and MQTT.
PROFINET is not offered or planned in the active catalog. The RC2 product-decision
document records an older, superseded catalog proposal.

This is an additive local Core API contract. `DeviceDefinition`, `TagDefinition`, and
`TagValueSnapshot` JSON shapes are unchanged. Web cloud and mobile clients do not need
new fields to consume live values. These connectors are not yet production-certified.

## MQTT

- `POST /api/devices/mqtt` accepts `{ "name": "Broker", "host": "mqtts://broker.example.com", "port": 8883, "pollIntervalMs": 1000 }`.
- `host` may be a bare DNS/IP name (treated as `mqtts://`) or an explicit `mqtt://` / `mqtts://` URL. The port is always a separate field. URL credentials, paths, query strings and fragments are rejected.
- A Tag `address` is one exact topic, e.g. `plant/temperature`. `+` and `#` wildcards are not accepted. Numeric payloads use invariant decimal text; BOOL accepts `true`, `false`, `1`, or `0`. Payloads over 256 bytes, malformed UTF-8, non-finite values and out-of-range values become `Bad`.
- Core maintains a read-only subscription. No publish capability exists. `mqtts://` uses normal OS certificate validation; there is no insecure-certificate override. `mqtt://` is plaintext and should only be used on an explicitly trusted local network.
- Retained messages are `Uncertain` because source freshness is unknown. A missing initial message is `Uncertain`; after `max(10 seconds, 3 × pollIntervalMs)` without a new message (capped at 5 minutes), the last value becomes `Stale`. Disconnect/reconnect clears cached values, then subscribes again.
- This v1 scope has no username/password, client certificate, wildcard, JSON payload extraction, or publish configuration. Credentials must not be put in the broker URL.

## OPC UA

- `POST /api/devices/opc-ua` accepts `{ "name": "PLC", "endpointUrl": "opc.tcp://plc.example.com:4840/", "pollIntervalMs": 1000 }`.
- A Tag `address` is an OPC UA NodeId, e.g. `ns=2;s=Temperature` or `ns=2;i=1234`. The connector polls the Value attribute through a persistent anonymous session and maps scalar BOOL/numeric results into existing Tag values and quality states. It never writes, calls methods, browses, or creates monitored-item subscriptions.
- Only SignAndEncrypt endpoints with Basic256Sha256 or AES/SHA-256 policies are accepted. Unknown server certificates are **not** accepted automatically; certificate domain checking is enabled. Core's application certificate and PKI are under `<DataRoot>/opc-ua/pki/` (`own`, `trusted`, `issuers`, `rejected`). An on-site administrator must independently verify and trust the server certificate, and the OPC UA server must trust the Core application certificate. No username/password flow is included in v1.
- Core backup excludes private-key files. After a restore onto a new Core installation, the OPC UA application certificate may be regenerated; the server must explicitly trust the new certificate before data resumes. Do not copy private keys through the backup archive.
- A failed secure session or read marks the affected poll `Bad`; a bad UA status is `Bad`, uncertain UA status is `Uncertain`, and a value/type mismatch is `ConfigError`.

## Integration and acceptance

- `PUT /api/devices/{id}` edits existing MQTT or OPC UA host and poll settings. The existing `/api/tags` create/update routes validate by the selected device protocol; they no longer apply Modbus address parsing to all Tags.
- `/api/protocols` reports both as `Available` with `Field Test` note. The local commissioning UI exposes their device and Tag fields.
- `pwsh -File scripts/Validate-Baseline.ps1` performs locked restore, Release build and six console contracts. The protocol contract covers Modbus FC01/02/03/04 Tag reads against an isolated TCP responder; S7 COTP, setup and BOOL/WORD ReadVar against an isolated responder; MQTT broker-to-Tag subscription and payload quality; and OPC UA URL/NodeId validation plus isolated application-certificate generation. It also checks that the catalog contains exactly four enabled protocols and that Tag creation/update dispatches to each protocol's validator without a global Modbus parse.
- A passing automated suite means zero **detected test failures**, not zero protocol defects. The OPC UA suite does not establish a full secure server session. The tests do **not** prove interoperability with physical PLCs, site broker TLS/certificates, disconnect/reconnect behavior, service restart, or field performance.
- Before beta: test real Modbus and S7 PLCs (including offline/reconnect and representative data types), a secured OPC UA server with verified trust on both sides, and a site MQTT broker (including disconnect/reconnect and TLS). For all four, verify Tag quality, alarm/historian propagation and service restart with persisted settings. Capture results in a separate field acceptance report.
