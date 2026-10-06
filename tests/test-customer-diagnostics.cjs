const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const root=path.join(__dirname,'../src/Prognode.Host/wwwroot');
const html=fs.readFileSync(path.join(root,'index.html'),'utf8');
const source=fs.readFileSync(path.join(root,'app.js'),'utf8');
for(const id of ['diagnosticsCoreStatus','diagnosticsAgentStatus','diagnosticsTagQuality','diagnosticsHistorianSamples','diagnosticsDeviceRows','diagnosticsRefresh'])
  assert(html.includes(`id="${id}"`),`missing ${id}`);
assert(!html.includes('class="placeholder-page"'),'customer diagnostics cannot be a roadmap placeholder');
const begin=source.indexOf('function renderDiagnostics()');
const end=source.indexOf('function renderAgentStatus()',begin);
assert(begin>=0&&end>begin);
const elements=new Map();
const lookup=id=>{if(!elements.has(id))elements.set(id,{textContent:'',innerHTML:'',classList:{toggle(){}}});return elements.get(id)};
const state={page:'diagnostics',language:'tr',health:{status:'Running',coreVersion:'1.0'},agentStatus:{isOnline:true,machineName:'PC'},
 devices:[{id:'d1',name:'PLC',protocol:'OPC UA',status:'Configured'}],tags:[{id:'t1',deviceId:'d1'},{id:'t2',deviceId:'d1'}],
 tagValues:new Map([['t1',{quality:'Good',timestamp:'2026-10-02T12:00:00Z'}],['t2',{quality:'Bad',timestamp:'2026-10-02T12:00:01Z',error:'PLC read timeout'}]]),
 historianStats:{totalSamples:123,databaseBytes:4096}};
const ctx={state,$:lookup,Date,Number,String,formatBytes:()=> '4 KB',escapeHtml:s=>String(s)};
vm.createContext(ctx);vm.runInContext(source.slice(begin,end),ctx);ctx.renderDiagnostics();
assert.equal(lookup('diagnosticsCoreStatus').textContent,'Çalışıyor');
assert.equal(lookup('diagnosticsTagQuality').textContent,'1 / 2');
assert(lookup('diagnosticsDeviceRows').innerHTML.includes('1 / 1 / 0'));
assert(lookup('diagnosticsDeviceRows').innerHTML.includes('PLC read timeout'));
console.log('PASS: customer diagnostics shows real Core, Agent, Tag and Historian state');
