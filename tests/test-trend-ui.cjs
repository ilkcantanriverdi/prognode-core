const fs=require('fs'),vm=require('vm'),assert=require('node:assert/strict'),path=require('node:path');
const source=fs.readFileSync(path.join(__dirname,'../src/Prognode.Host/wwwroot/trend-studio.js'),'utf8');
function el(id){const events={},classes=new Set(id==='studioEventDetail'||id==='studioCompareBar'?'hidden':[]);
  return {id,events,style:{},dataset:{},classList:{add:k=>classes.add(k),remove:k=>classes.delete(k),contains:k=>classes.has(k),toggle(k,force){if(force===undefined? !classes.has(k):force)classes.add(k);else classes.delete(k)}},
  value:'',checked:true,disabled:false,innerHTML:'',textContent:'',clientWidth:1200,clientHeight:446,width:1200,height:446,
  addEventListener(type,fn){(events[type]??=[]).push(fn)},fire(type,ev={}){return Promise.all((events[type]||[]).map(f=>f({target:this,currentTarget:this,...ev})))},
  setAttribute(name,value){this[name]=value},getBoundingClientRect(){return {width:1200,height:446,left:0,top:0}},
  querySelectorAll(){return []},querySelector(sel){if(sel==='.studioResetCursors'&&this.innerHTML.includes('studioResetCursors'))return {addEventListener(){}};if(sel==='#studioCloseEvent'&&this.innerHTML.includes('studioCloseEvent'))return {addEventListener(){}};return null},
  getContext(){return ctx},setPointerCapture(){},scrollIntoView(){},replaceChildren(){}
  }
}
const ctx={strokeStyle:'',fillStyle:'',beginPath(){},moveTo(){},lineTo(){},stroke(){},save(){},restore(){},rect(){},clip(){},fillText(){},fillRect(){},setLineDash(){},arc(){},fill(){},strokeRect(){},setTransform(){},clearRect(){},closePath(){}};
const els=new Map(),get=id=>els.get(id)||els.set(id,el(id)).get(id);
const now=Date.now(),iso=t=>new Date(now-3600000+t*60000).toISOString();
const pointSeries=Array.from({length:61},(_,m)=>({timestamp:iso(m),value:13,quality:'GOOD'}));
const alarm={alarmKey:'alarm-1',tagId:'temp',activeAt:iso(35),text:'Temperature Low',sourceName:'Temperature',priority:'High',state:'CLEARED',clearedAt:iso(36)};
const definitions=[{tagId:'temp',condition:'GreaterThanOrEqual',threshold:30,enabled:true,text:'High Alarm'}, {tagId:'temp',condition:'LessThanOrEqual',threshold:10,enabled:true,text:'Low Alarm'}];
const state={page:'trends',language:'en',theme:'dark',trends:[],tags:[{id:'temp',name:'Temperature',unit:'°C',dataType:'Float32',decimalPlaces:1},{id:'press',name:'Pressure',unit:'bar',dataType:'Float32',decimalPlaces:2}],devices:[],hiddenTrendTags:new Set(),historianConfigurations:[{configuration:{tagId:'temp',enabled:true,sampleIntervalSeconds:10}},{configuration:{tagId:'press',enabled:true,sampleIntervalSeconds:10}}],selectedTrendId:null,trendPayload:null};
const mocks={
  state,window:{addEventListener(){}},document:{getElementById:get,querySelectorAll:()=>[],createTextNode:text=>({textContent:text})},console,Date,Math,Set,Map,Number,
  MutationObserver:class {observe(){}},requestAnimationFrame:cb=>cb(),
  api:async url=>{if(url.includes('/api/trend-studio/series'))return {from:iso(0),to:iso(60),series:[{tagId:'temp',points:pointSeries,minimum:13,maximum:13,average:13,sampleCount:61},{tagId:'press',points:pointSeries.map(p=>({...p,value:5})),minimum:5,maximum:5,average:5,sampleCount:61}]};if(url.includes('/api/alarms/history'))return [alarm];if(url.includes('/api/alarms/definitions'))return definitions;if(url.includes('/api/batches'))return [];throw Error(url)},
  tagAddressText:()=>'',showToast:()=>{},renderTrendList:()=>{},populateTrendTagChecklist:()=>{},openAddTrendModal:()=>{},dateToLocalInput:d=>d.toISOString().slice(0,16),syncTrendRangePills:()=>{},currentTrendRange:()=>({from:new Date(iso(0)),to:new Date(iso(60))}),
  drawTrendChart:()=>{},refreshTrendPoints:()=>{},setTimeout:()=>0
};
vm.createContext(mocks);vm.runInContext(source,mocks);
const u=mocks.window.PrognodeStudio;
(async()=>{
  u.selected=['temp','press'];u.compare=true;await get('studioRefresh').fire('click');await new Promise(resolve=>setImmediate(resolve));
  assert.equal(u.flagHits.length,1,'flag appears after loading real context');
  assert.equal(u.events.definitions.length,2);
  const coords=u._test.buildAxes(state.trendPayload,1200,446),p=coords.plot;
  assert(coords.axis.left.min<=8&&coords.axis.left.max>=32,'combined axis includes alarm limits');
  await get('studioCursorA').fire('click');assert.equal(u.live,false,'A freezes live chart');
  await get('trendCanvas').fire('pointerdown',{clientX:p.x+p.w*.2,clientY:p.y+p.h*.4,pointerId:5});assert(u.cursors.A!==null,'cursor A placed');
  await get('studioCursorB').fire('click');await get('trendCanvas').fire('pointerdown',{clientX:p.x+p.w*.8,clientY:p.y+p.h*.4,pointerId:6});assert(u.cursors.B!==null,'cursor B placed');
  assert(get('studioCompareBar').innerHTML.includes('Δt'),'A/B comparison visible');
  assert(get('studioCompareBar').innerHTML.includes('Temperature'),'Tag A/B comparison shown');
  const flag=u.flagHits[0];await get('trendCanvas').fire('pointerdown',{clientX:flag.x,clientY:flag.y,pointerId:7});
  assert.equal(u.selectedEventKey,u._test.eventKey(alarm),'click flag selects occurrence');
  assert(!get('studioEventDetail').classList.contains('hidden'),'click opens details');
  assert.equal(u.flagHits.length,1,'flag retained after click');
  get('studioFlagToggle').checked=false;await get('studioFlagToggle').fire('change');
  assert.equal(u.flagHits.length,0,'independent flag visibility');
  assert(!get('studioEventDetail').classList.contains('hidden'),'event details persist after hiding flags');
  get('studioThresholdToggle').checked=false;await get('studioThresholdToggle').fire('change');
  const hiddenAxis=u._test.buildAxes(state.trendPayload,1200,446).axis.left;
  assert(hiddenAxis.min<=8&&hiddenAxis.max>=32,'alarm-aware scale persists if overlay lines hidden');
  console.log('PASS: mocked browser DOM — LIVE→A/B pause; Δt; alarm-aware axes; clickable persistent event flag; independent flags/thresholds');
})().catch(e=>{console.error('FAIL',e);process.exit(1)});
