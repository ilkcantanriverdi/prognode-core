const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const web=fs.readFileSync(path.join(__dirname,'../src/Prognode.Web/EndpointExtensions.cs'),'utf8');
const start=web.indexOf('"/api/data-exchange/alarm-history.csv"');
const end=web.indexOf('"/api/data-exchange/historian-configurations.csv"',start);
const exportRoutes=web.slice(start,end);
assert(start>=0&&end>start);
assert.equal((exportRoutes.match(/LoadAllAlarmOccurrencesAsync\(alarms, ct\)/g)||[]).length,2,
  'CSV and XLSX must both load every occurrence');
assert(!exportRoutes.includes('GetHistoryAsync(500'));
assert.match(web,/LoadAllAlarmOccurrencesAsync\(AlarmService alarms, CancellationToken ct\)[\s\S]*?GetHistoryPageAsync\(rows.Count, pageSize/);
console.log('PASS: alarm CSV and XLSX exports page all occurrences beyond 500');
