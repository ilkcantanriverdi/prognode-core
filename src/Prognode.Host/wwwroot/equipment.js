// Equipment cards: optional machine view over Tags. Live values and active alarms come from app.js state
// (refreshed every second); chart history and recent alarm events are fetched here while a card is visible.
(() => {
  const eq = { items: [], filter: "all", series: new Map(), history: [], seriesAt: 0, historyAt: 0, editingId: null };
  const PRIORITY = ["low", "medium", "high", "critical"];
  const esc = value => escapeHtml(String(value ?? ""));

  async function load() {
    try { eq.items = await api("/api/equipment"); } catch (e) { console.warn(e); }
    render();
  }

  const tagById = id => state.tags.find(t => t.id === id);
  const recorded = () => new Set((state.historianConfigurations || []).map(c => c.configuration?.tagId ?? c.tagId));

  // The card's alarm: highest priority among its Tags; it blinks while any of them waits for ACK.
  function alarmOf(card) {
    const ids = new Set(card.items.map(i => i.tagId));
    const alarms = (state.activeAlarms || []).filter(a => ids.has(a.tagId));
    if (!alarms.length) return null;
    const rank = a => PRIORITY.indexOf(String(a.priority || "low").toLowerCase());
    const top = alarms.reduce((a, b) => rank(b) > rank(a) ? b : a);
    return { priority: PRIORITY[Math.max(0, rank(top))], rank: rank(top), unacked: alarms.some(a => a.state !== "Acknowledged" && a.requiresAcknowledgement !== false), count: alarms.length };
  }

  // Last hour of history, with the Tag's analog alarm limits as dashed lines.
  function sparkline(tagId, points, alarmed) {
    const values = points.map(p => p.value).filter(v => typeof v === "number");
    if (values.length < 2) return `<div class="eq-chart-empty">${"Waiting for history…"}</div>`;
    const limits = (state.alarmDefinitions || [])
      .filter(d => d.tagId === tagId && d.enabled !== false && typeof d.threshold === "number" && d.condition !== "DigitalEquals")
      .map(d => d.threshold);
    const all = values.concat(limits);
    const min = Math.min(...all), max = Math.max(...all), span = max - min || 1;
    const y = v => (56 - ((v - min) / span) * 50).toFixed(1);
    const step = 300 / (values.length - 1);
    const path = values.map((v, i) => `${(i * step).toFixed(1)},${y(v)}`).join(" ");
    return `<svg class="eq-spark${alarmed ? " alarmed" : ""}" viewBox="0 0 300 60" preserveAspectRatio="none" aria-hidden="true">${
      limits.map(l => `<line class="eq-limit" x1="0" x2="300" y1="${y(l)}" y2="${y(l)}"/>`).join("")}<polyline points="${path}"/></svg>`;
  }

  // One cell of the card's 2-column grid; a CHART Tag adds its sparkline under the value.
  function cell(item, recordedSet) {
    const tag = tagById(item.tagId);
    if (!tag) return `<div class="eq-cell"></div>`;
    const snapshot = state.tagValues.get(tag.id);
    const alarmed = (state.activeAlarms || []).some(a => a.tagId === tag.id);
    const bad = snapshot && snapshot.quality && snapshot.quality !== "Good";
    const chart = item.view !== "CHART" ? ""
      : recordedSet.has(tag.id) ? sparkline(tag.id, eq.series.get(tag.id) || [], alarmed)
      : `<div class="eq-chart-empty">${"Record in Historian for a chart"}</div>`;
    return `<div class="eq-cell${alarmed ? " alarmed" : ""}${bad ? " bad" : ""}">
      <span class="eq-label" title="${esc(tag.name)}">${esc(tag.name)}</span>
      <strong class="eq-value">${esc(tagValueText(tag, snapshot))}<small>${esc(tag.unit || "")}</small></strong>
      ${chart}</div>`;
  }

  // Always three rows, so every card has the same height.
  function events(card) {
    const ids = new Set(card.items.map(i => i.tagId));
    const rows = eq.history.filter(e => ids.has(e.tagId)).slice(0, 3);
    if (!rows.length) return `<div class="eq-events-empty">${"No recent alarms"}</div>`;
    return rows.map(e => {
      const label = e.clearedAt ? "NORMAL" : e.acknowledgedAt ? "ACK" : "ALARM";
      const cls = label === "ALARM" ? `eq-tag-${String(e.priority || "low").toLowerCase()}` : label === "ACK" ? "eq-tag-ack" : "eq-tag-normal";
      const time = new Date(e.activeAt).toLocaleTimeString([], { hour: "2-digit", minute: "2-digit", hour12: false });
      return `<div class="eq-event"><span class="eq-tag ${cls}">${label}</span><span class="eq-event-text">${esc(e.text)}</span><time>${time}</time></div>`;
    }).join("");
  }

  function card(item, recordedSet, writable) {
    const alarm = alarmOf(item);
    const device = state.devices.find(d => d.id === item.deviceId);
    const status = device ? deviceRuntimeStatus(device) : null;
    const online = status && /online|connected|good/i.test(status);
    const cells = item.items.map(i => cell(i, recordedSet));
    if (cells.length % 2) cells.push(`<div class="eq-cell eq-cell-empty"></div>`);
    // Fixed card size: the more Tags, the denser the grid (rows 1-4) and the smaller its text.
    const rows = cells.length / 2;
    // Cards are redrawn every second; the negative delay keeps the blink in phase instead of restarting it.
    return `<article class="eq-card eq-rows-${rows}${alarm ? ` eq-alarm-${alarm.priority}${alarm.unacked ? " eq-blink" : ""}` : ""}" data-equipment="${item.id}" style="animation-delay:-${Date.now() % 1200}ms">
      <header class="eq-head">
        <h3 title="${esc(item.name)}">${esc(item.name)}</h3>
        <div class="eq-head-side">
          ${status ? `<span class="eq-status${online ? " on" : ""}">${esc(device.name)} ${esc(status)}</span>` : ""}
          <button class="eq-edit" data-edit-equipment="${item.id}" ${writable ? "" : "disabled"} title="${"Edit"}" aria-label="${"Edit"} ${esc(item.name)}">✎</button>
        </div>
      </header>
      <div class="eq-cells">${cells.join("")}</div>
      <div class="eq-events">${events(item)}</div>
    </article>`;
  }

  function render() {
    const grid = $("equipmentGrid");
    if (!grid) return;
    renderOverviewStrip();
    if (state.page !== "equipment") return;
    const recordedSet = recorded(), writable = canMutateConfiguration();
    const list = eq.items
      .map(item => ({ item, alarm: alarmOf(item) }))
      .filter(x => eq.filter === "all" || x.alarm)
      .sort((a, b) => (b.alarm ? b.alarm.rank + 1 : 0) - (a.alarm ? a.alarm.rank + 1 : 0));
    $("equipmentEmpty").classList.toggle("hidden", eq.items.length > 0);
    $("equipmentAdd").disabled = !writable;
    grid.innerHTML = list.map(x => card(x.item, recordedSet, writable)).join("") ||
      (eq.items.length ? `<p class="panel-copy">${"No equipment in alarm."}</p>` : "");
  }

  // Overview: one line, plus the cards that are in alarm (at most two).
  function renderOverviewStrip() {
    const strip = $("overviewEquipment");
    if (!strip) return;
    strip.classList.toggle("hidden", eq.items.length === 0);
    if (!eq.items.length || state.page !== "overview") return;
    const alarmed = eq.items.filter(alarmOf);
    const recordedSet = recorded(), writable = canMutateConfiguration();
    strip.innerHTML = `<button class="eq-strip" data-page-link="equipment"><strong>${"Equipment"}</strong><span>${eq.items.length} · ${alarmed.length ? `<b class="danger-text">${alarmed.length} ${"in alarm"}</b>` : "all normal"}</span><span aria-hidden="true">→</span></button>
      ${alarmed.length ? `<div class="eq-grid">${alarmed.slice(0, 2).map(x => card(x, recordedSet, writable)).join("")}</div>` : ""}`;
  }

  async function refreshDetails() {
    const visible = state.page === "equipment" || (state.page === "overview" && eq.items.some(alarmOf));
    if (!visible || !eq.items.length) return;
    const now = Date.now();
    if (now - eq.historyAt > 10000) {
      eq.historyAt = now;
      try { eq.history = await api("/api/alarms/history?limit=200"); } catch (e) { console.warn(e); }
    }
    if (now - eq.seriesAt > 30000) {
      eq.seriesAt = now;
      const recordedSet = recorded();
      const ids = [...new Set(eq.items.flatMap(c => c.items.filter(i => i.view === "CHART" && recordedSet.has(i.tagId)).map(i => i.tagId)))];
      for (let i = 0; i < ids.length; i += 8) {
        try {
          const result = await api(`/api/trend-studio/series?tagIds=${ids.slice(i, i + 8).join(",")}&maxPoints=120`);
          for (const s of result.series || []) eq.series.set(s.tagId, s.points || []);
        } catch (e) { console.warn(e); }
      }
    }
  }

  // ---- editor
  function openEditor(id) {
    const item = eq.items.find(x => x.id === id);
    eq.editingId = item?.id || null;
    $("equipmentModalTitle").textContent = item ? "Edit equipment" : "Add equipment";
    $("equipmentName").value = item?.name || "";
    $("equipmentDevice").innerHTML = `<option value="">${"No device"}</option>` +
      state.devices.map(d => `<option value="${d.id}">${esc(d.name)}</option>`).join("");
    $("equipmentDevice").value = item?.deviceId || "";
    $("deleteEquipment").classList.toggle("hidden", !item);
    $("equipmentError").textContent = "";
    renderPicker(new Map((item?.items || []).map(i => [i.tagId, i.view])));
    $("equipmentModal").classList.remove("hidden");
    $("equipmentName").focus();
  }

  // The card's Tags first, in card order (the list order is the card order); then the chosen device's Tags.
  function renderPicker(selected) {
    const deviceId = $("equipmentDevice").value;
    const order = [...selected.keys()];
    const rank = t => order.includes(t.id) ? order.indexOf(t.id) - 1000 : t.deviceId === deviceId ? 0 : 1;
    const tags = [...state.tags].sort((a, b) => rank(a) - rank(b) || a.name.localeCompare(b.name));
    $("equipmentTagList").innerHTML = tags.map(t => {
      const view = selected.get(t.id);
      return `<label class="eq-pick"><input type="checkbox" data-eq-tag="${t.id}" ${view ? "checked" : ""}>
        <span><strong>${esc(t.name)}</strong><small>${esc(tagDeviceName(t.deviceId))}${t.unit ? " · " + esc(t.unit) : ""}</small></span>
        <select data-eq-view="${t.id}" ${view ? "" : "disabled"} aria-label="${"Show as"}"><option value="VALUE">${"Value"}</option><option value="CHART" ${view === "CHART" ? "selected" : ""}>${"Chart"}</option></select></label>`;
    }).join("") || `<p class="panel-copy">${"Add Tags first."}</p>`;
    updatePickCount();
  }

  function picked() {
    return [...document.querySelectorAll("[data-eq-tag]:checked")].map(box => ({ tagId: box.dataset.eqTag, view: document.querySelector(`[data-eq-view="${box.dataset.eqTag}"]`).value }));
  }

  function updatePickCount() { $("equipmentPickCount").textContent = `${picked().length} / 8`; }

  async function save() {
    const body = { name: $("equipmentName").value, deviceId: $("equipmentDevice").value || null, items: picked() };
    if (!body.name.trim()) return void ($("equipmentError").textContent = "Enter a name.");
    if (body.items.length < 1 || body.items.length > 8) return void ($("equipmentError").textContent = "Choose between 1 and 8 Tags.");
    try {
      await api(eq.editingId ? `/api/equipment/${eq.editingId}` : "/api/equipment", { method: eq.editingId ? "PUT" : "POST", body: JSON.stringify(body) });
      $("equipmentModal").classList.add("hidden");
      eq.seriesAt = 0;
      await load();
      showToast("Equipment saved.");
    } catch (e) { $("equipmentError").textContent = e.message; }
  }

  async function remove() {
    if (!eq.editingId || !confirm("Delete this equipment card? Its Tags are kept.")) return;
    try {
      await api(`/api/equipment/${eq.editingId}`, { method: "DELETE" });
      $("equipmentModal").classList.add("hidden");
      await load();
    } catch (e) { $("equipmentError").textContent = e.message; }
  }

  function install() {
    if (!$("page-equipment")) return;
    $("equipmentAdd").addEventListener("click", () => openEditor(null));
    $("equipmentEmptyAdd").addEventListener("click", () => openEditor(null));
    $("closeEquipmentModal").addEventListener("click", () => $("equipmentModal").classList.add("hidden"));
    $("saveEquipment").addEventListener("click", save);
    $("deleteEquipment").addEventListener("click", remove);
    $("equipmentDevice").addEventListener("change", () => renderPicker(new Map(picked().map(i => [i.tagId, i.view]))));
    $("equipmentTagList").addEventListener("change", event => {
      const box = event.target.closest("[data-eq-tag]");
      if (box) {
        if (box.checked && picked().length > 8) { box.checked = false; $("equipmentError").textContent = "A card shows at most 8 Tags."; }
        document.querySelector(`[data-eq-view="${box.dataset.eqTag}"]`).disabled = !box.checked;
      }
      updatePickCount();
    });
    document.querySelectorAll("[data-eq-filter]").forEach(b => b.addEventListener("click", () => {
      eq.filter = b.dataset.eqFilter;
      document.querySelectorAll("[data-eq-filter]").forEach(x => x.classList.toggle("active", x === b));
      render();
    }));
    document.addEventListener("click", event => {
      const edit = event.target.closest("[data-edit-equipment]");
      if (edit && !edit.disabled) openEditor(edit.dataset.editEquipment);
      const link = event.target.closest("[data-page-link]");
      if (link) navigate(link.dataset.pageLink);
    });
    window.addEventListener("prognode:pagechange", () => { eq.seriesAt = 0; eq.historyAt = 0; render(); void refreshDetails().then(render); });
    void load();
    setInterval(() => { render(); void refreshDetails(); }, 1000);
    setInterval(load, 30000);
  }

  if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", install); else install();
})();
