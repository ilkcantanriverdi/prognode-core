# PROGNODE Client Auto Discovery — V0.7.2 DEV3

## Goal
The operator never types an IP address. The client remembers a stable `ServerId`, not a DHCP address.

## LAN discovery
1. PROGNODE Server owns a persistent `ServerId` in `data/server-access.json`.
2. Windows/Android Client broadcasts UDP `PROGNODE_DISCOVER_V1` to port `5081`.
3. Each PROGNODE Server replies with `ServerId`, display name, API version and API port.
4. The client resolves the remembered `ServerId` to the current LAN address. If DHCP changes the server IP, no reconfiguration is required.

## Pairing and API security
Discovery never broadcasts an access token.

First connection:
1. Client discovers the server.
2. Local Server UI shows the rotating 6-digit pairing code from `/api/client/pairing-code`.
3. Client posts `ServerId + client name + pairing code` to `/api/client/pair`.
4. Server returns a random long-lived client token.
5. Client stores `ServerId + token` securely.
6. Future API calls use `Authorization: Bearer <token>`.

All non-loopback `/api/*` calls require a valid paired-client token, except discovery identity and the pairing endpoint.

## Local-first boundary
the **PROGNODE Account Cloud** does NOT store Devices, Tags, Alarms, Historian configuration, or process data.

Account Portal remains identity/license/activation only. LAN clients connect directly to the customer-owned PROGNODE Server.

## Remote access
UDP discovery is LAN-only and intentionally does not cross routers. Remote Android/Windows access later uses Cloud Push/Relay or customer VPN. The client UI can keep the same `ServerId`; only transport resolution changes.
