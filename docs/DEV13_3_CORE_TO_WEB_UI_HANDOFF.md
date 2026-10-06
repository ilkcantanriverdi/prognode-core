# PROGNODE DEV13.3 — Core -> Web UI handoff

Shared product-language decisions suitable for prognode.io/account/control surfaces:

- Core is dashboard-first; public Web can remain storytelling-first.
- Shared visual system: deep navy surfaces, cyan for active/interactive states, teal/green for healthy state, amber for attention, red only for alarm/failure.
- Header geometry uses one 72px baseline across logo/brand and page header.
- Product status is expressed through compact operational rails rather than decorative hero photography.
- Recommended reusable patterns: operational status rail, compact KPI cards, status chips, setup progress indicator, access state (Signed in / View only).
- 15-minute commissioning is a first-class product workflow, not a dismissible marketing welcome screen.
- "Sign in" must always correspond to actual mutation authorization; never show editable controls as active while signed out.
