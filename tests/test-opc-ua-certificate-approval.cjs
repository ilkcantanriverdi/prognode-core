const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');

const app = fs.readFileSync(path.join(__dirname,'../src/Prognode.Host/wwwroot/app.js'),'utf8');
const html = fs.readFileSync(path.join(__dirname,'../src/Prognode.Host/wwwroot/index.html'),'utf8');
const endpoint = fs.readFileSync(path.join(__dirname,'../src/Prognode.Web/EndpointExtensions.cs'),'utf8');
const factory = fs.readFileSync(path.join(__dirname,'../src/Prognode.Protocols.OpcUa/OpcUaConnectionFactory.cs'),'utf8');
assert(html.includes('id="inspectOpcUaCertificate"') && html.includes('id="opcUaCertificateResult"'));
assert(endpoint.includes('/api/devices/opc-ua/certificate/inspect') &&
  endpoint.includes('/api/devices/opc-ua/certificate/trust') &&
  endpoint.includes('CheckQrAdmin(context, sessions)'));
assert(factory.includes('expectedSha256') && factory.includes('certificate changed'));
assert(app.includes('!state.opcUaCertificate?.trusted') && app.includes('opcUaCertificateInput !== endpointUrl'));

const elements = new Map();
function $(id) {
  if(!elements.has(id))elements.set(id,{value:'',checked:false,disabled:false,innerHTML:'',
    textContent:'',classList:{remove(){},add(){}},addEventListener(){}});
  return elements.get(id);
}
const state={language:'en',opcUaCertificate:null,opcUaCertificateInput:''};
const calls=[];
const info={endpointUrl:'opc.tcp://plc:4840/',subject:'CN=PLC',sha256:'ABC123',sha1:'DEF456',
  validFrom:'2026-01-01T00:00:00Z',validUntil:'2027-01-01T00:00:00Z',trusted:false};
const context={state,$,Date,escapeHtml:x=>String(x),showToast:()=>{},
  requireConfigurationAccess:()=>true,api:async(route)=>{
    calls.push(route);
    return route.endsWith('/trust')?{...info,trusted:true}:info;
  }};
vm.createContext(context);
const start=app.indexOf('async function inspectOpcUaCertificate()');
const end=app.indexOf('async function pingModbusHost()',start);
assert(start>=0&&end>start);
vm.runInContext(app.slice(start,end),context);

(async()=>{
  $('opcUaUrl').value=info.endpointUrl;
  await context.inspectOpcUaCertificate();
  assert.equal(state.opcUaCertificate?.trusted,false);
  await context.approveOpcUaCertificate();
  assert.equal(calls.filter(x=>x.endsWith('/trust')).length,0,'unchecked fingerprint was trusted');
  $('opcUaFingerprintVerified').checked=true;
  $('opcUaUrl').value='opc.tcp://other:4840/';
  await context.approveOpcUaCertificate();
  assert.equal(calls.filter(x=>x.endsWith('/trust')).length,0,'changed endpoint was trusted');
  $('opcUaUrl').value=info.endpointUrl;
  await context.approveOpcUaCertificate();
  assert.equal(calls.filter(x=>x.endsWith('/trust')).length,1);
  assert.equal(state.opcUaCertificate.trusted,true);
  console.log('PASS: OPC UA certificate approval requires explicit verified fingerprint and unchanged endpoint');
})().catch(error=>{console.error(error);process.exitCode=1});
