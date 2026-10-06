# PROGNODE V0.7.2 RC3 — Mobile ACK + Flutter

## Frozen mobile alarm policy

PROGNODE now has two explicit notification modes on alarm definitions:

1. `NotifyOnce`
   - one notification on ACTIVE
   - no reminder loop

2. `RepeatUntilAcknowledged`
   - first notification on ACTIVE
   - repeat notifications at the configured interval while the alarm remains unacknowledged
   - ACK changes the runtime state to Acknowledged; ACK never clears the process condition
   - optional `ContinueAfterClearUntilAcknowledged` keeps reminders alive after the process condition clears, so short critical events cannot silently disappear before a person sees them

Repeat interval is clamped by Core to 15 seconds..24 hours. UI presets are 30 s / 1 / 2 / 5 / 10 min.

## ACK security path

A mobile device must satisfy both boundaries:

- paired-client Bearer token (device trust)
- valid offline local account session (`X-PROGNODE-Session`) for mutating actions

Therefore a paired phone can read notification events, but it cannot ACK until the licensed account signs in locally. Core remains the authority: the phone never locally marks an ACK as successful before Core accepts it.

## Notification event metadata

Notification events now expose:

- `alarmKey`
- `requiresAcknowledgement`
- `repeatSequence`

This lets Android/iOS render an ACK action against the exact Core alarm and identify repeated reminders.

## SQLite migration

Alarm definitions gain:

- `notification_mode`
- `repeat_interval_seconds`
- `continue_after_clear_until_ack`

Existing alarm definitions migrate to safe defaults: `NotifyOnce`, 60 seconds, continue-after-clear disabled.

## Flutter mobile client

`mobile/Prognode.Mobile` is one Dart/Flutter codebase for Android and iOS.

LOCAL milestone:

`UDP discovery -> Pair -> secure token -> notification polling -> native notification -> account sign-in -> ACK -> Core`

The current client polls every 4 seconds while running. This is intentionally the LOCAL MVP. Production out-of-LAN delivery must use push (FCM/APNs) through the licensed Remote Notifications relay rather than depending on unrestricted background polling.
