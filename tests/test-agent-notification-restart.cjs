// Windows notifications: a restart does not show old notifications again, and a click while
// PROGNODE is already open brings that window forward instead of opening one more tab.
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const root=path.join(__dirname,'../src');
const agent=fs.readFileSync(path.join(root,'Prognode.Agent.Windows/AgentApplicationContext.cs'),'utf8');
const windows=fs.readFileSync(path.join(root,'Prognode.Agent.Windows/BrowserWindows.cs'),'utf8');
const app=fs.readFileSync(path.join(root,'Prognode.Host/wwwroot/app.js'),'utf8');
const web=require('./lib/web-endpoints.cjs');

// Restart: the cursor is kept on disk; no cursor means "start after the newest event".
assert(agent.includes('"PROGNODE", "Agent", "notification-cursor"'),'cursor must be stored per user');
assert(agent.includes('_lastNotificationId = LoadCursor();'),'cursor must be loaded at start');
assert(/if \(_lastNotificationId is null \|\| _lastNotificationId > newest\)\s*SaveCursor\(newest\);/.test(agent),
  'first start or replaced database must begin after the newest event');
assert(agent.includes('MaxNotificationAge')&&agent.includes('.Where(x => DateTimeOffset.UtcNow - x.Timestamp < MaxNotificationAge)'),
  'events that waited too long are not shown as pop-ups');
assert(!agent.includes('"Agent Started"'),'no notification on every sign-in');

// Click: bring the open PROGNODE window forward, then let the page switch to Alarms.
const openAlarms=agent.slice(agent.indexOf('private void OpenAlarms()'),agent.indexOf('private async Task RequestOpenAlarmsAsync()'));
assert(openAlarms.indexOf('BrowserWindows.TryActivatePrognode()')<openAlarms.indexOf('Process.Start('),'existing window first');
assert(agent.includes('"/api/agent/open-alarms"')&&web.includes('"/api/agent/open-alarms"'),'open-alarms request');
assert(/"\/api\/agent\/open-alarms",[\s\S]{0,120}IsLoopbackRequest\(context\)/.test(web),'only from this PC');

const pattern=windows.match(/new\(\s*@"([^"]+)"/)[1];
const title=new RegExp(pattern);
for(const t of ['PROGNODE','(3) PROGNODE - Google Chrome','PROGNODE and 2 more pages - Personal - Microsoft​ Edge',
  '(1) PROGNODE — Mozilla Firefox','PROGNODE - Work - Microsoft Edge'])assert(title.test(t),`should match: ${t}`);
for(const t of ['PROGNODE — Industrial Monitoring for Alarms, Historian & Trends - Google Chrome',
  'PROGNODE Account - Google Chrome','Pricing — PROGNODE - Google Chrome','PROGNODE Client'])assert(!title.test(t),`must not match: ${t}`);

const begin=app.indexOf('async function loadAgentStatus()');
const end=app.indexOf('function renderDiagnostics()',begin);
let opened=0,status={openAlarmsRequest:4};
const context={state:{},navigate:p=>{if(p==='alarms')opened++;},renderAgentStatus:()=>{},api:async()=>status};
vm.createContext(context);vm.runInContext(app.slice(begin,end),context);
(async()=>{
  await context.loadAgentStatus();
  assert.equal(opened,0,'an old request is not replayed when the page loads');
  status={openAlarmsRequest:5};await context.loadAgentStatus();
  assert.equal(opened,1,'a new request shows Alarms');
  await context.loadAgentStatus();
  assert.equal(opened,1,'shown once');
  console.log('PASS: Agent keeps its notification cursor across restarts and reuses an open PROGNODE window');
})().catch(e=>{console.error(e);process.exit(1);});
