# PROGNODE V0.7.2 RC4 — Remote Access Core Contract

## Frozen product rules

`REMOTE_ACCESS` is an optional paid add-on. It is **not** required for local PROGNODE runtime.

- LAN use never consumes a Remote Access seat.
- A phone, tablet or Windows Client consumes **one** seat only after Remote Access is explicitly enabled for that paired device.
- One device identity = one remote client registration. The same device coming back to LAN does not consume another seat.
- Remote Access is bound to one stable PROGNODE `ServerId`.
- Seat authority is PROGNODE Cloud. The signed `.pgnlicense` is the local upper-bound entitlement, not the authoritative live seat counter.
- Remote failures, expiry, seat exhaustion or cloud outage must never stop PLC polling, alarms, Historian, local login, LAN notifications or LAN client access.
- Core is the final authority for alarm ACK. Cloud only relays authenticated commands.

## Signed license extension

RC4 accepts the following optional signed claim inside `payload.entitlements`:

```json
"remoteAccess": {
  "enabled": true,
  "maxClients": 10,
  "unlimitedClients": false
}
```

`maxClients` may also be `null` or the string `"unlimited"` when the Web generator deliberately issues an unlimited plan. Existing v2 licenses that do not contain `remoteAccess` remain valid and are treated as `REMOTE_ACCESS` not purchased.

The claim is parsed only after the normal Ed25519 envelope verification path. It does not participate in offline password validation.

## Local Core state

Remote state is cached under:

`data/remote-access/`

Files:
- `state.json` — server binding/cloud status/client snapshot
- `notification-outbox.json` — durable remote notification delivery queue
- `ack-audit.jsonl` — processed remote ACK command audit/replay guard

The cache never grants more capacity than the signed local license. Cloud can reduce effective capacity/status; Core does not elevate a cloud response above the signed claim.

## Device identity

LAN pairing remains separate and free.

A client that wants Remote Access must also provide a stable cryptographic public key during pairing. Current implementations:

- Flutter Mobile: Ed25519 key pair, private key in secure storage.
- Windows Client Preview: ECDSA P-256 key pair, persistent local private key.

Remote registration is refused if the paired client has no device public key. Old paired clients can continue LAN use but must be re-paired before Remote Access can be enabled.

## Core local API

Read:

```text
GET /api/remote-access/status
GET /api/remote-access/clients
GET /api/remote-access/ack-audit   (loopback only)
```

Admin actions:

```text
POST /api/remote-access/bind       (loopback only + signed-in local session)
POST /api/remote-access/sync       (loopback only + signed-in local session)
```

Client actions:

```text
POST   /api/remote-access/clients/register
DELETE /api/remote-access/clients/{localClientId}
```

For a non-loopback paired client, Core forces registration/revoke to that caller's own paired `ClientId`; it cannot register or revoke another paired client. A non-loopback client sees only its own Remote Access client row. Core Settings on loopback can list/manage all paired clients.

## Cloud configuration

Until the Web backend is deployed, `Prognode:RemoteAccess:BaseUrl` is intentionally empty. With an empty URL, no Remote Access cloud calls are attempted.

Expected configuration:

```json
"RemoteAccess": {
  "BaseUrl": "https://<production-api-host>",
  "StatusPath": "/api/v1/remote-access/status",
  "BindServerPath": "/api/v1/remote-access/bind-server",
  "RegisterClientPath": "/api/v1/remote-access/register-client",
  "RevokeClientPath": "/api/v1/remote-access/revoke-client",
  "PublishNotificationPath": "/api/v1/remote-access/notifications",
  "CommandsPath": "/api/v1/remote-access/commands",
  "CompleteCommandPath": "/api/v1/remote-access/commands/complete"
}
```

Production URL must be HTTPS. Loopback HTTP is accepted only for local development/testing.

## Cloud contract currently expected by Core

The Web team can align its backend to this shape or return the final contract so the Core adapter can be adjusted before release.

### Bind server

`POST {BindServerPath}`

```json
{
  "licenseId": "PGN-...",
  "signedLicenseDocument": "{ complete signed .pgnlicense JSON }",
  "serverId": "uuid",
  "serverName": "PROGNODE-XXXXXX"
}
```

Success response:

```json
{
  "success": true,
  "code": "BOUND",
  "message": "...",
  "serverAccessToken": "opaque-server-credential",
  "status": { /* status object below */ }
}
```

Cloud must independently verify the signed license, subscription, ServerId binding and product entitlement before issuing the server credential.

### Status

`GET {StatusPath}?serverId={serverId}` with `Authorization: Bearer {serverAccessToken}`.

```json
{
  "enabled": true,
  "serverBound": true,
  "bindingStatus": "BOUND",
  "subscriptionStatus": "ACTIVE",
  "unlimitedClients": false,
  "maxClients": 10,
  "usedClients": 2,
  "expiresAtUtc": "2027-09-19T00:00:00Z",
  "clients": [
    {
      "remoteClientId": "uuid",
      "localClientId": "uuid",
      "deviceName": "Galaxy S25",
      "platform": "Android",
      "userId": "...",
      "userDisplayName": "...",
      "devicePublicKey": "...",
      "status": "ACTIVE",
      "registeredAtUtc": "...",
      "lastSeenAtUtc": "..."
    }
  ]
}
```

### Register remote client

`POST {RegisterClientPath}` + server Bearer credential:

```json
{
  "serverId": "uuid",
  "localClientId": "uuid",
  "deviceName": "Galaxy S25",
  "platform": "Android",
  "devicePublicKey": "base64url-public-key",
  "userId": "signed-license-user-id",
  "userDisplayName": "Signed License User"
}
```

Success returns `remoteClientId` plus an updated status. Seat exhaustion must return a failed operation and must not allocate a client.

### Revoke remote client

`POST {RevokeClientPath}` + server Bearer credential:

```json
{
  "serverId": "uuid",
  "remoteClientId": "uuid"
}
```

On success Cloud releases the seat. Core then clears only the remote registration; LAN pairing remains intact.
Revocation is deliberately allowed as cleanup even if a refreshed local license no longer contains `REMOTE_ACCESS`, provided the cloud endpoint/server credential still exists. This prevents orphaned paid seats.

### Publish alarm notification

`POST {PublishNotificationPath}` + server Bearer credential:

```json
{
  "serverId": "uuid",
  "eventId": 1789806761291001,
  "severity": "Critical",
  "title": "Communication",
  "message": "plc1 • Communication lost",
  "timestamp": "...",
  "alarmKey": "...",
  "requiresAcknowledgement": true,
  "repeatSequence": 3
}
```

Core writes the event to a durable outbox before remote delivery. Failed cloud delivery is retried without impacting local alarm processing.

### Remote commands / ACK

`GET {CommandsPath}?serverId={serverId}` + server Bearer credential.

Expected ACK command:

```json
{
  "commandId": "opaque-unique-id",
  "type": "ACK",
  "alarmKey": "alarm-activation-key",
  "userId": "...",
  "userDisplayName": "...",
  "remoteClientId": "uuid",
  "issuedAtUtc": "..."
}
```

Core rejects unsupported, missing, stale or unauthorized commands, performs the ACK locally through `AlarmService`, writes an audit record and reports completion:

`POST {CompleteCommandPath}`

```json
{
  "serverId": "uuid",
  "commandId": "...",
  "success": true,
  "resultCode": "ACKNOWLEDGED"
}
```

RC4 authorization is intentionally fail-closed: the command `userId` must equal the user assigned in the currently verified signed license. This is temporary until the separate industrial runtime role/user model is implemented.

## Remote client counting examples

```text
Galaxy S25      LAN only     -> 0 seats
Galaxy S25      Remote       -> 1 seat
Betül iPhone    Remote       -> 1 seat
Office PC       LAN only     -> 0 seats
Office PC       Remote       -> 1 seat
```

For a 10-client plan, the Cloud must reject the 11th active remote identity. Revoking one active identity immediately returns the count from `10/10` to `9/10`.

## Production acceptance sequence

1. Web issues a newly signed license containing `remoteAccess.enabled=true` and `maxClients=10`.
2. Set the production Remote Access BaseUrl in Core configuration.
3. Import license and sign in locally.
4. Core Settings -> Bind Remote Access. Cloud binds the subscription to the stable ServerId and returns the server credential.
5. Pair Galaxy S25 with its stable public key; LAN still consumes 0 seats.
6. Enable Remote Access for S25 -> Cloud becomes `1/10`.
7. Pair Windows Client; enable Remote -> `2/10`.
8. Revoke one -> `1/10`, LAN pairing remains.
9. Leave LAN and verify FCM/APNs remote notification delivery.
10. Trigger a `Repeat Until Acknowledged` critical alarm and ACK remotely; verify repetition stops only after Core ACK and audit is written.
