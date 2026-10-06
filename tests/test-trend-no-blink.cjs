const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const source=fs.readFileSync(path.join(__dirname,'../src/Prognode.Host/wwwroot/trend-hf3plus.js'),'utf8');
const begin=source.indexOf('async function fetchSeries(');
const end=source.indexOf('function coreVisibleChanged(',begin);
assert(begin>=0&&end>begin);
assert(source.includes('if(c.loading)continue;'),'live range must stay fixed during a query');

const now=Date.now(),range=[now-60000,now];
const chart={id:'tag-1',range:[...range],windowMin:15,requestId:0,lastFetch:0,loading:false,needsFetch:true};
const allData=new Map(),metaById=new Map(),signalDefs=[{id:'tag-1',interval:10000,base:0}];
const replies=[];
let draws=0;
const context={coreVisible:true,document:{hidden:false},Core:{ready:true},signalDefs,allData,metaById,
 api:()=>{assert(replies.length,'unexpected historian request');return replies.shift()()},
 drawChart:()=>draws++,console,Date,URLSearchParams,Number,String,Error};
vm.createContext(context);vm.runInContext(source.slice(begin,end),context);
const response=points=>({series:[{tagId:'tag-1',points,sampleCount:points.length}]});
const sample=(t,value)=>({timestamp:new Date(t).toISOString(),value,quality:'GOOD'});

(async()=>{
 replies.push(()=>Promise.resolve(response([sample(now-20000,1),sample(now-10000,2)])));
 await context.fetchSeries(chart,true);
 assert.equal(allData.get(chart.id).length,2);

 replies.push(()=>Promise.resolve(response([])));
 await context.fetchSeries(chart,true);
 assert.equal(allData.get(chart.id).length,2,'a transient empty reply must not erase visible recorded points');
 assert(chart.lastError);

 let resolveOld,resolveNew;
 replies.push(()=>new Promise(resolve=>{resolveOld=resolve}));
 const oldRequest=context.fetchSeries(chart,true);
 chart.range=[now,now+60000];
 context.refreshPanel(chart);
 replies.push(()=>new Promise(resolve=>{resolveNew=resolve}));
 resolveOld(response([sample(now-5000,3)]));
 await oldRequest;
 await Promise.resolve();
 assert.equal(allData.get(chart.id).length,2,'stale response must not replace current plot');
 assert.equal(chart.loading,true,'updated range must be refetched');
 resolveNew(response([sample(now+10000,4)]));
 await new Promise(resolve=>setImmediate(resolve));
 assert.equal(allData.get(chart.id).length,1);
 assert.equal(allData.get(chart.id)[0].v,4);
 assert.equal(chart.lastError,null);
 assert(draws>=3);
 console.log('PASS: trend retains points across transient empty and stale historian replies');
})().catch(error=>{console.error(error);process.exitCode=1});
