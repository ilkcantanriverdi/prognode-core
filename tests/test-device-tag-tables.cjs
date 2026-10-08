const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const root=path.join(__dirname,'../src/Prognode.Host/wwwroot');
const source=fs.readFileSync(path.join(root,'app.js'),'utf8');
const html=fs.readFileSync(path.join(root,'index.html'),'utf8');
for(const id of ['deviceProtocolFilter','deviceStatusFilter','deviceSort','devicePageSize','devicePager',
  'tagDeviceFilter','tagTypeFilter','tagSort','tagPageSize','tagPager'])
  assert(html.includes(`id="${id}"`),`missing ${id}`);
function section(start,end){const a=source.indexOf(start),b=source.indexOf(end,a+start.length);assert(a>=0&&b>a);return source.slice(a,b)}
const elements=new Map();
function $(id){if(!elements.has(id))elements.set(id,{
  value:'',innerHTML:'',textContent:'',disabled:false,options:[{textContent:''}],
  classList:{toggle:()=>{},remove:()=>{}},addEventListener:()=>{},querySelectorAll:()=>[],closest:()=>null
});return elements.get(id)}
const devices=Array.from({length:30},(_,i)=>({
  id:`d${i+1}`,name:`Device ${String(i+1).padStart(2,'0')}`,
  protocol:i<15?'Modbus TCP':'Siemens S7 TCP',status:i%2?'Online':'Configured',
  host:'127.0.0.1',port:502,pollIntervalMs:1000,createdAt:new Date(2026,0,i+1).toISOString()
}));
const tags=devices.map((device,i)=>({id:`t${i+1}`,deviceId:device.id,
  name:`Tag ${String(i+1).padStart(2,'0')}`,address:String(40001+i),dataType:i%2?'Word':'Float32',unit:''}));
const context={state:{language:'en',devices,tags,tagValues:new Map(),devicePage:1,tagPage:1,licenseUsage:{}},
  $,document:{querySelectorAll:()=>[],getElementById:()=>({id:'',className:'',innerHTML:'',classList:{toggle(){}},querySelector:()=>({}),querySelectorAll:()=>[]}),createElement:()=>({id:'',className:'',innerHTML:'',classList:{toggle(){}},querySelector:()=>({}),querySelectorAll:()=>[]})},escapeHtml:x=>String(x),statusClass:()=>'',t:x=>x,
  tagDeviceName:id=>devices.find(d=>d.id===id)?.name||'—',
  tagAddressText:t=>t.address,tagDatatypeText:t=>t.dataType,tagValueText:()=> '—',
  applyAccessMode:()=>{context.accessReapplied=(context.accessReapplied||0)+1}};
vm.createContext(context);
vm.runInContext(section('function syncTableFilter(', 'function tagDeviceName('),context);
vm.runInContext(section('function renderTags(', 'function populateTagDeviceSelect('),context);
$('devicePageSize').value='10';$('deviceSort').value='name-asc';
context.renderDevices();
assert.equal($('devicePageInfo').textContent,'1 / 3');
assert.equal(($('deviceTableBody').innerHTML.match(/<tr>/g)||[]).length,10);
assert($('deviceTableBody').innerHTML.includes('Device 01'));
context.state.devicePage=3;context.renderDevices();
assert($('deviceTableBody').innerHTML.includes('Device 30'));
assert.equal($('deviceNextPage').disabled,true);
$('deviceProtocolFilter').value='Siemens S7 TCP';context.state.devicePage=1;context.renderDevices();
assert.equal($('deviceTableMeta').textContent,'15 / 30 devices');
assert(!$('deviceTableBody').innerHTML.includes('Device 01'));
$('deviceSort').value='name-desc';context.renderDevices();
assert($('deviceTableBody').innerHTML.indexOf('Device 30')<$('deviceTableBody').innerHTML.indexOf('Device 29'));
$('deviceStatusFilter').value='Online';context.renderDevices();
assert.equal($('deviceTableMeta').textContent,'8 / 30 devices');
$('tagPageSize').value='10';$('tagSort').value='name-asc';
context.renderTags();
assert.equal($('tagPageInfo').textContent,'1 / 3');
assert.equal(($('tagTableBody').innerHTML.match(/<tr title=/g)||[]).length,10);
$('tagTypeFilter').value='Word';context.renderTags();
assert.equal($('tagTableMeta').textContent,'15 / 30 tags');
assert.equal($('tagPageInfo').textContent,'1 / 2');
$('tagSort').value='name-desc';context.renderTags();
assert($('tagTableBody').innerHTML.indexOf('Tag 30')<$('tagTableBody').innerHTML.indexOf('Tag 28'));
context.state.tagPage=8;context.renderTags();
assert.equal(context.state.tagPage,2,'page clamps after filter narrows results');
assert(context.accessReapplied>=4,'session gate reapplied to rebuilt Tag action buttons');
console.log('PASS: Device/Tag filters, sorting controls and page-size pagination');
