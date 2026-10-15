// The Tag form must list devices of every protocol the Monitor reads, not only the first four.
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const app=fs.readFileSync(path.join(__dirname,'../src/Prognode.Host/wwwroot/app.js'),'utf8');
function section(start,end){const a=app.indexOf(start),b=app.indexOf(end,a+start.length);assert(a>=0&&b>a,start);return app.slice(a,b)}
const context={};
vm.createContext(context);
vm.runInContext(section('const TAG_PROTOCOLS','function openAddTagModal('),context);
vm.runInContext(section('function dev3Protocol(','function dev3ImportExistingMode('),context);
vm.runInContext('this.protocols=TAG_PROTOCOLS;this.endpoint=tagDeviceEndpoint;',context);

// Every protocol a device CSV can create is selectable in the Tag form.
const csvProtocols=new Set(['modbus','rtu','mc','fins','enip','s7','mqtt','opcua'].map(k=>vm.runInContext(`dev3Protocol(${JSON.stringify(k)})`,context)));
for(const protocol of csvProtocols) assert(context.protocols.includes(protocol),`Tag form misses ${protocol}`);
assert.equal(context.protocols.length,csvProtocols.size);

// No more hard-coded four-protocol lists.
assert(!/\["Modbus TCP", "Siemens S7 TCP", "MQTT", "OPC UA"\]/.test(app),'a four-protocol device filter is left');

assert.equal(context.endpoint({host:'192.168.250.1',port:9600}),'192.168.250.1:9600');
assert.equal(context.endpoint({host:'COM3;9600;8E1',port:null}),'COM3 · 9600 · 8E1');
assert.equal(context.endpoint({host:'opc.tcp://10.0.0.5:4840/ua',port:4840}),'opc.tcp://10.0.0.5:4840/ua');
console.log('PASS: Tag form lists Modbus TCP/RTU, Mitsubishi MC, Omron FINS, EtherNet/IP, S7, MQTT and OPC UA devices');
