# RC4 release checks

Checks actually executed in the assistant build environment:

- JavaScript syntax (`node --check`): PASS
- HTML duplicate IDs: PASS (0 duplicates)
- HTML `data-i18n` keys vs EN/TR translation sets: PASS (0 missing)
- `appsettings.json` parse: PASS
- Flutter `pubspec.yaml` parse: PASS
- Dart relative imports resolve: PASS
- `.csproj` project references resolve after Windows-path normalization: PASS
- Customer runtime contains personal `.pgnlicense`: NO
- Customer runtime contains private signing key file: NO
- Remote endpoint self-scope/admin-loopback guards present: PASS
- Existing production v2 license Ed25519 verification: PASS
- Production public-key SPKI SHA-256: `3e902011b79009ec3b1e66fb7fbfddb24567eb438479802c36cb8d4db6c3e402`
- Tested legacy license SHA-256: `7e96c914014bbcbb911b7e4b464320532dbab3e8a7f6f16343cb8e0403dfa97d`
- Tested legacy license contains `entitlements.remoteAccess`: `False` (expected; the new signed extension is optional/backward compatible)

Toolchain availability in this environment:

- .NET SDK: NOT INSTALLED
- Flutter SDK: NOT INSTALLED
- Dart SDK: NOT INSTALLED

Therefore a full `dotnet build` and `flutter analyze/build` were **not** claimed here. They must be run on the Windows development machine before production promotion.

Cloud end-to-end Remote Access is also **not** claimed in RC4. The Web/Cloud service is still being implemented and `Prognode:RemoteAccess:BaseUrl` intentionally defaults to empty.
