# RC5 Release Checks

Executed in the assistant build workspace:

- `node --check src/Prognode.Host/wwwroot/app.js` — PASS
- duplicate HTML IDs — 0
- missing `data-i18n` keys — 0
- CSS brace balance — 0
- customer package `.pgnlicense` files — 0
- private signing key files — 0
- version markers Core / Agent / UI — `0.7.2-rc5`
- production Ed25519 public-key signature verification against the previously supplied real license — PASS
- `TAG_LIMIT_REACHED` API path present
- `/api/license/usage` present
- central `TagService` capacity policy present
- ACTIVE / GRACE / EXPIRED / INVALID lifecycle implementation present
- future Cloud license refresh abstraction present

The container does not have the .NET SDK, therefore a real `dotnet build` was not executed here. Run `BUILD_PROGNODE_CORE.cmd` on the Windows development machine before release acceptance.
