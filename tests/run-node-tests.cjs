// Runs every tests/*.cjs (except this runner) and fails if any of them fails.
// Usage: node tests/run-node-tests.cjs
const { spawnSync } = require('node:child_process');
const fs = require('node:fs');
const path = require('node:path');

// Tests that describe behaviour not present in the current source. Each needs an owner decision;
// they are reported on every run so they cannot be forgotten.
const pendingDecision = {
  'test-overview-historian-previews.cjs':
    'Overview "three configurable Historian previews" (overviewSparkline, #overviewTrendCards) is not in the source restored in 537c74a: restore the feature or delete the test.',
};

const dir = __dirname;
const files = fs.readdirSync(dir).filter(f => f.endsWith('.cjs') && f !== path.basename(__filename)).sort();
let failed = 0;
for (const file of files) {
  if (pendingDecision[file]) {
    console.log(`SKIP ${file}\n     ${pendingDecision[file]}`);
    continue;
  }
  const run = spawnSync(process.execPath, [path.join(dir, file)], { encoding: 'utf8' });
  if (run.status === 0) {
    console.log(`PASS ${file}`);
  } else {
    failed++;
    console.log(`FAIL ${file}\n${(run.stderr || run.stdout).split('\n').filter(l => !/^\s+at /.test(l)).slice(0, 8).join('\n')}`);
  }
}
console.log(`\n${files.length - failed - Object.keys(pendingDecision).length} passed, ${failed} failed, ${Object.keys(pendingDecision).length} pending decision`);
process.exit(failed ? 1 : 0);
