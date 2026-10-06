# RC1 release checks performed in this workspace

- JavaScript parse (`node --check`): PASS
- CSS parse (`tinycss2`): PASS / 0 parser errors
- Duplicate HTML IDs: PASS / 0 duplicates
- Missing `data-i18n` keys: PASS / 0 missing
- Runtime plant/factory image assets: REMOVED
- Customer protocol catalog advertises Mock/Simulator: NO
- Development license fallback / automatic development session: REMOVED
- First-run Setup Assistant call remains in startup: YES
- Signed license fixture + real Ed25519 public key verification: PASS
- Public key SHA-256 SPKI fingerprint: 3e902011b79009ec3b1e66fb7fbfddb24567eb438479802c36cb8d4db6c3e402

Full .NET build was not executed in this workspace because the .NET SDK is not installed here. Build must be run on the Windows development machine before tagging RC1.
