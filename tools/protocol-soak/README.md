# Protocol soak

Long-running test of the four protocols Core reads: Modbus TCP, Siemens S7, OPC UA and MQTT. The
soak (`tests/ProtocolSoak`) polls every second with the same readers Core uses, checks every value
it reads, injects outages and writes a report. The quick contract tests in `tests/ProtocolContract`
stay the CI gate; this is the long run you start by hand.

## Simulated devices (no hardware needed)

One-time setup (Python 3.11 or newer):

```bash
cd tools/protocol-soak
python -m venv .venv
.venv/Scripts/python -m pip install -r requirements.txt
```

Run (from the repository root):

```bash
dotnet run --project tests/ProtocolSoak -c Release -- --minutes 30 --tags 200 --fault-every 2 --fault-down 20
```

The soak starts the simulators in `simulators.py` (pymodbus, python-snap7, asyncua, paho-mqtt) and an
MQTT broker in-process, approves the OPC UA server certificate the way an administrator does in Core,
and takes one of them down every `--fault-every` minutes for `--fault-down` seconds, in turn.

Every simulator serves the same values, so every reading is checked:

* counter *k* = (whole Unix seconds + *k*) mod 30000, for `--tags` counters per protocol (MQTT: up to 50);
* fixed values per data type: Word 1234, Int16 -1234, Int32 -123456, UInt32 4000000000, Float32 12.5
  (Modbus also in CDAB word order), Bool true (S7 also a false bit).

Options: `--minutes`, `--tags`, `--fault-every` (minutes), `--fault-down` (seconds),
`--faults modbus,s7,opcua,mqtt-broker,mqtt` (which outages to inject; `mqtt` is the publisher),
`--report <file.md>` (a `.json` with the raw numbers is written next to it).

## Real devices

Copy `real-plcs.example.json`, fill in your PLCs and Tags, and run:

```bash
dotnet run --project tests/ProtocolSoak -c Release -- --config my-plcs.json --minutes 480
```

A target can carry `"username"` and `"password"` for an MQTT broker or an OPC UA server that does
not allow anonymous login (the passwords stay in your file; keep it out of git). No outages are
injected. Pull a cable, restart a PLC or stop a broker while it runs; the report
shows how fast each outage was noticed and how fast readings were correct again. A Tag can carry
`"expect"`: a number (the value it must read) or `"counter:k"`; without it any Good reading counts.
For OPC UA the soak approves the server certificate it is shown, as an administrator would in Core.

## Result

PASS for a protocol means: no wrong value, at least 99.9 % Good readings outside outages, every
outage recovered within 60 s and, for runs of 30 minutes or more, less than 100 MB memory growth.
Reports go to `tools/protocol-soak/reports/` unless `--report` says otherwise.

## Notes from the simulated runs

* S7: absolute addresses only. DBs: `DB1.DBX0.0`, `DB1.DBW2`, `DB1.DBD4` (non-optimized DBs; turn
  off "optimized block access" for the DBs PROGNODE reads). Inputs, outputs and bit memory: `I0.0` /
  `IW64` / `ID100`, `Q0.1` / `QW2`, `M10.0` / `MW20` / `MD30` (also `%I0.0` and the German `E`/`A`);
  these need only PUT/GET on S7-1200/1500. Optimized (symbolic) DB access is not supported.
* Modbus: six-digit addresses reach registers above 9999 (`419000` = holding register 19000);
  Int64 / UInt64 / Float64 read four registers, with the same byte orders as 32-bit values.
* MQTT: a Tag reads a plain message (`21.5`, `true`) or one field of a JSON message
  (`plant/env#temp`, `gw/01#sensors[0].value`). The simulated broker and OPC UA server require a
  user name and password, as most site servers do.
* OPC UA: Core only connects with SignAndEncrypt (Basic256Sha256 or the AES policies) and checks
  that the server certificate names the host in the endpoint URL. For `localhost`/`127.0.0.1` the
  OPC UA .NET stack looks for the PC's name in the certificate.
* Integer Tags with decimal places are read with implied decimals (raw 1234 with 2 places = 12.34);
  Word and Bool ignore decimal places.
