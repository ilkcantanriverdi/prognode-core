# PROGNODE 0.7.2 RC1 — UI / UX Freeze

This revision closes the current Core UI/UX milestone so protocol work can continue.

## Product rules frozen in RC1

- Core is an operational product, not a marketing page. No industrial stock imagery or promotional hero blocks in runtime.
- Overview opens directly on operational state, KPIs, alarms, notifications, historian status and commissioning readiness.
- The global header reports the real operational posture (stable vs attention required), not a permanently green decorative status.
- Customer configuration is fail-closed: an imported signed license plus an authenticated local account session is required for mutation.
- Development-license auto-login/fallback is removed from the customer Core path.
- First-run commissioning remains a first-class workflow until Device -> Tag -> Alarm -> Historian reaches 4/4.
- The Setup Assistant is discoverable from the top bar and Overview until commissioning is complete.
- Light and dark modes share the same information hierarchy and component grammar.
- Device onboarding exposes only production connector(s). Mock/Simulator is no longer advertised in the customer protocol catalog.
- Prototype/version/milestone wording is removed from customer-facing UI copy.
- Runtime photos have been removed from wwwroot; the Core UI is text/data/component driven.

## Overview information architecture

1. Operational status / attention state
2. Connectivity / alarm / recording / Core / license / access facts
3. KPI cards
4. Device health, active alarms and recent notifications
5. Historian / recording health
6. Setup readiness + quick actions

## Shared design tokens / web handoff

- Deep Navy: #0A1B2E
- Cyan: #00D9FF
- Teal: #00B3A6
- Light Slate: #E6EDF4
- Status colors are semantic only: green=healthy, amber=warning, red=critical/offline.
- Runtime surfaces use restrained radius/shadows and high information density.
- Marketing imagery belongs on the public website; app-like web/account areas can reuse Core's status chips, surface hierarchy and spacing rules.

## Next milestone

UI is considered frozen except for functional bugs, accessibility defects, localization defects or issues discovered while implementing new connectors. The next product milestone is protocol expansion.
