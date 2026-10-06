// RC6.4.2 deterministic tests against the shipped, unmodified Trend Studio module.
const fs=require('fs'),vm=require('vm'),assert=require('node:assert/strict'),path=require('node:path');
const root=path.join(__dirname,'../src/Prognode.Host/wwwroot');
const source=fs.readFileSync(path.join(root,'trend-studio.js'),'utf8');
const uiContext={
  state:{language:'tr',theme:'dark',trends:[],selectedTrendId:null,tags:[{id:'temp',name:'Temperature',unit:'°C'}],devices:[],hiddenTrendTags:new Set(),historianConfigurations:[]},
  window:{addEventListener:()=>{}},
  document:{getElementById:()=>null},
  Date, Math, Number, Set, Map, console, requestAnimationFrame:f=>f()
};
vm.createContext(uiContext);vm.runInContext(source,uiContext);
const studio=uiContext.window.PrognodeStudio,test=studio._test;
studio.selected=['temp'];
studio.events.definitions=[
 {id:'hi',tagId:'temp',condition:'GreaterThanOrEqual',threshold:30,enabled:true,text:'High'},
 {id:'lo',tagId:'temp',condition:'LessThanOrEqual',threshold:10,enabled:true,text:'Low'},
 {id:'off',tagId:'temp',condition:'GreaterThan',threshold:1000,enabled:false},
 {id:'dig',tagId:'temp',condition:'DigitalEquals',threshold:2000,enabled:true}
];
const limits=test.numericLimits('temp');
assert.equal(limits.length,2,'only enabled numeric alarm thresholds');
assert.equal(test.valueZone(13,limits),'NORMAL');
assert.equal(test.valueZone(30,limits),'HIGH');
assert.equal(test.valueZone(32,limits),'HIGH');
assert.equal(test.valueZone(10,limits),'LOW');
assert.equal(test.valueZone(8,limits),'LOW');
const now=Date.now(),payload={from:new Date(now-3600000).toISOString(),to:new Date(now).toISOString(),series:[{tagId:'temp',points:[{timestamp:new Date(now-300000).toISOString(),quality:'GOOD',value:13}]}]};
const axis=test.buildAxes(payload,1200,450).axis.left;
assert(Math.abs(axis.min-8)<1e-6&&Math.abs(axis.max-32)<1e-6,'13 sample, thresholds 10/30 → Y=8..32');
// Hiding reference lines is only a presentation choice and must not alter autoscaling.
const calls=[];
const ctx={beginPath(){},moveTo(){},lineTo(){},stroke(){calls.push(this.strokeStyle)}};
test.drawThresholdSegments(ctx,0,31,100,9,v=>v,limits,'#38d5e5');
assert.equal(calls.length,3,'line precisely split at both threshold crossings');
assert.equal(calls[0],'#ff6c82','High segment red');
assert.equal(calls[1],'#38d5e5','Normal segment retains tag color');
assert.equal(calls[2],'#f6af5c','Low segment amber');
assert.equal(test.valueZone(13,[]),'NORMAL','tags with no numeric alarms keep normal color');
assert.equal(test.eventKey({alarmKey:'A',activeAt:'2026-09-25T01:00:00Z'}),'A|2026-09-25T01:00:00Z');
const html=fs.readFileSync(path.join(root,'index.html'),'utf8'),app=fs.readFileSync(path.join(root,'app.js'),'utf8');
for(const id of ['navAlarmCount','navTrendCount','navHistorianCount','studioEventDetail','studioCompareBar'])assert(html.includes('id="'+id+'"'),id+' visible');
assert(app.includes('String(state.alarmDefinitions.length)'),'alarm nav shows total definitions');
assert(app.includes('const available = state.historianConfigurations.filter(x=>x.configuration?.enabled===true).length;'),'trend nav shows available recorded signals');
assert(app.includes('configuration?.enabled===true'),'historian nav counts explicitly enabled configs');
assert(!html.includes('id="studioResetCursors"'),'no duplicate ID in A/B panels');
console.log('PASS: alarm-aware 8..32 scale, disabled/digital excluded, normal/high/low split, nav counts, A/B/event UI contract');
