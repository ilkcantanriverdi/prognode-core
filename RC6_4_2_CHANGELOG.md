# RC6.4.2 — Trend Studio Analysis Feedback

Base: RC6.4.1 Trend Feedback. Production PLC reads/historian writes remain unchanged. No SQLite schema migration.

## Improvements
1. Cursor A/B: click button A, then click a plotted point; repeat for B. First action pauses LIVE to freeze the timeline. The visible comparison panel shows A and B timestamps, elapsed time and per-tag values and B−A deltas; Clear removes both. Only valid GOOD samples enter arithmetic.
2. Alarm-aware Y scale: enabled numeric alarm definitions' High/Low setpoints are included in the axis bounds regardless of the displayed sample range or the visibility of threshold lines. Example GOOD=13, LOW=10 and HIGH=30 -> approximate auto Y range 8–32. Left/right axis handled independently. Digital alarms do not affect numeric Y bounds.
3. Colored signal: normal uses per-tag color, high threshold exceedance is red, low exceedance amber. Continuous linear samples split exactly at each crossing. Configured setpoints and threshold state appear in signal cards. A colored trace indicates sampled setpoint crossings, not necessarily Alarm Engine's ACTIVE state (delay/hysteresis/quality still apply in the engine). BAD/STALE values break lines; no invented interpolation.
4. Alarm events: event rows and chart flags open the same persistent detail panel; selected flag is outlined, not removed. Flag visibility and threshold-line visibility remain independently controllable. Events in a 12 px cluster show a count; clicking chooses the first occurrence and other occurrences remain selectable in the event list.
5. Sidebar: Alarm = configured alarm definitions, Trend = saved Trend views, Historian = recorded Tag configurations. The top alarm bell continues to mean *active alarms*. Counts show zero too.

## Validation on Windows
Run `START_PROGNODE_RC6_4_PREVIEW.cmd`. Confirm `v0.7.2-rc6.4.2-trend-analysis` at bottom left. Select a Historian-recorded analog Tag with enabled numeric alarm definitions; refresh and check 13/10/30 scaling. Pause then set A and B and inspect deltas. Click an alarm flag: detail panel appears, flag stays. Navigate between pages and verify sidebar counts.

Static JavaScript/HTML tests are included in `tests/`. Actual .NET Windows compilation and real PLC behavior must be verified on the developer PC.
