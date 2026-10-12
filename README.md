# PROGNODE Core

Site-side monitoring service of PROGNODE: reads PLCs and meters (Modbus TCP, Siemens S7, OPC UA, MQTT),
evaluates alarms, records the historian and serves the local web interface. Also contains the tray agent
and the PROGNODE Client for Windows.

| Path | Contents |
|---|---|
| `src/` | Core host, protocols, alarm, historian, licensing, tray agent, Windows Client |
| `tests/` | Console contract suites and Node/Python UI regression tests |
| `installer/` | Inno Setup installers (`installer/build.ps1 -Version x.y.z`) |
| `docs/` | Current contracts, compliance and security documentation |
| `tools/Prognode.Backup.Restore` | Developer backup tool; installed Core uses `PROGNODE.Core.exe backup` via Start menu › Restore PROGNODE Backup |

## Build and test

```powershell
pwsh -File scripts/Validate-Baseline.ps1   # release build + contract suites
node tests/run-node-tests.cjs              # UI and static regression tests
```

`START_PROGNODE.cmd` runs Core and the tray agent from source on a developer PC. Customers use the installer.

See [AGENTS.md](AGENTS.md) for working rules, [SECURITY.md](SECURITY.md) for vulnerability reporting and
[docs/compliance/INTENDED_USE.md](docs/compliance/INTENDED_USE.md) for intended use.
