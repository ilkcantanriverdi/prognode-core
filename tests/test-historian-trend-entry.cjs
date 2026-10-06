const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const root=path.join(__dirname,'../src/Prognode.Host/wwwroot');
const shell=fs.readFileSync(path.join(root,'app.js'),'utf8');
const trend=fs.readFileSync(path.join(root,'trend-hf3plus.js'),'utf8');
const html=fs.readFileSync(path.join(root,'trend-hf3plus.html'),'utf8');
assert(trend.indexOf('const byId=id=>document.getElementById(id)')<trend.indexOf("byId('fullscreen-alarm-indicator').addEventListener"),'DOM helper must initialize before first use');
assert(shell.includes('data-open-historian-trend="${c.tagId}"'));
assert(shell.includes('sessionStorage.setItem("prognode.pendingHistorianTrendTag", tagId)'));
assert(shell.includes('navigate("trends")'));
assert(trend.includes("if(e.data?.type==='pgn:open-historian-tag'"));
assert(trend.includes("sessionStorage.getItem('prognode.pendingHistorianTrendTag')"));
assert(trend.includes('appendSignals([id])'));
assert(html.includes('id="available-signal-count"'));
assert(trend.includes("byId('available-signal-count').textContent=String(signalDefs.length)"));
const start=trend.indexOf('async function loadCatalog(){');
const end=trend.indexOf('async function consumePendingHistorianTag(){',start);
assert(start>=0&&end>start);
const tags=Array.from({length:100},(_,i)=>({id:`T${i+1}`,deviceId:'PLC',name:`Tag ${i+1}`,dataType:'Real'}));
const recordings=tags.map(t=>({configuration:{tagId:t.id,enabled:true,sampleIntervalSeconds:10},lastValue:42}));
let emptyCatalog=false;
const dom={'hf61-source':{textContent:''}};
let alarmAttempts=0;
const context={
  Promise,Map,Set,Date,Math,Number,String,console,
  signalDefs:[],allData:new Map(),metaById:new Map(),catalogPromise:null,
  Core:{ready:false,error:'',lastCatalogAt:0},
  S:{charts:[],activeId:null,lang:'tr'},
  palette:['#abc'],
  api:async url=>{
    if(url==='/api/tags')return emptyCatalog?[]:tags;
    if(url==='/api/devices')return [{id:'PLC',name:'PLC'}];
    if(url==='/api/historian/configurations')return emptyCatalog?[]:recordings;
    if(url==='/api/alarms/definitions'){alarmAttempts++;throw Error('Alarm service unavailable')}
    throw Error(`Unexpected URL: ${url}`);
  },
  restoreLayout:async()=>{},renderLayout:()=>{},renderDrawer:()=>{},applyTrendPermissions:()=>{},
  consumePendingHistorianTag:async()=>{},fetchSeries:async()=>{},toast:()=>{},
  byId:id=>dom[id]||null,
};
vm.createContext(context);
vm.runInContext(trend.slice(start,end),context);
(async()=>{
  await context.loadCatalog();
  assert.equal(context.Core.ready,true,'alarm outage must not block Historian catalog');
  assert.equal(context.signalDefs.length,100,'all 100 recorded Tags must be available');
  assert.equal(dom['hf61-source'].textContent,'100 HISTORIAN SİNYALİ');
  assert.equal(alarmAttempts,1);
  emptyCatalog=true;
  await context.loadCatalog();
  assert.equal(context.signalDefs.length,100,'transient empty response must not erase available signals');
  emptyCatalog=false;
  await context.loadCatalog();
  assert.equal(context.Core.ready,true,'catalog must recover on retry');
  assert(trend.includes('void loadCatalog();\nwindow.addEventListener(\'DOMContentLoaded\''),'catalog must start even if iframe visibility message is lost');
  assert(trend.includes('if(!Core.ready||Date.now()-Core.lastCatalogAt>15000)void loadCatalog()'),'failed catalog must retry');
  console.log('PASS: 100 Historian Tags load despite alarm outage; available and open counts are distinct');
})().catch(error=>{console.error(error);process.exitCode=1});
