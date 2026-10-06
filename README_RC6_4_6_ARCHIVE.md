# PROGNODE RC6.4.6 — Mobile Notification Foundation

**Deliverable:** Full RC6.4.5 HF2 source plus additive Core/Flutter source changes. Core, Host, Client, Agent, Historian, licensing and Trend Studio sources remain in this package. New version marker: `0.7.2-rc6.4.6-mobile-foundation`.

## What's included

1. SQLite v9 persistent notifications with alarm occurrence IDs, atomic alarm-event/notification/eligible remote-outbox transaction and retry; v2 cursor sync, receipt and conditional mobile ACK API. Old Windows Agent/API compatibility preserved.
2. Secure LAN HTTPS listener (5443) with independently verified certificate SHA256; unencrypted port 5080 is **loopback only**, private LAN firewall helper updated accordingly. Existing paired device must re-pair if it lacks a verified TLS fingerprint.
3. Annual **5/10/25** remote client commercial entitlement for newly issued signed licenses; older signed remote periods remain compatible. Free LAN clients don't consume remote seats.
4. Flutter source updated for certificate pinning, persistent cursor retrieval, local notifications, exact-occurrence ACK. **Not a released APK/IPA**: platform folders/FCM/APNs integration and physical device validation remain for mobile team.
5. Separate, complete `MOBILE_TEAM_HANDOFF_RC6_4_6.md` and `WEB_CLOUD_RELAY_HANDOFF_RC6_4_6.md`.

## Windows developer test

1. **Back up the entire existing `data` directory (including `prognode.db`, WAL and SHM while service is stopped)** before any upgrade. This migration is additive but database rollback after v9 requires backup.
2. Extract this ZIP into a **new** directory; do not overwrite a running installed Core. If you want prior config/history, copy the **stopped and backed-up** Core `data` into this new directory.
3. On Windows 11 with .NET 10 SDK, run `START_PROGNODE_RC6_4_6.cmd`. The script stops old developer Core, builds `PROGNODE.sln`, launches the new DLL and checks `/api/health` version and native fullscreen Trend Studio. It does not silently launch an old build on failure.
4. For mobile LAN from the real Core machine, run **Administrator** PowerShell: `./ENABLE_PROGNODE_LAN_HTTPS.ps1`. It creates a nonexportable LocalMachine certificate, enables port 5443 Private firewall and displays its SHA256 fingerprint. Restart Core via the new starter; read/enter that fingerprint **independently from the Core PC** during Flutter pairing. UDP 5081 discovery does not prove server identity. Browser on Core localhost can continue HTTP 5080.
5. Test event DB separately (Python 3): `python tests/validate_notification_sqlite_v9.py`. This checks extracted production SQL and simulated v8 upgrade + rollback/commit/reopen. It **does not replace .NET build, actual migration on customer data, or real PLC/phone acceptance**.
6. Core JSON read/test after sign-in: `GET /api/mobile/v2/notifications?cursor=0&limit=100`; see handoff for proper paired Bearer and user session requirements. Windows Client legacy protocol remains; remote clients require deployed cloud.

## Expected safety behavior / known external blockers

- Core local PLC alarms/historian remain independent of external network; new mobile ACK never authorizes an older occurrence against a newer activation.
- `/api/mobile/v2/notifications/health` is loopback + signed-in-only. Health appears in the local Notification Center when journal storage fails.
- Cloud URL is intentionally **not auto-guessed**; prior domain certificates were untrusted and the tested Vercel activation URL produced a Next.js HTML not-found page. Do not disable SSL validation. A working production JSON license API + relay + FCM/APNs is required for real **REMOTE** phone delivery.
- With Flutter foreground polling the LAN app can display notifications while open. Killed/background app delivery on Android 16, and APNs suspended app notifications on iOS, are **not delivered by this source alone**; full mobile team integration and signed Android/iOS builds are required.
- This packaging environment has no `dotnet` or `flutter` SDK. **The ZIP is source-level/pre-Windows-compile**. The test results in `VALIDATION_RC646.txt` list exact static checks and limitations. Please return your Windows build output and the first physical-device test log before production rollout.
