# HF6.5 source validation and limitations

- Baseline: HF6.4 full source. HF6.5 modifies manual pairing + backup review/restore guard only.
- Independent Python SAS golden vector: PASS (`BQF4-2T1V-R08H` with the included public DER fixture).
- Node syntax: `app.js`, `manual-pairing.js` PASS.
- Isolated actual Chromium component test with stub JSON: OTP/SAS displayed in the Manual tab, full fingerprint only after opening Advanced, QR tab cancels active manual code, Cancel clears displayed secrets: PASS. This is UI component smoke, not real Core TLS or mobile pairing.
- QR pairing service, original Trend Studio engine/mount, LAN TLS health class, backup scheduler and mobile ACK audit store bytewise unchanged from HF6.4.
- Backup restore guard added: source and current installation IDs must match, otherwise explicit offline identity-adoption consent required; existing data preserved for rollback. Existing encrypted backup import still performs verification only.
- Unit/contract tests added: .NET `tests/ManualPairingContract`, BackupContract cross-server migration safety; NOT RUN here (.NET 10 SDK missing).
- Windows PowerShell UAC/service, real TLS handshake, physical Android/iOS SAS matching, MITM, QR/ACK and actual backup restore acceptance: NOT RUN. Do not send to customers as a validated production release.
