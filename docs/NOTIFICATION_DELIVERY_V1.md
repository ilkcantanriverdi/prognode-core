# Core notification-delivery control v1

Core keeps alarm occurrences and `notification_events` even when delivery is off.
The local owner/admin controls delivery with a signed-in loopback session:

- `GET /api/notifications/delivery` (loopback): `{enabled,clients:[{clientId,name,platform,notificationsEnabled}]}`.
- `PUT /api/notifications/delivery` with `{enabled:boolean}`: globally pause/resume delivery.
- `PUT /api/notifications/delivery/clients/{clientId}` with `{enabled:boolean}`: pause/resume one paired LAN client.

`GET /api/mobile/v2/notifications` keeps schema version 2, adds `deliveryEnabled`,
and returns no items while that client's delivery is off. Its `nextCursor` still
advances over suppressed events: resuming does not replay them. Windows Agent
likewise advances its cursor without a toast while globally paused. Core does
not enqueue new remote relay rows while globally paused; existing queued rows
are marked `SUPPRESSED` on pause and will not be sent on resume.

App should display the `deliveryEnabled` state if helpful, but must not mistake
an empty suppressed page for a lost alarm history. Alarm APIs remain available.
No mobile request may change the global or another client's setting.

The cloud relay API currently fans out one event without a recipient list.
Therefore **per-client remote push suppression is not covered by this Core v1
contract**. Web/Control must add recipient filtering before claiming remote
per-client mute; do not infer that the Core LAN setting affects cloud fanout.
