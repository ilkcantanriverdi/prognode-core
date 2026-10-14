// The product is called PROGNODE Monitor; "Core" stays an internal name (service PROGNODECore,
// PROGNODE.Core.exe, folders, API paths) and must not appear in text people see.
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");

const root = path.join(__dirname, "..");
const wwwroot = "src/Prognode.Host/wwwroot";
const files = [
  ...fs.readdirSync(path.join(root, wwwroot)).filter(f => /\.(html|js)$/.test(f)).map(f => `${wwwroot}/${f}`),
  "src/Prognode.Client.Windows/Assets/ui/index.html",
  "src/Prognode.Client.Windows/Assets/ui/client.js",
  "src/Prognode.Agent.Windows/AgentApplicationContext.cs",
  "src/Prognode.Client.Windows/ClientContext.cs",
  "src/Prognode.Client.Windows/CoreConnection.cs",
  "installer/PROGNODE-Monitor.iss",
];

// A visible "Core": the whole word, not code such as Core.ready or a path.
const visibleCore = /(?<![\w.$/-])Core(?![\w(:/-])(?!\.\w)/;
const technical = /PROGNODECore|PROGNODE\.Core\.exe|LAN Core HTTPS|Prognode\.Core|\/api\/|https?:\/\//;
const isText = s => /\s/.test(s.trim()) && !technical.test(s) && !/\bCore\.\w|\w\.Core\b/.test(s);

function literals(line, quotes) {
  const out = [];
  for (const q of quotes) {
    const re = new RegExp(`${q === "`" ? "`" : q}((?:\\\\.|[^${q}\\\\])*)${q === "`" ? "`" : q}`, "g");
    for (const m of line.matchAll(re)) out.push(m[1]);
  }
  return out;
}

const found = [];
for (const rel of files) {
  const raw = fs.readFileSync(path.join(root, rel), "utf8").replace(/\r\n/g, "\n");
  const ext = path.extname(rel);
  const check = (piece, line) => { if (isText(piece) && visibleCore.test(piece)) found.push(`${rel}:${line}: ${piece.trim().slice(0, 120)}`); };
  if (ext === ".html") {
    // Visible HTML text and attributes outside <script>/<style>; inline scripts as JS lines.
    const scripts = [];
    const html = raw.replace(/<(script|style)\b[^>]*>([\s\S]*?)<\/\1>/g, (m, tag, body) => {
      if (tag === "script") scripts.push(body);
      return `<${tag}></${tag}>`;
    });
    html.split("\n").forEach((line, i) => {
      for (const m of line.matchAll(/>([^<>]+)</g)) check(m[1], i + 1);
      for (const m of line.matchAll(/\b(?:title|aria-label|placeholder|alt|data-customer-en)="([^"]*)"/g)) check(m[1], i + 1);
    });
    scripts.join("\n").split("\n").forEach((line, i) => literals(line, ['"', "'", "`"]).forEach(p => check(p, `script ${i + 1}`)));
  } else {
    const code = ext === ".cs" || ext === ".iss";
    raw.split("\n").forEach((line, i) => {
      if (/^\s*(\/\/|;)/.test(line)) return; // comments
      literals(line, code ? ['"'] : ['"', "'", "`"]).forEach(p => check(p, i + 1));
    });
  }
}
assert.deepEqual(found, [], `"Core" is visible to users:\n${found.join("\n")}`);
console.log(`PASS: no visible "Core" in ${files.length} UI files (product name PROGNODE Monitor)`);
