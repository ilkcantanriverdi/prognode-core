// The API endpoints as one text, in their former single-file order, for the static checks that read them.
const fs = require('node:fs');
const path = require('node:path');
const dir = path.join(__dirname, '../../src/Prognode.Web');
const parts = ['Pairing', 'License', 'RemoteAccess', 'Devices', 'Tags', 'Batches', 'Alarms', 'Trends',
  'Historian', 'DataExchange', 'System', 'Mobile'];
const files = [...parts.map(p => `EndpointExtensions.${p}.cs`), 'EndpointExtensions.cs'];
const extra = fs.readdirSync(dir).filter(f => /^EndpointExtensions\..+\.cs$/.test(f) && !files.includes(f));
if (extra.length) throw new Error(`Add ${extra.join(', ')} to tests/lib/web-endpoints.cjs`);
module.exports = files.map(f => fs.readFileSync(path.join(dir, f), 'utf8')).join('\n');
