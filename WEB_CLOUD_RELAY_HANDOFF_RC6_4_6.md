# PROGNODE Web / Cloud Relay Handoff — RC6.4.6

**Scope:** Server-side work required before PROGNODE Mobile REMOTE 4G/5G push can be called production-ready. Core source now queues notification events in SQLite, retries and requires an explicit JSON relay acceptance; it cannot deploy a Firebase project, APNs signing assets or Vercel endpoints itself.

## Licensing decision (FINAL)

- Remote Access / Remote Notifications is **annual per Core/site**. Signed paid device-count tiers are **5, 10, 25**. The paid remote pool is shared by remote Android/iOS/tablet/Windows clients; LAN devices on the local network use **zero paid seats**.
- Signer must emit `payload.addons.remoteAccess.billingPeriod:"YEARLY"`, `maxClients:5|10|25`, `expiresAtUtc`; `payload.entitlements.remoteAccess.enabled/maxClients` match. A separate remote expiry is allowed. New commercial Core verifier enforces yearly; old signed monthly licenses remain readable to avoid breaking past customers. Internal QA manual licenses may have test durations. No unlimited commercial tier.

## Blocker: real production API host and certificates

On 2026-09-26 production test, default `prognode.io`, `account.prognode.io`, `control.prognode.io` each raised Windows `SEC_E_UNTRUSTED_ROOT`; Vercel Control Center POST `https://prognode-control.vercel.app/api/core/activate` returned `HTTP 200 text/html` + `X-Matched-Path:/_not-found`. It is **not an activation endpoint**. Locate/deploy the actual API and validate with `curl` returning `Content-Type:application/json` and a meaningful status for missing JSON body; install correct public TLS chain at production domains. Do not ask Core developers to disable certificate verification.

## Production endpoint contract (must be implemented/tested)

1. `/api/core/activate`, `/api/core/heartbeat`: accept existing signed v2 license/Core identity; persist installation as active with lastSeen and show it in Control Center `Active Core`/Customer Portal `Installations`. For revoked licenses return **HTTP 200 JSON** with `licenseStatus:"REVOKED"` (and licenseId/revokedAtUtc), not 401/403 that prevents Core learning about revocation. Maintain genuine signature verification and replay controls.
2. `GET /api/v1/remote-access/status?serverId=...`: server bearer token, registration/binding/entitlement/remaining seats/expiry. Cloud seats **cannot exceed signed max**; Core also takes the min of cloud/signed quota. Preserve old field names expected by `RemoteAccessCloudStatusResponse` (`enabled`, `serverBound`, `subscriptionStatus`, `maxClients`, `usedClients`, `expiresAtUtc`, `clients`).
3. `POST /api/v1/remote-access/bind-server`: signed license, server UUID/name; idempotent binding; return access token as existing `RemoteAccessCloudOperationResponse`. Reject invalid/revoked/expired remote subscriptions. Core initiates outbound TLS; no site inbound port.
4. `POST /api/v1/remote-access/register-client` and `/revoke-client`: verify server token, seat quota and unique `(serverId,localClientId)`; assign remoteClientId. Revocation immediately invalidates remote device command and push token.
5. `POST /api/v1/remote-access/push-token`: server bearer authentication plus bound `serverId/localClientId/remoteClientId`, `platform`, `pushToken`. Store token encrypted or within trusted provider backend, handle renewal/deactivation; never place provider tokens or user passwords into telemetry. Reject non-entitled clients.
6. `POST /api/v1/remote-access/notifications`: Core sends event metadata (`serverId`, `eventId`, `occurrenceId`, `eventType`, `priority/severity`, `repeatSequence`, source/title/message, minimal created/active/clear timestamps). **Idempotency unique `(serverId,eventId)`**. **Return JSON `{ "accepted": true, "eventId": <same numeric ID> }`** only after relay safely records event; a random HTML 200 must never count as success. Downstream FCM/APNs send is independent and retriable.
7. `GET /api/v1/remote-access/commands` and `POST /api/v1/remote-access/commands/complete`: remote ACK commands include **required `occurrenceId`** alongside commandId/alarmKey/user/device/timestamp; Core performs exact occurrence conditional ACK and reports success/stale/failed. Provider must authenticate user and remote client, prevent replay/forged commands, audit who ACKed and when. Cloud is not process alarm authority.
8. Delivery pipeline state distinction: `QUEUED` (Core SQLite) → `RELAY_ACCEPTED` (cloud durable record) → `PUSH_PROVIDER_ACCEPTED` (actual FCM/APNs provider) → `DEVICE_RECEIVED` (mobile receipt where available) → `USER_OPENED` → `ACKNOWLEDGED` (Core) or `FAILED`. Do not show "delivered" for mere FCM/API acceptance; clearly show stale/missing receipt and last seen in Control Center and Core when telemetry channel is added.
9. Prefer app's outbound socket/webhook command delivery without inbound customer firewall changes. Cloud storage only minimum event metadata; PLC raw historian and tag stream **remain on premises**.

## Deployment gates

- Test invalid JSON endpoint, real active signed license, installation count after activation, REVOKED propagation on the next heartbeat, and normal offline Core when cloud fails.
- Register Android FCM token with actual Firebase project + server credentials and provider response; iOS APNs with Apple developer signing/entitlements. Verify phone in mobile data, foreground/background/locked, duplicate event processing and old-occurrence ACK 409.
- Publish a versioned production BaseUrl and genuine sample JSON for Core developer integration. If FCM/APNs, Vercel runtime credentials, or production domain access are unavailable, state exactly which external prerequisite blocks remote testing.

This document describes **required Web work**; its existence is not evidence those services are deployed.
