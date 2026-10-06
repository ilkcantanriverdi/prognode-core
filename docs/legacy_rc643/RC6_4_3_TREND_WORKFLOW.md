# PROGNODE V0.7.2 RC6.4.3 — TREND STUDIO FEEDBACK PREVIEW

**Based on RC6.4.2.** The existing RC6.3.1-HF2 licensing, sign-in, alarm, Modbus and Batch/Lot components are retained.

## Open and test it

- **No Core install required for UI feedback:** open `TREND_STUDIO_RC6_4_3_INTERACTIVE_DEMO.html`. All sample process values are **simulated** and explicitly labeled. Toggle Light/Dark; the real Core uses historian data instead.
- **Windows development PC:** run `START_PROGNODE_RC6_4_3_PREVIEW.cmd`. It builds the solution, stops the old PROGNODE service/developer process, launches the current Core and Windows Agent, checks `/api/health`, then opens the browser.
- Verify the sidebar version `v0.7.2-rc6.4.3-trend-workflow`; if it differs, the browser is still reaching an older Core.
- Add the test PLC Tag on the **Historian** page first. An unenrolled Tag appears disabled in Trend Studio. Enable recording and then revisit Trends.
- For the read-only API acceptance check use `TEST_PROGNODE_RC6_4_3_TREND.ps1`.

## Changes since RC6.4.2

1. **5m / 15m / 1h / 8h / 24h / 7d actually select a distinct query window.** Their handlers no longer race with the legacy app.js handlers. In LIVE each click changes the continuous rolling window and keeps LIVE active. In PAUSE it selects a frozen interval anchored at the previous end time. From/To are editable only in PAUSE.
2. **Shape-preserving smooth curves**, enabled by default for numeric signals with at least 3 valid samples. Smooth is purely visual; the raw historian values, alarm thresholds, A/B calculations and CSV are unchanged. Disable Smooth to inspect exact straight-line interpolation; digital/BOOL remains a step graph; BAD/STALE never gets bridged.
3. **Historian-first selection.** The explorer visibly marks Tags without recording as unavailable. Both ad-hoc and saved Trend API read endpoints reject queries for non-enrolled Tags. The raw SQLite history is *not* deleted by this gate. Runtime enrollment checks query only the configuration repository (not expensive sample statistics).
4. **Trend counters:** the sidebar and workspace distinguish saved views from currently open, unsaved charts. A new ad-hoc chart no longer leaves the Trends menu at zero.
5. **Separate vs Compare mode:** separate is the default. Each selected Tag has its own independent graph, Y scale, alarm thresholds, assigned alarm flags and inline alarm events. Comparison is explicit: `Compare` overlays the selected Tags on one chart, retaining left/right Y-axis controls. System alarms are opt-in to avoid mixing unrelated alarms.
6. **Full light theme** for chart surfaces, cards, navigation and event/details, and no duplicate oversized Trend Studio heading. `Trend Studio` is the main page title; the workspace retains `New analysis`/saved-view name.

## Regression checklist on a real Windows Core

- Import the **same existing** signed license, sign in and confirm revoke/session behavior remains unchanged.
- Configure at least two numeric Tags in Historian with samples. Confirm a non-recorded Tag is disabled until it is enrolled.
- Test all six range presets in LIVE and while PAUSED. LIVE must advance; selecting a preset must not silently restart a paused trend.
- Select multiple Tags in Separate, check each chart shows only that Tag's alarms; click Compare and verify dual-axis choices.
- Set HIGH and LOW thresholds outside the measured range: the axis must include both; change the measured value across a limit and observe the line color.
- Move A/B cursors, hide/reveal flags, click alarm details, toggle smooth/exact and Light/Dark.
- Check the menu count when a view is unsaved and after Save view.
- Run the Developer Test Bench against the **real Modbus simulator → Core → historian API**; this HTML demo alone is not end-to-end validation.

**Validation here:** Chromium offline interactive tests + node syntax and previous trend logic tests. **Not done here:** `dotnet build`, real PLC/Windows Core regression, multi-day historian soak/load. Do not treat the preview demo as production historian evidence.

---

