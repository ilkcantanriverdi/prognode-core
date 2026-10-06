# PROGNODE Android Parallel Track

## Product behavior

PROGNODE Android should start as a parallel workstream while the desktop/Core product continues to mature.

Two alarm delivery modes are planned:

1. **Normal mobile notification** — one high-priority visible notification for ACTIVE and optional CLEARED events.
2. **Require Mobile ACK** — a per-alarm option. When enabled, an ACTIVE occurrence remains in a mobile escalation state until the operator explicitly acknowledges that occurrence.

The important distinction is that `Notify ACTIVE` and `Require Mobile ACK` are separate behaviors.

## Proposed end-to-end flow

`Alarm Engine -> Mobile Push Queue -> optional Cloud Push relay -> FCM -> PROGNODE Android -> ACK -> Core occurrence`

Each mobile alarm payload must contain a stable `alarmOccurrenceId`; acknowledgment must target the occurrence, not only the alarm definition.

## Android behavior for Require Mobile ACK

- Use a dedicated high-importance alarm notification channel.
- Post a persistent/ongoing notification with Device + Alarm Text + Priority.
- Include an explicit **ACKNOWLEDGE** action.
- Keep the occurrence visibly active until ACK or CLEARED according to product policy.
- If not acknowledged, Core/cloud relay can re-send the visible high-priority event on a configurable escalation cadence.
- Store all delivery/ACK events in PROGNODE Notification Center.

## Important Android platform constraint

Do not design PROGNODE around unrestricted full-screen call-like alerts. Newer Android versions restrict full-screen intents primarily to calling/alarm-provider use cases, and users retain control over notification channel sound/importance. PROGNODE should therefore use a high-importance alarm channel + persistent notification + repeated server-side escalation for unacknowledged critical events.

## Connectivity

- **Same LAN:** Android may communicate directly with the local PROGNODE Core over HTTPS/LAN once authentication is implemented.
- **Remote / internet:** use the planned optional PROGNODE Cloud Push relay. Process data remains local; only notification metadata and ACK routing are relayed.

## Suggested first Android milestone

- Android app shell
- Site registration / pairing
- FCM token registration
- Active alarm list
- Notification channels: Normal / Critical ACK
- ACK action from notification and app
- Delivery/ACK status shown in Core Notification Center

