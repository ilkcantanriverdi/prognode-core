const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const source = fs.readFileSync(path.join(__dirname, '../src/Prognode.Host/wwwroot/app.js'), 'utf8');
function section(first,last) {
  const start=source.indexOf(first),end=source.indexOf(last,start+first.length);
  assert(start>=0 && end>start);
  return source.slice(start,end);
}
const elements=new Map();
function element(id) {
  if (!elements.has(id)) elements.set(id,{
    value:'',placeholder:'',textContent:'',
    classList:{toggle:()=>{}},querySelector:()=>({textContent:''})
  });
  return elements.get(id);
}
const context={
  state:{language:'tr',editingTagId:null,tagSuggestedAddress:null,tags:[],devices:[
    {id:'modbus',protocol:'Modbus TCP'}, {id:'s7',protocol:'Siemens S7 TCP'},
    {id:'mqtt',protocol:'MQTT'}, {id:'opc',protocol:'OPC UA'}
  ]},
  $:element,t:()=> 'Modbus help',validateTagAddressClient:()=>true
};
vm.createContext(context);
vm.runInContext(section('function tagIsS7()', 'function s7AddressValid('),context);
vm.runInContext(section('function updateTagFormVisibility()', 'function modbusAddressInfo('),context);
vm.runInContext(section('function modbusAddressInfo(', 'function tagRegisterWidth('),context);
vm.runInContext(section('function tagRegisterWidth(', 'function describeTagAddress('),context);
const device=element('tagDevice'),type=element('tagDataType'),address=element('tagAddress');

device.value='modbus';type.value='UInt16';
assert.equal(context.tagAddressExample(),'40001');
device.value='s7';type.value='UInt16';
assert.equal(context.tagAddressExample(),'DB1.DBW2');
address.value='40001';context.state.tagSuggestedAddress='40001';
context.refreshTagAddressExample();
assert.equal(address.value,'DB1.DBW2');
assert(element('tagAddressHelp').textContent.includes('DB1.DBW2'));
type.value='Bool';context.refreshTagAddressExample();
assert.equal(address.value,'DB1.DBX0.0');
type.value='Float32';context.refreshTagAddressExample();
assert.equal(address.value,'DB1.DBD4');
device.value='mqtt';context.refreshTagAddressExample();
assert.equal(address.value,'plant/temperature');
assert(element('tagAddressHelp').textContent.includes('MQTT'));
device.value='opc';context.refreshTagAddressExample();
assert.equal(address.value,'ns=2;s=Temperature');
assert(element('tagAddressHelp').textContent.includes('NodeId'));
address.value='ns=2;s=Custom';context.state.tagSuggestedAddress=null;
device.value='s7';context.refreshTagAddressExample();
assert.equal(address.value,'ns=2;s=Custom','manual address is not silently overwritten');
console.log('PASS: Tag examples/help follow all four protocols and S7 datatype');
