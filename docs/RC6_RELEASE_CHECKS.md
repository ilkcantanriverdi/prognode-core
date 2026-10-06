# RC6.3 Release Checks — Web V1.8 + Developer Test Bench Core

Required static/runtime acceptance points:

- Core / Agent / UI version markers: `0.7.2-rc6.3`
- production Ed25519 trust root unchanged
- Web V1.8 requires signed `expiresAtUtc`, `graceUntilUtc`, numeric `maxTags`, `pricingVersion`
- subscription and entitlement `maxTags` must match
- V1.8 Tag capacities are exactly `100 / 250 / 500`
- V1.8 Remote Access capacities are exactly `5 / 10 / 25`
- V1.8 does not expose a sellable unlimited Tag or Remote Access tier
- pre-V1.8 legacy signed licenses may retain unlimited compatibility semantics
- lifecycle includes ACTIVE / EXPIRING_SOON / GRACE / EXPIRED / INVALID
- exact base grace duration: 7 days
- configured unique Tag count is the only Tag-capacity counter
- downgrade over current count reports OVER_CAPACITY; configuration preserved; new/edit config blocked; deletion remains possible
- EXPIRED stops paid Alarm engine and Historian sampling without stopping the Core host/PLC polling
- Web V1.8 Remote Access expiry is accepted from signed `payload.addons.remoteAccess.expiresAtUtc`
- RC6-style signed `entitlements.remoteAccess.expiresAtUtc` remains accepted as a fallback
- if both Remote Access expiry fields exist, they must match
- `addons.remoteAccess.enabled/maxClients` and `entitlements.remoteAccess.enabled/maxClients` must match when both objects exist
- Remote Access has independent signed expiry and no base-license grace extension
- LAN clients consume zero Remote Access seats
- cloud activation/heartbeat is optional/best effort and cannot stop valid offline runtime
- no prices are embedded in Core
- no private signing key or customer `.pgnlicense` is shipped in the release package
- `RemoteAccessService.cs` no longer has the RC6 CS0173 nullable conditional compile error

- Setup Assistant is absent from persistent Overview/top-bar UI and supports a persistent first-run opt-out
- Tags page displays used and remaining licensed Tag capacity
- Alarm History primary export is XLSX with separate columns
- CloudLicense production host discovery is configured and new license import resets cached activation state
- cloud activation checks occur within the short local sync loop while heartbeat traffic remains throttled


## RC6.3 Test Bench contract

- numeric AlarmDefinition uses the existing AlarmEngine (no parallel analog engine)
- numeric conditions: GreaterThan / GreaterThanOrEqual / LessThan / LessThanOrEqual
- threshold + deadband persist in SQLite; old rows default to DigitalEquals
- BAD/STALE quality never drives process alarm ACTIVE/CLEAR state changes
- Modbus 00001/10001/30001/40001 map to FC01/FC02/FC04/FC03 respectively
- Modbus batching is separated by Area / Function Code and respects 2000-bit / 125-register limits
- existing 40001 Holding Register Tags remain valid without migration
- BatchRun supports Running / Completed / Aborted and only one Running Batch
- historian_samples persist batch_id
- batch_alarm_occurrences persist many-to-many Batch/alarm occurrence membership
- alarms already ACTIVE when a Batch starts are linked without re-triggering the alarm
- Batch APIs are exposed only through the normal Core HTTP boundary and mutation authentication middleware
- SQLite schema version is 8 and RC6.1/RC6.2 data migrates additively
