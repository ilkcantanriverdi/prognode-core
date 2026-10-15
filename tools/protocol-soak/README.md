# Protocol soak

Long-running test of the protocols PROGNODE Monitor reads: Modbus TCP and RTU, Mitsubishi MC, Omron
FINS, EtherNet/IP (Logix), BACnet/IP, Siemens S7, OPC UA and MQTT. The soak (`tests/ProtocolSoak`) polls every second with the same
readers the Monitor uses, checks every value
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

The soak starts the simulators in `simulators.py` (pymodbus, an MC 3E server, a FINS/TCP server,
cpppo's Logix emulation, a bacpypes3 BACnet device, python-snap7, asyncua, paho-mqtt) and an
MQTT broker in-process, approves the OPC UA server certificate the way an administrator does in Core,
and takes one of them down every `--fault-every` minutes for `--fault-down` seconds, in turn.

Every simulator serves the same values, so every reading is checked:

* counter *k* = (whole Unix seconds + *k*) mod 30000, for `--tags` counters per protocol (MQTT: up to 50);
* fixed values per data type: Word 1234, Int16 -1234, Int32 -123456, UInt32 4000000000, Float32 12.5
  (Modbus also in CDAB word order), Bool true (S7 also a false bit).

Options: `--minutes`, `--tags`, `--fault-every` (minutes), `--fault-down` (seconds),
`--faults modbus,mc,fins,enip,bacnet,s7,opcua,mqtt-broker,mqtt` (which outages to inject; `mqtt` is the publisher),
`--report <file.md>` (a `.json` with the raw numbers is written next to it).

## Modbus RTU over a real COM port

With a virtual serial pair (com0com: COM3 <-> CNCB0) the RTU reader is tested through the real
Windows serial stack: start pymodbus RTU slaves (unit 1, and unit 2 with counters offset by 1000)
on the second port and point a `--config` target at the first:

```bash
.venv/Scripts/python simulators.py modbus-rtu --counters 50 --serial "\\.\CNCB0"
```

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
* Modbus RTU: `"protocol": "Modbus RTU"` with `"host": "COM3;9600;8E1"` (port, baud rate, data
  bits, parity N/E/O, stop bits) and the device's `"unitId"`; several targets may share a COM port.
* Mitsubishi MC: `"protocol": "Mitsubishi MC"`, the SLMP / MC port (binary, TCP) and `"unitId"` 0 for
  iQ-R / Q / L (hex X/Y) or 1 for FX5 (octal X/Y). `check_mc_simulator.py <port>` checks the MC
  simulator with pymcprotocol.
* Omron FINS: `"protocol": "Omron FINS"`, port 9600. Addresses `D100`, `CIO100`, `W10`, `H5`, `A20`,
  `E0_100`, `T0` / `C0`, bits `D100.05`; 32/64-bit values low word first. `check_fins_simulator.py
  <port>` checks the FINS simulator with the independent `fins` client (GPL, used only as a test tool).
* EtherNet/IP: `"protocol": "EtherNet/IP"`, port 44818, `"unitId"` = CPU slot (255 = direct, Micro800).
  Tags by Studio 5000 name (`Tank_Level`, `Counters[3]`, `Program:Main.Pump.Amps`, `Status.5`); the Tag data
  type must match the controller's. The simulator is cpppo (GPL, test tool only); it is slow (hundreds of
  ms per request), real controllers answer in a few ms.
* BACnet/IP: `"protocol": "BACnet/IP"`, UDP port 47808; Tags are objects (`AI:1`, `BV:2`, `MSV:1`) and read
  their Present_Value. Only devices on the IP network (no MS/TP behind a router yet).
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
