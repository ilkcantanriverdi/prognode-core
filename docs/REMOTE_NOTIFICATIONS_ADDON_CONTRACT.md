> **SUPERSEDED BY `docs/RC4_REMOTE_ACCESS_CORE_CONTRACT.md`.** The product name is now `REMOTE_ACCESS`, covering mobile notification/ACK and Windows Client remote connectivity under one shared client-capacity model.

# PROGNODE Remote Notifications — product/architecture contract

Product add-on name: `REMOTE_NOTIFICATIONS`.

## Commercial boundary

- Local/LAN mobile alarm use is included with an alarm-enabled PROGNODE license.
- Out-of-LAN push delivery is a separately purchasable add-on.
- A Remote Notifications expiry must not stop Core, PLC communication, local alarms, historian, local login, or LAN mobile operation.

## Architecture

Core keeps the authoritative alarm state on-site.

Remote path:

`PROGNODE Core -> outbound encrypted relay connection -> PROGNODE Cloud Relay -> FCM/APNs -> PROGNODE Mobile`

No inbound router port-forward is required.

The relay receives only the minimum notification/command metadata required for delivery. Historian samples, PLC register maps, project databases and process datasets remain on-site.

Remote ACK path:

`Mobile -> authenticated relay command -> Core -> authorization -> Alarm Engine -> ACK result -> Mobile`

Cloud does not independently change alarm state.

## License implementation note

The current signed `.pgnlicense` v2 contract is frozen and must not be silently changed Core-side. The web/account license generator and Core verifier must introduce the Remote Notifications entitlement together in a coordinated contract revision/compatible extension. Until that web-side contract is implemented, Remote Notifications remains architecturally reserved and disabled.
