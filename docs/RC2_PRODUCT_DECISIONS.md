# PROGNODE V0.7.2 RC2 — Product decisions

## Protocol catalog
Historical RC2 decision (superseded by the four-protocol Core catalog in
`docs/PROTOCOL_CONNECTORS_V1.md`):

Customer-facing target catalog was fixed to four protocols:
- Modbus TCP — current live runtime
- PROFINET — next connector track
- OPC UA — planned
- MQTT — planned

Modbus RTU and the generic Siemens S7 card are removed from the customer protocol catalog.

## License UX
License page is subscription-focused rather than capacity-focused. Unlimited device/tag/alarm counters are intentionally hidden because current commercial licensing is not capacity-based.

Customer sees:
- product / plan
- exact expiry date
- remaining time
- licensed account + organization
- enabled modules
- Buy / Renew action
- refreshed `.pgnlicense` import

## Client validation
`tools/Prognode.Client.Windows.Preview` validates UDP discovery, one-time pairing and authenticated LAN API access before the final Windows Client UX is built.

## Mobile notification next test
Use the Galaxy S25 on the same LAN first. Validate paired mobile notification delivery locally before introducing FCM/cloud relay. See `docs/S25_MOBILE_NOTIFICATION_MVP.md`.
