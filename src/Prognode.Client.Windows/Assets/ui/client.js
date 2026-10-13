// PROGNODE Client window. The tray app (C#) owns the Core connection; this page only renders the state
// it sends and posts the user's commands back. Outside the app (plain browser) it shows sample data.
"use strict";
const $ = id => document.getElementById(id);
const host = window.chrome && window.chrome.webview;
const send = msg => host ? host.postMessage(msg) : preview(msg);

const VIEWS = { connect: "Connect", alarms: "Alarms", history: "History", connection: "Connection" };
let state = { paired: false, alarms: [], access: null, connection: {}, server: null };
let view = "alarms", selected = null, historyOffset = 0, historyTotal = 0, candidate = null, toastTimer = 0;

const fmt = iso => iso ? new Date(iso).toLocaleString(undefined, { day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit", second: "2-digit" }) : "—";
const ago = iso => {
  if (!iso) return "—";
  const s = Math.max(0, Math.round((Date.now() - new Date(iso)) / 1000));
  return s < 60 ? `${s} s ago` : s < 3600 ? `${Math.round(s / 60)} min ago` : fmt(iso);
};
const text = (tag, value, cls) => { const el = document.createElement(tag); if (cls) el.className = cls; el.textContent = value ?? ""; return el; };

function toast(message, kind) {
  const el = $("toast");
  el.textContent = message; el.className = "toast" + (kind ? " " + kind : ""); el.hidden = false;
  clearTimeout(toastTimer); toastTimer = setTimeout(() => { el.hidden = true; }, 5000);
}

function show(next) {
  view = state.paired ? (next === "connect" ? "alarms" : next) : "connect";
  for (const key of Object.keys(VIEWS)) $("view" + key[0].toUpperCase() + key.slice(1)).hidden = key !== view;
  document.querySelectorAll("#nav button").forEach(b => {
    b.disabled = !state.paired;
    if (b.dataset.view === view) b.setAttribute("aria-current", "page"); else b.removeAttribute("aria-current");
  });
  $("viewTitle").textContent = VIEWS[view];
  $("refreshBtn").hidden = view === "connect";
  $("viewSub").textContent = view === "alarms" ? "Active alarms on PROGNODE Core" : view === "history" ? "Most recent first" : "";
  if (view === "history") send({ cmd: "history", offset: historyOffset });
}

// ---------- state from the app ----------
function renderState() {
  const c = state.connection || {};
  const pill = $("sidePill");
  pill.className = "pill " + (state.paired ? (c.online ? "online" : "offline") : "");
  $("sidePillText").textContent = !state.paired ? "Not connected" : c.online ? "Connected" : "Core not reachable";
  $("sidePc").textContent = state.paired ? state.pcName || "" : "";
  $("sideCore").textContent = state.server ? `Core: ${state.server.name} · ${state.server.host}` : "";
  $("sideVersion").textContent = state.version ? `Version ${state.version}` : "";
  $("autostart").checked = !!state.autostart;
  if (state.server) {
    $("connCore").textContent = state.server.name;
    $("connAddress").textContent = `${state.server.host}:${state.server.port}`;
  }
  $("connStatus").textContent = c.message || "—";
  $("connLast").textContent = c.lastContactUtc ? ago(c.lastContactUtc) : "—";
  const a = state.access;
  $("connAccess").textContent = !a ? "—" : a.canAcknowledge && a.deviceAck ? `${state.pcName || "This PC"} · may view and acknowledge alarms` : `${state.pcName || "This PC"} · may view alarms`;
  $("ackHint").textContent = a && !a.deviceAck ? "View only. A Core administrator can allow acknowledgement for this PC in Settings › Server & QR › Paired devices." : "";
  renderAlarms();
}

function ackState(alarm) {
  if (alarm.requiresAcknowledgement === false) return ["No ACK needed", "noack"];
  if (alarm.acknowledgedAt) return [`Acknowledged${alarm.acknowledgedBy ? " · " + alarm.acknowledgedBy : ""}`, "acked"];
  return ["Awaiting ACK", "awaiting"];
}

function renderAlarms() {
  const rows = $("alarmRows"); rows.replaceChildren();
  const order = { Critical: 0, High: 1, Medium: 2, Low: 3 };
  const alarms = [...(state.alarms || [])].sort((a, b) => (order[a.priority] ?? 4) - (order[b.priority] ?? 4) || new Date(b.activeSince) - new Date(a.activeSince));
  const waiting = alarms.filter(a => ackState(a)[1] === "awaiting").length;
  $("navCount").hidden = !alarms.length; $("navCount").textContent = String(alarms.length);
  if (!alarms.length) {
    const td = document.createElement("td"); td.colSpan = 5; td.className = "empty";
    td.append(text("strong", "No active alarms"), text("span", state.connection?.online === false ? "Core is not reachable right now; the list updates when it is back." : "New alarms appear here and as Windows notifications."));
    const tr = document.createElement("tr"); tr.append(td); rows.append(tr);
    return;
  }
  for (const alarm of alarms) {
    const tr = document.createElement("tr");
    if (alarm.occurrenceId === selected) tr.className = "selected";
    const [label, cls] = ackState(alarm);
    const td1 = document.createElement("td"); td1.append(text("span", alarm.priority, "prio " + alarm.priority));
    const td2 = document.createElement("td"); td2.append(text("strong", alarm.text), text("span", alarm.sourceName, "sub"));
    const td3 = text("td", fmt(alarm.activeSince), "num");
    const td4 = document.createElement("td"); td4.append(text("span", label, "state " + cls));
    const td5 = document.createElement("td");
    if (cls === "awaiting" && state.access?.deviceAck) {
      const b = text("button", "ACK", "btn small primary"); b.type = "button";
      b.addEventListener("click", () => { b.disabled = true; send({ cmd: "ack", id: alarm.occurrenceId }); });
      td5.append(b);
    }
    tr.append(td1, td2, td3, td4, td5);
    rows.append(tr);
  }
  $("viewSub").textContent = view === "alarms" ? `${alarms.length} active${waiting ? ` · ${waiting} awaiting ACK` : ""}` : $("viewSub").textContent;
}

function renderHistory(msg) {
  const rows = $("historyRows"); rows.replaceChildren();
  historyTotal = msg.total || 0;
  if (msg.error) { const td = text("td", msg.error, "empty"); td.colSpan = 5; rows.append(document.createElement("tr")); rows.lastChild.append(td); }
  else if (!msg.items?.length) { const td = document.createElement("td"); td.colSpan = 5; td.className = "empty"; td.append(text("strong", "No alarm history yet")); rows.append(document.createElement("tr")); rows.lastChild.append(td); }
  for (const item of msg.items || []) {
    const tr = document.createElement("tr");
    const td1 = document.createElement("td"); td1.append(text("span", item.priority, "prio " + item.priority));
    const td2 = document.createElement("td"); td2.append(text("strong", item.text), text("span", item.sourceName, "sub"));
    const td5 = document.createElement("td");
    if (item.acknowledgedAt) td5.append(text("span", item.acknowledgedBy || "Acknowledged", "state acked"), text("span", fmt(item.acknowledgedAt), "sub"));
    else td5.append(text("span", "—", "muted"));
    tr.append(td1, td2, text("td", fmt(item.activeAt), "num"), text("td", item.clearedAt ? fmt(item.clearedAt) : "Still active", "num"), td5);
    rows.append(tr);
  }
  const from = historyTotal ? historyOffset + 1 : 0, to = Math.min(historyOffset + (msg.items?.length || 0), historyTotal);
  $("historyInfo").textContent = `${from}–${to} of ${historyTotal}`;
  $("historyPrev").disabled = historyOffset === 0;
  $("historyNext").disabled = to >= historyTotal;
}

// ---------- connect flow ----------
function renderCores(items) {
  const list = $("coreList"); list.replaceChildren();
  if (!items.length) { list.append(text("p", "No Core answered on this network yet. Enter its address below, or check that the Core PC is on.", "note")); return; }
  items.forEach((core, i) => {
    const b = document.createElement("button"); b.type = "button"; b.className = "core-option";
    b.append(text("strong", core.displayName), text("span", core.address, "muted num"));
    b.addEventListener("click", () => {
      list.querySelectorAll(".core-option").forEach(x => x.setAttribute("aria-pressed", "false"));
      b.setAttribute("aria-pressed", "true");
      $("hostInput").value = core.address; $("portInput").value = core.securePort; $("hostInput").dataset.serverId = core.serverId;
    });
    if (i === 0) b.click();
    list.append(b);
  });
}

function connectStatus(message, error) { const el = $("connectStatus"); el.textContent = message || ""; el.className = "status" + (error ? " error" : ""); }
function resetVerify() { candidate = null; $("verifyStep").hidden = true; $("pairStep").hidden = true; $("codeMatches").checked = false; $("pairBtn").disabled = true; }

window.chrome?.webview?.addEventListener("message", e => receive(e.data));
function receive(msg) {
  switch (msg.type) {
    case "state": state = { ...state, ...msg }; if (!state.paired && view !== "connect") show("connect"); else if (state.paired && view === "connect") show("alarms"); renderState(); break;
    case "history": renderHistory(msg); break;
    case "select": selected = msg.occurrenceId; show("alarms"); renderAlarms(); break;
    case "discovered": $("searchBtn").disabled = false; renderCores(msg.items || []); connectStatus(msg.items?.length ? "Select your Core and choose Continue." : ""); break;
    case "candidate":
      $("inspectBtn").disabled = false;
      if (msg.error) { connectStatus(msg.error, true); break; }
      candidate = msg; $("checkCode").textContent = msg.checkCode; $("verifyStep").hidden = false; $("pairStep").hidden = false;
      connectStatus(`Reached ${msg.displayName}. Compare the code, then enter the pairing code.`);
      break;
    case "paired": $("pairBtn").disabled = false; if (msg.error) connectStatus(msg.error, true); else { connectStatus(""); toast("This PC is now connected to PROGNODE Core.", "ok"); } break;
    case "toast": toast(msg.text, msg.kind); if (msg.reenable) document.querySelectorAll(`#${msg.reenable}`).forEach(b => b.disabled = false); break;
  }
}

// ---------- commands ----------
$("nav").addEventListener("click", e => { const b = e.target.closest("button[data-view]"); if (b && !b.disabled) { if (b.dataset.view === "history") historyOffset = 0; show(b.dataset.view); } });
$("cordaraLink").addEventListener("click", e => { e.preventDefault(); send({ cmd: "openCordara" }); });
$("refreshBtn").addEventListener("click", () => view === "history" ? send({ cmd: "history", offset: historyOffset }) : send({ cmd: "refresh" }));
$("historyPrev").addEventListener("click", () => { historyOffset = Math.max(0, historyOffset - 50); send({ cmd: "history", offset: historyOffset }); });
$("historyNext").addEventListener("click", () => { historyOffset += 50; send({ cmd: "history", offset: historyOffset }); });
$("checkBtn").addEventListener("click", () => { $("checkBtn").disabled = true; send({ cmd: "check" }); });
$("reconnectBtn").addEventListener("click", () => { $("reconnectBtn").disabled = true; send({ cmd: "reconnect" }); });
$("testToastBtn").addEventListener("click", () => send({ cmd: "testNotification" }));
$("autostart").addEventListener("change", e => send({ cmd: "autostart", value: e.target.checked }));
$("disconnectBtn").addEventListener("click", () => { $("disconnectConfirm").hidden = false; });
$("disconnectNo").addEventListener("click", () => { $("disconnectConfirm").hidden = true; });
$("disconnectYes").addEventListener("click", () => { $("disconnectConfirm").hidden = true; send({ cmd: "disconnect" }); });

$("searchBtn").addEventListener("click", () => { $("searchBtn").disabled = true; connectStatus("Searching the plant network…"); resetVerify(); send({ cmd: "discover" }); });
$("hostInput").addEventListener("input", () => { delete $("hostInput").dataset.serverId; resetVerify(); });
$("inspectBtn").addEventListener("click", () => {
  const address = $("hostInput").value.trim(), port = Number($("portInput").value);
  if (!address) return connectStatus("Select a Core or enter its address.", true);
  if (!Number.isInteger(port) || port < 1 || port > 65535) return connectStatus("Enter a valid HTTPS port (default 5443).", true);
  resetVerify(); $("inspectBtn").disabled = true; connectStatus("Checking the Core certificate…");
  send({ cmd: "inspect", host: address, port, serverId: $("hostInput").dataset.serverId || null });
});
const pairReady = () => { $("pairBtn").disabled = !(candidate && $("codeMatches").checked && /^\d{6}$/.test($("pairCode").value.trim())); };
$("codeMatches").addEventListener("change", pairReady);
$("pairCode").addEventListener("input", pairReady);
$("pairBtn").addEventListener("click", () => {
  $("pairBtn").disabled = true; connectStatus("Pairing…");
  send({ cmd: "pair", code: $("pairCode").value.trim(), name: $("pcName").value.trim() });
});

// ---------- start ----------
setInterval(() => { if (state.connection?.lastContactUtc) $("connLast").textContent = ago(state.connection.lastContactUtc); }, 5000);
show(view);
send({ cmd: "ready" });

// Plain-browser preview with sample data (never used inside the app).
function preview(msg) {
  if (msg.cmd !== "ready") return;
  const now = Date.now(), iso = m => new Date(now - m * 60000).toISOString();
  receive({ type: "state", paired: true, version: "1.0.0 (preview)", pcName: "Line 2 office PC", autostart: true,
    server: { name: "PROGNODE-430584", host: "192.168.8.4", port: 5443 },
    connection: { online: true, message: "Connected · 192.168.8.4", lastContactUtc: new Date(now - 3000).toISOString() },
    access: { canViewAlarms: true, canAcknowledge: true, deviceAck: true },
    alarms: [
      { occurrenceId: "a", priority: "Critical", text: "Press 3 oil temperature high", sourceName: "Press line PLC", activeSince: iso(4) },
      { occurrenceId: "b", priority: "High", text: "Compressor pressure low", sourceName: "Utility PLC", activeSince: iso(19), acknowledgedAt: iso(12), acknowledgedBy: "Ayşe K." },
      { occurrenceId: "c", priority: "Medium", text: "Filter change due", sourceName: "Utility PLC", activeSince: iso(55), requiresAcknowledgement: false } ] });
}
