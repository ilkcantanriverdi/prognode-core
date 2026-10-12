# Security policy

PROGNODE is developed by Cordara Labs. We take the security of PROGNODE Core, the PROGNODE Client
for Windows, the PROGNODE mobile app and the PROGNODE Cloud services (account.prognode.io) seriously
and welcome reports from customers, researchers and partners.

## Reporting a vulnerability

Email **security@prognode.io**. Please do not open a public issue, pull request or discussion for a
suspected vulnerability. On this repository you can also use GitHub's
**Security › Report a vulnerability** (private vulnerability reporting).

Please include:

- the product and version (PROGNODE Core shows it in the sidebar, for example `v1.0.0-beta.4`);
- a description of the issue and its impact;
- steps to reproduce, a proof of concept or logs, if available;
- whether the issue is known to be exploited;
- how we can reach you, and whether you want to be credited.

Do not include real process data, customer data or credentials in a report.

## What happens next

| Step | Target |
|---|---|
| Acknowledgement of your report | 2 business days |
| Initial assessment and severity (CVSS v4.0) | 5 business days |
| Fix or mitigation available — Critical | 14 days |
| Fix or mitigation available — High | 30 days |
| Fix or mitigation available — Medium / Low | Next planned release, at most 90 days |
| Public advisory | When a fix is available, at the latest 90 days after the report unless agreed otherwise |

We keep you informed during the process and agree the disclosure date with you. Actively exploited
vulnerabilities and severe incidents are reported to the competent authorities as required by law
(EU Cyber Resilience Act, Turkish Cyber Security Law No. 7545). The internal procedure is described in
[docs/security/VULNERABILITY_AND_INCIDENT_HANDLING.md](docs/security/VULNERABILITY_AND_INCIDENT_HANDLING.md).

## Supported versions

| Product | Supported with security updates |
|---|---|
| PROGNODE Core 1.x | The latest release. Each major version receives security updates for at least 5 years after its first release. |
| PROGNODE Client for Windows | The latest release. |
| PROGNODE mobile app | The latest version in the App Store and Google Play. |
| PROGNODE Cloud (account.prognode.io) | The running service; fixes are deployed centrally. |

Core and the Windows Client check for updates and offer to install them; installers are verified with
their published SHA-256 checksum before installation.

## Scope

In scope: PROGNODE Core and its local and LAN interfaces, the tray agent, the PROGNODE Client for
Windows, the PROGNODE mobile app, account.prognode.io, the license and activation services and the
Remote Access relay.

Out of scope: denial of service through traffic volume, social engineering of Cordara Labs staff,
physical attacks, issues that require a compromised administrator account on the Core PC, and
vulnerabilities in third-party services we do not operate (for example Paddle, Google Firebase or
Apple Push Notification service) — please report those to the respective vendor.

## Good-faith research

We will not pursue legal action against research that is done in good faith, stays within this
policy, avoids privacy violations and service disruption, does not access or modify other customers'
data and does not test against industrial equipment you are not authorised to operate. Use your own
installation or a test environment.

## Security design summary

- Industrial connectors (Modbus TCP, Siemens S7, OPC UA, MQTT) are read-only; PROGNODE never writes
  to devices.
- The web interface listens on localhost only; LAN clients use HTTPS with certificate pinning and a
  12-character check code at pairing.
- Licenses, activation certificates and offline credentials are signed with Ed25519 and verified on
  the Core PC; passwords are stored as Argon2id verifiers.
- Backups are encrypted with AES-256-GCM (PBKDF2-SHA256, 310,000 iterations).
- Remote Access relays alarm metadata only; process values stay on site.
- A software bill of materials (CycloneDX) is produced for every build.
