// Address checks in the Tag form: S7 I/Q/M, Modbus six-digit and 64-bit, MQTT topic#field.
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const app=fs.readFileSync(path.join(__dirname,'../src/Prognode.Host/wwwroot/app.js'),'utf8');
const html=fs.readFileSync(path.join(__dirname,'../src/Prognode.Host/wwwroot/index.html'),'utf8');
function section(start,end){const a=app.indexOf(start),b=app.indexOf(end,a+start.length);assert(a>=0&&b>a,start);return app.slice(a,b)}
const context={TextEncoder};
vm.createContext(context);
vm.runInContext(section('function s7AddressValid(','async function testS7Connection('),context);
vm.runInContext(section('function modbusAddressInfo(','function nextModbusTagAddress('),context);
vm.runInContext(section('function mqttTagAddressValid(','function validateTagAddressClient('),context);
const {s7AddressValid:s7,modbusAddressInfo:modbus,tagRegisterWidth:width,mqttTagAddressValid:mqtt}=context;

for(const [a,t] of [['DB1.DBX0.0','Bool'],['I0.0','Bool'],['%I0.7','Bool'],['E1.3','Bool'],['Q0.1','Bool'],['A4.0','Bool'],['M10.0','Bool'],['MX10.1','Bool'],
  ['DB1.DBW2','Word'],['IW64','Int16'],['%QW2','UInt16'],['MW20','Word'],['EW64','Int16'],
  ['DB1.DBD4','Float32'],['ID100','Int32'],['QD4','UInt32'],['%MD30','Float32'],['AD8','Float32']])
  assert.equal(s7(a,t),true,`${a} ${t}`);
for(const [a,t] of [['I0.8','Bool'],['IW64','Bool'],['I0.0','Word'],['MD30','Int16'],['MW20','Float32'],['DB0.DBX0.0','Bool'],
  ['P0.0','Bool'],['DB1.DBD4','Int64'],['MD65534','Float32'],['T1','Word']])
  assert.equal(s7(a,t),false,`${a} ${t}`);

assert.equal(modbus('40001').area,'Holding Register');
assert.equal(modbus('419000').area,'Holding Register');
assert.equal(modbus('419000').last,465536);
assert.equal(modbus('300001').area,'Input Register');
assert.equal(modbus('000017').area,'Coil');
assert.equal(modbus('165536').area,'Discrete Input');
assert.equal(modbus('465537'),null);
assert.equal(modbus('50001'),null);
assert.equal(width('Float64'),4);assert.equal(width('Int64'),4);assert.equal(width('Float32'),2);assert.equal(width('Word'),1);

for(const a of ['plant/temp','plant/env#temp','gw/01#sensors[0].value','gw#$.a.b','gw#[2]','a/b#x[0][1]'])assert.equal(mqtt(a),true,a);
for(const a of ['','plant/+/temp','plant#','plant#a..b','plant#a.','#temp'])assert.equal(mqtt(a),false,a);

for(const id of ['mqttUsername','mqttPassword','opcUaUsername','opcUaPassword'])assert(html.includes(`id="${id}"`),id);
assert(/id="mqttPassword" type="password"/.test(html)&&/id="opcUaPassword" type="password"/.test(html),'password inputs are masked');
for(const type of ['Int64','UInt64','Float64'])assert(html.includes(`<option value="${type}">`),type);
console.log('PASS: S7 I/Q/M, Modbus six-digit and 64-bit, MQTT topic#field and device credential fields');
