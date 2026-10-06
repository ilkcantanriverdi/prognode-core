const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const app = fs.readFileSync(path.join(__dirname, '../src/Prognode.Host/wwwroot/app.js'), 'utf8');
const start = app.indexOf('async function refreshAlarmHistory()');
const end = app.indexOf('/* =========================\n   Trend + Historian', start);
assert(start >= 0 && end > start);
const controls = {
  alarmHistoryPageSize:{value:'25'},alarmHistorySearch:{value:''},
  alarmHistoryPriority:{value:''}
};
const state = {alarmHistoryPage:5,alarmHistoryRequestId:0,alarmHistory:[],
  alarmHistoryCount:0,alarmHistoryFilteredCount:0};
const calls = [];
const context = {state,$:id=>controls[id],console,renderAlarms:()=>{},
  api:async route=>{
    calls.push(route);
    if(route.endsWith('/count'))return {count:105};
    const query = new URL('http://local'+route).searchParams;
    const offset=Number(query.get('offset'));
    const filtered=query.get('search')==='repeat' ? 102 : 105;
    return {total:filtered,items:Array.from({length:Math.max(0,Math.min(25,filtered-offset))},
      (_,index)=>({occurrenceId:`occ-${offset+index}`}))};
  }};
vm.createContext(context);
vm.runInContext(app.slice(start,end),context);
(async()=>{
  await context.refreshAlarmHistory();
  assert.equal(state.alarmHistoryCount,105);
  assert.equal(state.alarmHistory.length,5);
  assert.equal(state.alarmHistory[0].occurrenceId,'occ-100');
  controls.alarmHistorySearch.value='repeat';
  await context.refreshAlarmHistory();
  assert.equal(state.alarmHistoryFilteredCount,102);
  assert.equal(state.alarmHistory.length,2);
  assert(calls.some(url=>url.includes('offset=100')&&url.includes('search=repeat')));
  console.log('PASS: alarm history pages beyond 100 and filters across stored occurrences');
})().catch(error=>{console.error(error);process.exitCode=1});
