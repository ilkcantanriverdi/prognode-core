// Devices page shows the live state (Online / Degraded / Offline), not the stored "Configured".
const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const app=fs.readFileSync(path.join(__dirname,'../src/Prognode.Host/wwwroot/app.js'),'utf8');
const begin=app.indexOf('function deviceRuntimeStatus(');
const end=app.indexOf('function updateDeviceStatusBadges(',begin);
assert(begin>=0&&end>begin,'deviceRuntimeStatus missing');
const context={state:{}};
vm.createContext(context);vm.runInContext(app.slice(begin,end),context);
const values=entries=>new Map(entries);
context.state={
  activeAlarms:[{deviceId:'d3',text:'Packaging PLC • Communication lost'}],
  tags:[{id:'t1',deviceId:'d1'},{id:'t2',deviceId:'d1'},{id:'t3',deviceId:'d2'},{id:'t4',deviceId:'d3'},{id:'t5',deviceId:'d5'}],
  tagValues:values([['t1',{quality:'Good'}],['t2',{quality:'Good'}],['t3',{quality:'Bad'}],['t4',{quality:'Good'}],['t5',{quality:'Waiting'}]]),
};
const status=id=>context.deviceRuntimeStatus({id});
assert.equal(status('d1'),'Online');
assert.equal(status('d2'),'Degraded');
assert.equal(status('d3'),'Offline','communication-lost alarm wins');
assert.equal(status('d4'),'No tags');
assert.equal(status('d5'),'Waiting');
assert(app.includes('data-device-status="${escapeHtml(String(device.id))}">${escapeHtml(deviceRuntimeStatus(device))}'),'table uses the live status');
assert(!app.includes('${escapeHtml(device.status)}</span></td>'),'stored status no longer shown');
assert(/updateOverview\(\);\s*updateDeviceStatusBadges\(\);/.test(app),'badges refresh with live values');
console.log('PASS: Devices page shows live Online / Degraded / Offline status');
