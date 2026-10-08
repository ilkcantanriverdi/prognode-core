// Every HTTP method + route template is mapped exactly once. A duplicate MapPost made bulk alarm
// history delete fail with AmbiguousMatchException (review Y1) and no test called that route.
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const src = path.join(__dirname, '../src');
const files = [
  'Prognode.Web/EndpointExtensions.cs',
  'Prognode.Host/Program.cs',
].map(f => path.join(src, f));

const seen = new Map();
let count = 0;
for (const file of files) {
  const text = fs.readFileSync(file, 'utf8');
  const re = /\.Map(Get|Post|Put|Delete|Patch)\(\s*"([^"]+)"/g;
  for (let m; (m = re.exec(text));) {
    const key = `${m[1].toUpperCase()} ${m[2].replace(/\{([^}:]+)(:[^}]+)?\}/g, '{$1}').toLowerCase()}`;
    const where = `${path.basename(file)}:${text.slice(0, m.index).split('\n').length}`;
    assert(!seen.has(key), `${key} is mapped twice: ${seen.get(key)} and ${where}`);
    seen.set(key, where);
    count++;
  }
}
assert(count > 100, `expected the Core API surface, found only ${count} routes`);
console.log(`PASS: ${count} Core routes, each method + template mapped once`);
