# PROGNODE Cloud/Web Split

The public website, Account Portal and private Control Center are developed and deployed separately (Vercel/Web project). This runtime repository must not host those web applications.

The only development web endpoint in this repository is the local PROGNODE Server UI/API on port 5080.

Cloud responsibility: identity, organization, subscriptions, payment state, commercial license issuing/signing, activation/installations, setup/download metadata.

Customer-site responsibility: PLC connections, Devices, Tags, Alarm Engine, Alarm history, Historian, Trends, local configuration, local runtime data.

The cloud can issue a signed `.pgnlicense`; the Server verifies and consumes it. The private signing key never belongs in this repository.
