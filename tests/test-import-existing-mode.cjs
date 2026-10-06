const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const app=fs.readFileSync(path.join(__dirname,'../src/Prognode.Host/wwwroot/app.js'),'utf8');
const html=fs.readFileSync(path.join(__dirname,'../src/Prognode.Host/wwwroot/index.html'),'utf8');
const slice=(a,b)=>{const start=app.indexOf(a),end=app.indexOf(b,start+a.length);assert(start>=0&&end>start);return app.slice(start,end)};
const elements=new Map(),$=id=>elements.get(id)||elements.set(id,{value:'',disabled:false}).get(id);
const state={language:'en',devices:[
  {id:'d1',name:'Boiler PLC',protocol:'Modbus TCP'},
  {id:'d2',name:'Packing PLC',protocol:'Siemens S7 TCP'},
  {id:'d3',name:'Plant Broker',protocol:'MQTT'},
  {id:'d4',name:'OPC Server',protocol:'OPC UA'}],
  tags:[{id:'t1',deviceId:'d1',name:'Temperature',dataType:'Float32'}],
  alarmDefinitions:[{id:'a1',tagId:'t1',text:'High Temp'}],
  historianConfigurations:[{configuration:{id:'h1',tagId:'t1'}}],importPreview:[]};
const calls=[];
const context={state,$,TextEncoder,URL,Date,Number,String,
  api:async(route,options)=>{calls.push({route,method:options.method,body:JSON.parse(options.body)})},
  loadDevices:async()=>{},loadTags:async()=>{},loadAlarms:async()=>{},loadHistorianStats:async()=>{},
  renderImportPreview:()=>{},showToast:()=>{},requireConfigurationAccess:()=>true,canMutateConfiguration:()=>true,confirm:()=>true};
vm.createContext(context);
vm.runInContext(slice('function s7AddressValid(','async function testS7Connection('),context);
vm.runInContext(slice('function modbusAddressInfo(','function describeTagAddress('),context);
vm.runInContext(slice('function dev3NormalizeHeader(','function renderImportPreview('),context);
vm.runInContext(slice('function opcUaNodeAddressValid(', 'function validateTagAddressClient('),context);
vm.runInContext(slice('async function executeCsvImport(','const dev3Templates ='),context);
const row=(dataset,text)=>context.dev3PreviewRow(context.dev3ParseCsv(text)[0],dataset);
const deviceCsv=(name,protocol,host,port,unit='')=>`Name;Protocol;Host;Port;UnitId;PollIntervalMs\n${name};${protocol};${host};${port};${unit};1000`;
const modbus=deviceCsv('Boiler PLC','Modbus TCP','192.168.1.2',502,1);
assert(html.includes('id="importExistingMode"'));
$('importExistingMode').value='skip';
const skipped=row('devices',modbus);
assert.equal(skipped.valid,false,'skip mode must not mutate existing record');
assert.equal(skipped.action,'skip');
assert.equal(skipped.errors.length,0,'skip must not be reported as an import error');
const newWithSkip=row('devices',deviceCsv('New PLC','Modbus TCP','192.168.1.5',502,1));
assert.equal(newWithSkip.action,'create');
assert.equal(newWithSkip.valid,true);
$('importExistingMode').value='update';
const devices=[
  row('devices',modbus),
  row('devices',deviceCsv('Packing PLC','Siemens S7 TCP','192.168.1.3',102,33)),
  row('devices',deviceCsv('Plant Broker','MQTT','broker.example.com',8883)),
  row('devices',deviceCsv('OPC Server','OPC UA','opc.tcp://192.168.1.4:4840',4840))
];
const tag=row('tags','Device;Name;Address;DataType\nBoiler PLC;Temperature;40001;Float32');
const alarm=row('alarms','Device;Tag;AlarmText;Priority;Condition;Threshold\nBoiler PLC;Temperature;High Temp;High;GreaterThanOrEqual;80');
const historian=row('historian','Device;Tag;SampleIntervalSeconds;RetentionDays\nBoiler PLC;Temperature;10;365');
for(const item of [...devices,tag,alarm,historian]){assert(item.valid,item.errors.join('; '));assert.equal(item.action,'update');assert(item.existingId)}
const mismatch=row('devices',deviceCsv('Boiler PLC','MQTT','broker.example.com',8883));
assert(!mismatch.valid&&mismatch.errors.some(x=>x.includes('protocol')));
(async()=>{
  state.importPreview=[...devices,tag,alarm,historian];
  await context.executeCsvImport();
  assert.equal(calls.length,7);
  assert(calls.every(x=>x.method==='PUT'),'existing records must update without delete/recreate');
  assert.equal(calls[1].body.unitId,33,'S7 rack/slot must survive device update');
  assert.equal(calls[3].body.host,'opc.tcp://192.168.1.4:4840');
  assert.equal(calls[3].route,'/api/devices/d4');
  assert.equal(calls[4].route,'/api/tags/t1');
  assert.equal(calls[5].route,'/api/alarms/definitions/a1');
  assert.equal(calls[6].route,'/api/historian/configurations/h1');
  assert(app.includes("item.errors.push('Duplicate row in this CSV')"));
  console.log('PASS: CSV skip/update preserves IDs across four devices, Tags, Alarms and Historian');
})().catch(error=>{console.error(error);process.exitCode=1});
