# PROGNODE Mobile Team Handoff — RC6.4.6 Mobile Notification Foundation

**Core reference:** `PROGNODE_RC6_4_6_MOBILE_FOUNDATION.zip` / `0.7.2-rc6.4.6-mobile-foundation`.

**Contract status:** The Core/Flutter *source integration* below has been implemented and the SQLite v8→v9 DDL and transaction semantics tested independently. **Neither the Windows .NET solution nor the Flutter Android/iOS app has been compiled in the packaging environment. No APK or real remote push is claimed.** The mobile team must build and test on physical devices, starting with Android 16 / Samsung One UI 8.5.

## 1 — Agreed product and licensing contract (FINAL)

| Transport | License | Device seats | Behavior |
|---|---|---|---|
| LOCAL | Valid Alarm module | **0 paid remote seats** | Phone and Core on reachable LAN: pairing, alarm history, foreground notification, authorized ACK; internet not required. |
| REMOTE | **ANNUAL per Core/site** Remote Access/Notifications entitlement | **5 / 10 / 25** signed `maxClients` | Outside LAN: cloud relay, FCM Android / APNs iOS, authorized occurrence ACK once real relay is live. The paid pool is shared by remote phones/tablets/Windows clients. |
| DISCONNECTED | No new cloud decision possible | Unchanged | Timestamped cached events only; ACK must not show success until Core confirms. |

Commercial remote add-ons in newly issued licenses: `addons.remoteAccess.enabled=true`, `billingPeriod="YEARLY"`, `maxClients in [5,10,25]`, expiry under `payload.addons.remoteAccess.expiresAtUtc`; signed `entitlements.remoteAccess` must match `enabled/maxClients`. For backward compatibility, older signed monthly licenses can still be imported; new commercial licenses are annual. INTERNAL_QA manual licenses can have custom `maxTags` per existing license rules. **Never synthesize an unlimited tier.** Free LAN clients must not be registered as paid remote devices unless users explicitly enable remote service for that device.

## 2 — Core capabilities delivered in source

- SQLite `schema_version=9` adds durable `notification_events`, `notification_outbox`, `notification_receipts`, nullable `alarm_events.occurrence_id`. Migration is additive and retains legacy alarms, historian and Trend Studio.
- An alarm activation receives a new UUID `occurrenceId`; ACTIVE, REMINDER, CLEARED and ACK share the ID. A later activation has a different ID. System communication alarms also get an occurrence ID.
- For notifying alarms, `alarm_events` + mobile journal + eligible paid remote queue are committed in **one SQLite transaction**. Remote-outbox retries use capped exponential backoff; a legacy JSON outbox can migrate once. Ordinary non-alarm notification storage errors are visible via a health endpoint. **Do not advertise hard end-to-end delivery guarantees based on the outbox alone.**
- Relay acceptance = `RELAY_ACCEPTED`. It is *not* FCM/APNs delivery or Android notification display. Core stores `DEVICE_RECEIVED` and `USER_OPENED` receipts per local client ID where reported.
- Old Windows Agent `/api/notifications` and legacy `/api/alarms/ack` retained for compatibility. **Mobile must use v2 occurrence-specific ACK only.**
- Plain HTTP binds only to loopback port 5080. Secure LAN mobile API listens on 5443 **only after enabling certificate-based HTTPS**. UDP discovery stays on 5081. Device verifies all 64 SHA256 certificate fingerprint digits independently from the physical Core machine (not from untrusted UDP). Remote cloud uses HTTPS with normal certificate validation.
- Existing RC6.4.5 Trend Studio native UI preserved; no duplicate mobile app shell on Core.

## 3 — Canonical v2 HTTP endpoints

LAN origin: `https://<core-lan-ip>:5443` after enabling LAN HTTPS. All non-public LAN endpoints require `Authorization: Bearer <pairedClientToken>`. ACK and push-token registration also require `X-PROGNODE-Session: <locally signed-in user session>`.

| Verb and endpoint | Purpose / status |
|---|---|
| `GET /api/server/identity` | Discovery confirmation (do not trust TLS fingerprint solely from discovery). |
| `GET /api/client/pairing-code` | **Core localhost only**: 6-digit one-time code in Settings. |
| `POST /api/client/pair` | Pair with server ID, code, client name, platform, device public key. Use pinned HTTPS. |
| `POST /api/access/login` | Email/password from signed Core license; obtain `token` and store securely. |
| `GET /api/mobile/v2/notifications?cursor=0&limit=100` | Response `{schemaVersion:2,serverId,items,hasMore,nextCursor,storageHealthy}`. Items are `NotificationEvent`: `id`, `timestamp`, `occurrenceId`, `eventType`, `repeatSequence`, `severity`, `title`, `message`, `alarmKey`, `requiresAcknowledgement`, `sourceName`, `activeAtUtc`, `clearedAtUtc`. Cursor is the last **persisted** event `id`, not a wall-clock timestamp. |
| `GET /api/mobile/v2/alarms/active` | Response `{schemaVersion:2,items:[AlarmRuntimeSnapshot...]}` with `occurrenceId` and ACK state. |
| `POST /api/mobile/v2/alarms/{occurrenceId}/ack` | **Authoritative ACK**: 200 `{acknowledged:true,occurrenceId}`; 409 `{code:"STALE_OCCURRENCE"}` on old known activation; 404 `OCCURRENCE_NOT_FOUND`; 401 when login required. ACK doesn't clear process condition. Repeating ACK for same acknowledged occurrence is idempotent. |
| `POST /api/mobile/v2/notifications/receipt` | Body `{eventId:123,state:"DEVICE_RECEIVED"}` or `"USER_OPENED"`; idempotent per client, opened state wins. |
| `POST /api/mobile/v2/devices/push-token` | Body `{platform:"ANDROID_FCM"|"IOS_APNS",pushToken:"..."}`; requires paid registered remote client, valid user session, and deployed cloud relay. Core proxies token securely; local SQLite placeholder table does not store raw provider tokens. |
| `GET /api/mobile/v2/notifications/health` | **Core localhost + session only**, for storage/outbox diagnostics. |

Event stream or push delivery over LAN while mobile app is foreground currently uses four-second cursor poll. SSE/WebSocket remains optional future optimization, **not** an implemented endpoint.

## 4 — Flutter source included and mobile team's next work

Keep existing `mobile/Prognode.Mobile` Flutter/Dart project. Present source changes include: explicit SHA256 HTTPS pin acceptance, secure pairing persistence, v2 cursor reader with paged recovery, local notifications, per-occurrence ACK, best-effort receipts and remote status UI. Current project has no generated `android/` or `ios/` platform folders and **cannot ship as an APK/IPA as-is**. On development workstation: `flutter create --platforms=android,ios .`, `flutter pub get`, add/apply Android 16 notification permissions and foreground/background policy, configure iOS when macOS/Xcode available.

**Must complete in mobile implementation:**

1. Android actual notification channel, notification permission (`POST_NOTIFICATIONS`), background/locked-screen tests on Android 16 / One UI 8.5, vendor battery restriction tests; if using foreground service obey Android 12+ startup limitations and Android 15+ `dataSync` duration restrictions. Four-second Dart timer does **not** run reliably when app is terminated.
2. Android **FCM SDK** + valid Firebase project/app credentials; token registration & rotation through v2 endpoint when remote entitlement exists. iOS APNs and entitlements on macOS/Xcode/Apple Developer path. These external provider integrations are **not** in the current Core ZIP.
3. Durable receipt retry and dedup using `(serverId,eventId)`; retries for `occurrenceId + repeatSequence`, persistent cursor *only after* local ingestion. On reconnect fetch pages until `hasMore=false`. Prevent a flood of delayed toasts, but show old events in history.
4. Client authorization UX: never mark an ACK success while offline; show `PENDING`/`FAILED`. Handle 409 by refreshing the current occurrence. Use exact source/tag/priority and local event timestamps; no hidden downgrade to legacy alarmKey ACK.
5. Certificate renewal migration: explicit re-verification on changed fingerprint, not accepting silently from discovery. Android cleartext HTTP remains disallowed on LAN in release build.
6. Test lifecycle (app open, minimized, locked screen, Wi-Fi→4G, Core restarts, airplane mode, notification permission disabled, Doze).

**iOS product boundary:** fully offline LAN + suspended/terminated app cannot guarantee immediate notifications. Remote reliable background path requires internet and APNs; Android battery/OS likewise precludes an absolute delivery guarantee. PROGNODE mobile alerts do not replace plant safety PLCs or sirens.

## 5 — Cloud relay dependency (Web team)

Previous connectivity test: `prognode.io`, `account.prognode.io` and `control.prognode.io` failed trusted HTTPS validation on user's Windows host, while `https://prognode-control.vercel.app/api/core/activate` returned 200 **HTML `/_not-found`**, not activation JSON. **Do not configure Core or promise remote push until actual JSON activation/heartbeat and remote relay APIs are deployed and verified.**

A separate `WEB_CLOUD_RELAY_HANDOFF_RC6_4_6.md` in this package contains provider contract and deployment acceptance. `RemoteAccess.BaseUrl` is empty by default; safe local-only behavior is intentional. Never disable SSL verification. FCM `PUSH_PROVIDER_ACCEPTED` is only reportable after the actual provider accepts the push; Core can currently prove `RELAY_ACCEPTED` and mobile receipts only.

## 6 — Physical-device acceptance matrix

1. Windows: build entire solution with .NET 10 SDK: `dotnet build PROGNODE.sln -c Debug`. Check health coreVersion `0.7.2-rc6.4.6-mobile-foundation`; old UI/trend and license still work. 
2. Core: `ENABLE_PROGNODE_LAN_HTTPS.ps1` as Administrator once; copy certificate SHA256 from Core console and verify on phone independent of UDP. Pair Android on same private Wi-Fi, sign in using signed local account.
3. Trigger actual Modbus alarm from Test Bench: ACTIVE→ notification→ correct `occurrenceId`→ ACK→ history. Test CLEAR and re-ACTIVE; tapping old notification **must** get 409 and never ACK newer alarm.
4. Kill/restart Core after persisted alarm; re-pairing shouldn't be necessary; cursor catches missed event exactly once. Verify v8→v9 migration on a backed-up copy of the existing database before production deployment.
5. Disconnect factory internet but keep LAN: alarm/ACK still function while app foreground; free LAN seat consumption remains zero.
6. Android background/lock/battery test separately and record actual delivery latency and failures; measure rather than infer from foreground poll.
7. After Web team proves production HTTPS/JSON API and FCM integration, enable remote annual 5/10/25 license, register one device (uses one paid seat), swap to mobile data, verify receipt, occurrence ACK and cloud audit. Non-paying or expired remote must stop without breaking LAN.
8. Cloud down while local alarm active: outbox remains queued for entitled/bound site and retries; no fake `DEVICE_RECEIVED` claim.

**Handoff invariant:** Keep signed Core license, current historian, Trend Studio and customer configuration intact. Never inject fake values into the production Core; use external Modbus simulator. No raw customer credentials, private Ed25519 signing keys, FCM service keys, APNs keys or real customer license files belong in the source ZIP.
