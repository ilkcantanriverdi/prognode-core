# PROGNODE compliance program

Working index for standards and regulations (assessment of 11 October 2026). Legal deadlines and scope
must be confirmed by counsel; this folder is engineering documentation, not legal advice.

| Area | Document / control | Status |
|---|---|---|
| Vulnerability disclosure (CRA Annex I part II, IEC 62443-4-1 DM) | [SECURITY.md](../../SECURITY.md) | Draft, needs security@prognode.io |
| Incident and vulnerability reporting (CRA Art. 14, Law 7545, KVKK/GDPR) | [VULNERABILITY_AND_INCIDENT_HANDLING.md](../security/VULNERABILITY_AND_INCIDENT_HANDLING.md) | Draft, roles to be named |
| Intended use (IEC 61508/61511, IEC 61131-3 statements) | [INTENDED_USE.md](INTENDED_USE.md), installer EULA | Done |
| SBOM (CycloneDX) per build | `.github/workflows/security.yml` | Done |
| Dependency vulnerability scan (OSV), secret scan, static analysis | `.github/workflows/security.yml` | Done |
| Personal data inventory (KVKK/GDPR) | prognode-cloud `docs/privacy/DATA_INVENTORY.md` | Draft |
| Threat model, IEC 62443-4-2 SL1 gap analysis | — | Planned (Dec 2026) |
| Security event log, MFA, session timeout, role matrix | — | Planned (Dec 2026 – Feb 2027) |
| ISA-18.2 shelving, suppression, out of service, KPI reports | — | Planned (Dec 2026 – Feb 2027) |
| Secure installation guide (IEC 62443-3-3 zones and conduits) | — | Planned |
| Code signing of installers | `installer/build.ps1` (`PROGNODE_SIGN_CMD`) | Waiting for certificate |
| CRA technical documentation, Annex I mapping, EU declaration of conformity | — | Planned (2027) |
