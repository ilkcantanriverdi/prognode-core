const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const root=path.join(__dirname,'../src/Prognode.Host/wwwroot');
const source=fs.readFileSync(path.join(root,'app.js'),'utf8');
const html=fs.readFileSync(path.join(root,'index.html'),'utf8');
const css=fs.readFileSync(path.join(root,'styles.css'),'utf8');
for(const id of ['alarmActiveSearch','alarmActivePriority','alarmActiveState','alarmActivePageSize','alarmActivePager',
  'alarmDefinitionSearch','alarmDefinitionPriority','alarmDefinitionPageSize','alarmDefinitionPager',
  'alarmHistorySearch','alarmHistoryPriority','alarmHistoryPageSize','alarmHistoryPager'])
  assert(html.includes(`id="${id}"`),`missing ${id}`);
for(const group of ['active','definition','history'])assert(html.includes(`data-alarm-group="${group}"`),`missing ${group} accordion`);
assert(css.includes('.alarm-group-body.collapsed'));
function section(start,end){const a=source.indexOf(start),b=source.indexOf(end,a+start.length);assert(a>=0&&b>a);return source.slice(a,b)}
const elements=new Map();
function $(id){if(!elements.has(id))elements.set(id,{value:'',innerHTML:'',textContent:'',disabled:false,dataset:{},
  classList:{toggle:()=>{},remove:()=>{}},addEventListener:()=>{},querySelectorAll:()=>[],closest:()=>null});return elements.get(id)}
const devices=[{id:'d1',name:'Line 1'}],tags=[{id:'t1',deviceId:'d1',name:'Pressure'}];
const definitions=Array.from({length:27},(_,i)=>({id:`a${i}`,tagId:'t1',text:`Alarm ${i}`,priority:i%2?'High':'Low',condition:'GreaterThan',threshold:10,deadband:0,delayOnMs:0,delayOffMs:0}));
const active=Array.from({length:13},(_,i)=>({alarmKey:`k${i}`,deviceId:'d1',sourceName:'Pressure',text:`Active ${i}`,priority:i%2?'Critical':'High',state:i%3?'Active':'Acknowledged',activeSince:'2026-09-30T10:00:00Z'}));
const history=Array.from({length:31},(_,i)=>({deviceId:'d1',sourceName:'Pressure',text:`History ${i}`,priority:i%2?'Medium':'High',state:'Cleared',activeAt:'2026-09-30T10:00:00Z',clearedAt:'2026-09-30T10:00:05Z'}));
const state={language:'en',devices,tags,alarmDefinitions:definitions,activeAlarms:active,
  alarmHistory:history.slice(0,10),alarmHistoryCount:31,alarmHistoryFilteredCount:31,
  alarmActivePage:1,alarmDefinitionPage:1,alarmHistoryPage:1};
const context={state,$,document:{querySelectorAll:()=>[],title:'',getElementById:()=>({id:'',className:'',innerHTML:'',classList:{toggle(){}},querySelector:()=>({}),querySelectorAll:()=>[]}),createElement:()=>({id:'',className:'',innerHTML:'',classList:{toggle(){}},querySelector:()=>({}),querySelectorAll:()=>[]})},escapeHtml:x=>String(x),t:x=>x,Date,console,applyAccessMode:()=>{context.accessReapplied=(context.accessReapplied||0)+1}};
vm.createContext(context);
vm.runInContext(section('function tablePage(', 'function renderDevices('),context);
vm.runInContext(section('function alarmTag(', 'function populateAlarmTagSelect('),context);
for(const prefix of ['alarmActive','alarmDefinition','alarmHistory'])$(prefix+'PageSize').value='10';
context.renderAlarms();
assert.equal($('alarmActivePageInfo').textContent,'1 / 2');
assert.equal($('alarmDefinitionPageInfo').textContent,'1 / 3');
assert.equal($('alarmHistoryPageInfo').textContent,'1 / 4');
assert.equal(($('alarmActiveTableBody').innerHTML.match(/<tr>/g)||[]).length,10);
assert.equal(($('alarmDefinitionTableBody').innerHTML.match(/<tr>/g)||[]).length,10);
assert.equal(($('alarmHistoryTableBody').innerHTML.match(/<tr>/g)||[]).length,10);
const stableKey=$('alarmDefinitionTableBody').dataset.renderKey;
context.renderAlarms();
assert.equal($('alarmDefinitionTableBody').dataset.renderKey,stableKey);
assert.equal(context.accessReapplied,1,'unchanged alarm definitions should not be rebuilt every live poll');
$('alarmActivePriority').value='Critical';context.renderAlarms();
assert.equal($('alarmActiveMeta').textContent,'6 / 13');
$('alarmDefinitionSearch').value='alarm 26';context.renderAlarms();
assert.equal($('alarmDefinitionMeta').textContent,'1 / 27');
$('alarmHistoryPriority').value='Medium';state.alarmHistory=history.filter(x=>x.priority==='Medium').slice(0,10);
state.alarmHistoryFilteredCount=15;context.renderAlarms();
assert.equal($('alarmHistoryMeta').textContent,'15 / 31');
assert(source.includes('classList.toggle("collapsed")')&&source.includes('setAttribute("aria-expanded"'));
console.log('PASS: three alarm accordions, filters and independent page-size pagination');
