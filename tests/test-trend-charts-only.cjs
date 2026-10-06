const assert=require('node:assert/strict');
const fs=require('node:fs');
const path=require('node:path');
const vm=require('node:vm');
const root=path.join(__dirname,'../src/Prognode.Host/wwwroot');
const source=fs.readFileSync(path.join(root,'trend-hf3plus.js'),'utf8');
const html=fs.readFileSync(path.join(root,'trend-hf3plus.html'),'utf8');
const shell=fs.readFileSync(path.join(root,'index.html'),'utf8');
const css=fs.readFileSync(path.join(root,'trend-hf3plus.css'),'utf8');
for(const id of ['charts-only-toggle','clear-all-charts'])assert(html.includes(`id="${id}"`),`missing ${id}`);
assert(shell.includes('allowfullscreen'),'Trend iframe must permit browser full screen');
for(const selector of ['html.charts-only .hf61-titleline','html.charts-only .hf61-explorer','html.charts-only .board-top','html.charts-only .hf61-events'])assert(css.includes(selector),`missing ${selector}`);

const begin=source.indexOf('function syncChartsOnlyButton()');
const end=source.indexOf("byId('add').onclick",begin);
assert(begin>=0&&end>begin);
const classes=new Set(),elements=new Map();
function element(id){if(!elements.has(id))elements.set(id,{value:'x',textContent:'',attrs:{},setAttribute(k,v){this.attrs[k]=v}});return elements.get(id)}
let saved=0,rendered=0,fullscreen=0,resized=0;
const S={lang:'en',charts:Array.from({length:15},(_,i)=>({id:`t${i}`})),selected:new Set(['t1']),compareIds:new Set(['t2']),compare:true,maximized:'t1',activeId:'t1',chartFilter:'line'};
const document={fullscreenElement:null,documentElement:{classList:{contains:x=>classes.has(x),toggle(x){classes.has(x)?classes.delete(x):classes.add(x);return classes.has(x)}},requestFullscreen:async()=>{fullscreen++;document.fullscreenElement=document.documentElement}},exitFullscreen:async()=>{document.fullscreenElement=null}};
const context={S,document,byId:element,drawChart:()=>{},resizeLayout:()=>resized++,toast:()=>{},setTimeout:f=>f(),requireTrendEdit:()=>true,confirm:()=>true,renderLayout:()=>rendered++,storeLayout:async()=>saved++};
vm.createContext(context);vm.runInContext(source.slice(begin,end),context);
(async()=>{
 await context.toggleChartsOnly();assert(classes.has('charts-only'));assert.equal(fullscreen,1);assert.equal(resized,1);assert.equal(element('charts-only-toggle').attrs['aria-pressed'],'true');
 await context.clearAllCharts();assert.equal(S.charts.length,0);assert.equal(S.selected.size,0);assert.equal(S.compareIds.size,0);assert.equal(S.chartFilter,'');assert.equal(saved,1,'empty layout persisted');assert.equal(rendered,1);
 console.log('PASS: charts-only full screen and confirmed remove-all persists an empty layout');
})().catch(error=>{console.error(error);process.exitCode=1});
