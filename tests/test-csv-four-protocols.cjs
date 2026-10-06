const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const app = fs.readFileSync(path.join(__dirname, '../src/Prognode.Host/wwwroot/app.js'), 'utf8');
function slice(start, end) {
  const first = app.indexOf(start);
  const last = app.indexOf(end, first + start.length);
  assert(first >= 0 && last > first, `Missing app.js section ${start}`);
  return app.slice(first, last);
}

const devices = [
  {id:'modbus',name:'Boiler PLC',protocol:'Modbus TCP'},
  {id:'s7',name:'Packing PLC',protocol:'Siemens S7 TCP'},
  {id:'mqtt',name:'Plant Broker',protocol:'MQTT'},
  {id:'opc',name:'OPC Server',protocol:'OPC UA'}
];
const calls = [];
const context = {
  state:{devices,tags:[],alarmDefinitions:[],historianConfigurations:[],language:'en',importPreview:[]},
  TextEncoder, URL,
  $:()=>({disabled:false}),
  api:async (route,options)=>calls.push({route,body:JSON.parse(options.body)}),
  loadDevices:async()=>{},loadTags:async()=>{},loadAlarms:async()=>{},loadHistorianStats:async()=>{},
  renderImportPreview:()=>{},showToast:()=>{},requireConfigurationAccess:()=>true,
  canMutateConfiguration:()=>true,confirm:()=>true
};
vm.createContext(context);
vm.runInContext(slice('function s7AddressValid(', 'async function testS7Connection('), context);
vm.runInContext(slice('function modbusAddressInfo(', 'function describeTagAddress('), context);
vm.runInContext(slice('function opcUaNodeAddressValid(', 'function validateTagAddressClient('), context);
vm.runInContext(slice('function dev3NormalizeHeader(', 'function renderImportPreview('), context);
vm.runInContext(slice('async function executeCsvImport(', 'const dev3Templates ='), context);

function preview(csv, dataset) {
  return context.dev3ParseCsv(csv).map(row=>context.dev3PreviewRow(row,dataset));
}
function valid(item) {
  assert.equal(item.valid, true, item.errors.join('; '));
  return item;
}
function invalid(item, message) {
  assert.equal(item.valid, false, `${item.preview} unexpectedly valid`);
  assert(item.errors.some(error=>error.includes(message)), item.errors.join('; '));
}

const deviceCsv = [
  'Name;Protocol;Host;Port;UnitId;PollIntervalMs;Status;CreatedAtUtc',
  'Modbus New;Modbus TCP;192.168.1.10;502;1;1000;Configured;2026-09-30T00:00:00Z',
  'S7 New;Siemens S7 TCP;192.168.1.20;102;33;1000;Configured;2026-09-30T00:00:00Z',
  'MQTT New;MQTT;broker.example.com;8883;;1000;Configured;2026-09-30T00:00:00Z',
  'OPC New;OPC UA;opc.tcp://192.168.1.30:4840;4840;;1000;Configured;2026-09-30T00:00:00Z'
].join('\r\n');
const deviceRows = preview(deviceCsv,'devices').map(valid);
assert.equal(deviceRows[1].payload.body.rack,1,'S7 exported UnitId decodes rack');
assert.equal(deviceRows[1].payload.body.slot,1,'S7 exported UnitId decodes slot');
assert.equal(deviceRows.map(x=>x.payload.kind).join(','),'modbus,s7,mqtt,opcua');

const tagCsv = [
  'Device;Name;Address;DataType;BitIndex;ByteOrder;Unit;Offset;DecimalPlaces;Enabled',
  'Boiler PLC;Temperature;40001;Float32;;ABCD;C;0;1;true',
  'Packing PLC;S7 Word;DB1.DBW2;Word;;ABCD;;0;0;true',
  'Packing PLC;S7 Float;DB1.DBD4;Float32;;ABCD;rpm;0;1;true',
  'Plant Broker;MQTT Temperature;plant/temperature;Float32;;ABCD;C;0;1;true',
  'OPC Server;OPC Pressure;"ns=2;s=Pressure";Float32;;ABCD;bar;0;1;true'
].join('\r\n');
const tagRows = preview(tagCsv,'tags').map(valid);
assert.equal(tagRows[1].payload.body.address,'DB1.DBW2');
assert.equal(tagRows[2].payload.body.address,'DB1.DBD4');
assert.equal(tagRows[3].payload.body.address,'plant/temperature');
assert.equal(tagRows[4].payload.body.address,'ns=2;s=Pressure');

invalid(preview('Device;Name;Address;DataType\nPacking PLC;Bad;40001;Word','tags')[0],'S7 address');
invalid(preview('Device;Name;Address;DataType\nPacking PLC;Bad;DB1.DBD4;Word','tags')[0],'S7 address');
invalid(preview('Device;Name;Address;DataType\nPlant Broker;Bad;plant/+;Float32','tags')[0],'MQTT address');
invalid(preview('Device;Name;Address;DataType\nOPC Server;Bad;40001;Float32','tags')[0],'OPC UA address');

(async()=>{
  context.state.importPreview = deviceRows;
  await vm.runInContext('executeCsvImport()',context);
  assert.deepEqual(calls.map(x=>x.route),[
    '/api/devices/modbus-tcp','/api/devices/siemens-s7-tcp','/api/devices/mqtt','/api/devices/opc-ua'
  ]);
  assert(calls.every(x=>x.body.name),'each protocol posts its device payload');
  console.log('PASS: four-protocol device CSV roundtrip, tag address validation, and create routes');
})().catch(error=>{console.error(error);process.exitCode=1;});
