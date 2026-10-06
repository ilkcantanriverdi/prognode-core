# RC6.4.1 Trend Studio feedback patch

- LIVE runs in a rolling window ending at now, refreshed by the existing five-second shell interval.
- LIVE button pauses the exact visible window and enables From/To. Resume returns to rolling mode.
- Range pills change the live window duration without freezing it. Custom date selection requires pause.
- Removed mouse-wheel zoom, so page scrolling works normally. Zoom is by drag selection or toolbar.
- Independent controls: Alarm events, Flags on chart, Alarm thresholds, Batch/Lot.
- Added last valid historian sample timestamp and warning if no new samples arrive. This does not manufacture missing samples or mask a real Historian/Tag outage; verify recording configuration and quality if it warns.
- Core API and SQLite schema unchanged. No process-data injection.

Run START_PROGNODE_RC6_4_PREVIEW.cmd and BUILD_PROGNODE_CORE.cmd as usual. The service running on port 5080 must be the newly built Core; clear the browser cache if necessary.
