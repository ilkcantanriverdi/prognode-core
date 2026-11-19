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
| `tools/Prognode.Backup.Restore` | Developer backup tool; installed Core restores from Settings › Backup & audit |

## Build and test

```powershell
pwsh -File scripts/Validate-Baseline.ps1   # release build + contract suites
node tests/run-node-tests.cjs              # UI and static regression tests
```

`START_PROGNODE.cmd` runs Core and the tray agent from source on a developer PC. Customers use the installer.

### Run from source in an isolated folder

To try Core next to an installed PROGNODE Monitor without touching it (no service, agent or `C:\ProgramData` changes):

```powershell
dotnet publish src/Prognode.Host -c Release -o C:\temp\prognode\build
$env:Prognode__DataRoot = "C:\temp\prognode\data"; $env:Prognode__LocalPort = "5100"; $env:Prognode__DiscoveryPort = "5101"; $env:Prognode__LanHttps__Enabled = "false"
C:\temp\prognode\build\Prognode.Host.exe    # then open http://localhost:5100
```

Without a license Core is view-only. Simulated PLCs for testing: [tools/protocol-soak](tools/protocol-soak/README.md).

See [AGENTS.md](AGENTS.md) for working rules, [SECURITY.md](SECURITY.md) for vulnerability reporting and
[docs/compliance/INTENDED_USE.md](docs/compliance/INTENDED_USE.md) for intended use.
