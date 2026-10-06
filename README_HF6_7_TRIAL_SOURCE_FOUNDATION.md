# HF6.7 TRIAL — SOURCE FOUNDATION (NOT CUSTOMER RELEASE)

This is a **source-only early implementation**, based on HF6.6. It is NOT a signed trial license, EXE or installer.

Implemented in source: signed TRIAL recognition (Ed25519 verification remains mandatory), exact 14-day validity with NO grace, fixed 1 device / 10 unique Tags, both Alarm and Historian, preserved commercial 100/250/500 tiers and existing imports. All constraints are verified against claims AFTER signature verification. No changes to the underlying QR certificate, database, or backup format.

**Security/release blockers**: bind signed TRIAL to existing Core installation/machine identity without tying customers to a replaceable OS identifier; trusted-time/rollback journal with safe failure and VM rollback limits; Web/Control Center one-time trial issuance; device count enforcement on all mutations AND background polling when database has >1 device; live runtime expiry tests; remote entitlement trial policy; UI expiration/read-only flows; paid-license migration regression; .NET build, Windows EXE publish, physical tests, installer signing. Never place a trial signing private key in the customer package.

**Windows compile:** `dotnet build PROGNODE.sln -c Release`. This source patch has not been compiled in this environment; do not deploy to paying customers.
