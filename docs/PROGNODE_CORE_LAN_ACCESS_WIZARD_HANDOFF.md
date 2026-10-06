# PROGNODE Core — LAN Mobile Access: One-click Setup Handoff

**Scope:** Core team; next Core release after the currently tested RC6.4.6 mobile foundation. Preserves secure QR pairing, HTTPS, v2 occurrence-specific ACK, LOCAL free-of-remote-seats, and REMOTE entitlement semantics. No Mobile APK change is required merely to fix the Windows firewall profile mismatch.

## Problem reproduced
- Windows PC Wi-Fi `192.168.1.52` has **Public** network profile; Tailscale interface is **Private**.
- PROGNODE HTTPS listens on TCP `5443` and PC-to-PC connection succeeds.
- Existing firewall allow rule `PROGNODE LAN HTTPS` is limited to **Private**, so Samsung at `192.168.1.61` could not reach `https://192.168.1.52:5443/api/server/identity` on the **Public** Wi-Fi interface.
- Requiring customers to run PowerShell, switch their network to Private, or manually paste a certificate fingerprint is unsuitable for end-user onboarding.

## Required Core UX
Add **Settings → Mobile Access → Enable LAN Access** (admin-only, opt-in). A one-time guided flow:
1. Show detected NICs with name, network profile, IPv4/IPv6, CIDR and reachability status. Let administrator select the intended plant/LAN interface; never default to every interface or Tailscale.
2. Offer firewall scope: **selected device IPs** or **approved LAN subnet**. For Public interfaces, display clear security warning and require explicit confirmation. Show exact source range, selected interface and TCP `5443` before applying; customer must not be asked to change Windows Public/Private profile.
3. Trigger **local elevated Windows setup helper** via the installed Core tray/setup program, with Windows UAC consent/admin credentials once; never grant admin rights to arbitrary incoming web/API requests. Helper is signed where possible and takes validated, allowlisted parameters only.
4. Idempotently create/update a named, managed inbound Windows Defender Firewall rule: `PROGNODE LAN HTTPS — <selected NIC>` with TCP `5443`, selected interface, correct profile (Public or Private), `RemoteAddress` limited to approved device addresses/subnet, no edge traversal, no WAN/port forwarding. Optionally create a separate similarly scoped UDP `5081` discovery rule only if discovery is enabled; QR flow must work without UDP discovery.
5. Verify HTTPS locally and provide test instructions/status for mobile reachability; do not infer phone reachability solely from `localhost` success. Generate QR containing actual reachable LAN address, server ID, SHA256 fingerprint and short-lived single-use pairing ticket as defined by QR contract. Let customer test phone access and finish QR pairing. Do not silently accept fingerprint changes.
6. Display persistent state: `LAN access enabled`, selected network/scope, firewall rule health, HTTPS cert health, last phone connection, and diagnostic for guest VLAN/client isolation. Provide `Disable LAN access`, interface/scope edit and `Repair firewall rule` (admin-only), and remove app-owned rules on uninstall.

## Security boundaries
- Keep HTTPS certificate pinning and short-lived, one-time QR pairing ticket; firewall permission is not a replacement for app authentication, authorization, or v2 occurrence ACK checks.
- Never create blanket `Any` remote address rules across all Public adapters or enable WAN/router port forwarding; if a customer approves a whole subnet, explain that any host in that subnet may reach the TLS port until authenticated. Prefer a dedicated plant VLAN/subnet if guest devices share the segment.
- Never silently elevate the browser or allow a remotely authenticated web user to directly trigger privileged firewall changes. Installation/elevation is initiated and confirmed at the Core host by local administrator or authorized IT deployment policy.
- Enterprise GPO/MDM firewall enforcement, VLAN routing or AP client isolation cannot be overridden by the app; present exact port, selected subnet and a ready-to-share IT request when the rule is denied or phone remains unreachable.
- Existing RC6.4.6 Private rule must be migrated/updated safely without broadening unrelated firewall rules, preserving current configuration, license, historian and Trend Studio.

## Suggested status/API contract
- `GET /api/mobile-access/network-status` (admin session): interfaces, chosen interface, network profile, local IPs/CIDRs, HTTPS/listener/cert fingerprint, owned firewall rules, effective diagnostic status. No elevation.
- `POST /api/mobile-access/prepare` (localhost admin session): validates intended interface, port, scope and returns explicit preview plus short-lived local admin action token; no unprivileged firewall mutation.
- `POST /api/mobile-access/verify` (admin): recheck service and effective firewall configuration; report local-only vs actual phone-reported probe separately.
- Keep existing QR and mobile v2 API contract unchanged; UI/orchestration may use a local setup helper rather than exposing a new network mutation endpoint.

## Acceptance tests
1. Public Wi-Fi + mobile on same approved subnet: one-time admin consent -> only selected NIC's TCP 5443 inbound allowed -> Chrome reaches `/api/server/identity` -> QR pairs, local alarms and occurrence ACK work.
2. Private Ethernet: existing behavior preserved without redundant rule creation.
3. Public guest Wi-Fi / unapproved network: not opened implicitly; no blanket firewall rule; UI warns or requires another explicit admin decision.
4. Wi-Fi and Tailscale simultaneously: correct adapter/IP advertised in QR; Tailscale does not accidentally become the LAN pairing address.
5. Repeat setup is idempotent; disabling removes ONLY owned rules; uninstall cleanup verified.
6. Without admin rights or GPO permission: understandable error + IT instructions, no hidden security bypass.
7. No remote license: LAN pairs and acknowledges without consuming paid remote seats; cloud cannot be enabled without entitlement.
8. Phone on isolated SSID/VLAN: show network reachability fault and IT handoff; do not falsely label QR camera, auth, or certificate failure.

**Implementation state:** design/handoff only; do not claim the currently running Core already offers this one-click UI.
