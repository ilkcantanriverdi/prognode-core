# PROGNODE DEV13.3 — Operational WOW + strict local access + 15-minute commissioning

- Dashboard-first Overview gains an operational status rail with connectivity, alarm posture, recording and access state.
- Sidebar brand divider and topbar divider share the same 72px baseline.
- Configuration mutation is now fail-closed: a valid assigned license AND authenticated local session are required even when Development fallback exists.
- Quick Actions and configuration controls are visibly locked while signed out.
- The 15-minute Setup Assistant opens on incomplete commissioning, even before sign-in. It guides sign-in first, then Device -> Tag -> Alarm -> Historian.
- Dismissal is session-only; setup remains discoverable from the topbar and Overview until complete.
- Existing API shapes, PLC polling, alarms, historian and license cryptography are unchanged.
