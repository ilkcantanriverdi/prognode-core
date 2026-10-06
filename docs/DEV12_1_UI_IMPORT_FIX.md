# PROGNODE V0.7.2 DEV12.1 — License Import / Sign-In UX Fix

DEV12.1 is a focused bug-fix release on top of DEV12.

## Fixed

- The account modal was rendered **after** `app.js`, so the Close / Import / Sign-in / Sign-out buttons did not exist when event listeners were attached.
- `app.js` now loads after the modal markup. The modal controls bind correctly.
- Close now works from X, Close button, backdrop click, and Escape.
- Successful sign-in closes the popup automatically.
- Import from the License page opens the sign-in popup after a successful import.

## UI cleanup

- Removed the separate `DEV / DEVELOPMENT` top-bar license pill.
- Before a real customer login, the top bar shows only **Sign in** and does not display the Development fallback identity.
- After login, the account pill shows the account plus product and remaining license time.
- Removed the large Current License / Assigned User / Server / Tag Limit card from the access popup.
- Before login, the popup contains only the compact `.pgnlicense` import area and credentials.
- After login, the popup shows the product and remaining license time plus Sign out.

## Security behavior unchanged

DEV12.1 does not weaken the DEV12 offline-auth contract. Signed production licenses still require a locally trusted signing public key and must pass signature, expiry/status, entitlement, account email and Argon2id verifier validation.
