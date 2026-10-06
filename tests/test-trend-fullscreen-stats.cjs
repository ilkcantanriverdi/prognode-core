const fs = require('node:fs');
const vm = require('node:vm');
const assert = require('node:assert/strict');
const path = require('node:path');

const source = fs.readFileSync(path.join(__dirname,
  '../src/Prognode.Host/wwwroot/trend-studio-fullscreen.js'), 'utf8');
const start = source.indexOf('function chartMarkup(s){');
const end = source.indexOf('function render(){', start);
assert(start >= 0 && end > start, 'fullscreen chart markup function exists');

const tag = {id:'temperature', name:'Temperature', unit:'C', digits:1,
  color:'#2acfe1', sampleSeconds:10};
const store = {series:new Map(), maxed:null, compareMode:false, period:'15m', live:true};
const context = {store, Date, Math, Number,
  L:(tr)=>tr, esc:value=>String(value),
  lastGood:data=>[...(data?.points||[])].reverse().find(p=>p.quality==='Good')||null,
  severity:()=> 'NORMAL', alertLimits:()=>[], trSpans:{'15m':'15 dakika'},
  enSpans:{'15m':'15 minutes'}};
const chartMarkup = vm.runInNewContext(source.slice(start, end) + '\nchartMarkup', context);

store.series.set(tag.id, {points:[{timestamp:new Date().toISOString(), value:null,
  quality:'Bad'}], sampleCount:1, minimum:null, maximum:null, average:null});
const badOnly = chartMarkup(tag);
assert(badOnly.includes('NO DATA'), 'bad-only series has no valid value');
assert(!badOnly.includes('Seçili aralığın ham kayıt istatistikleri'),
  'null statistics must not appear as zero');

store.series.set(tag.id, {points:[{timestamp:new Date().toISOString(), value:0,
  quality:'Good'}], sampleCount:1, minimum:0, maximum:0, average:0});
const realZero = chartMarkup(tag);
assert(realZero.includes('Seçili aralığın ham kayıt istatistikleri'),
  'a genuine zero-value statistic must remain visible');
assert(realZero.includes('Ort <b>0.0</b>'), 'a genuine zero average must remain visible');
console.log('PASS: fullscreen trend distinguishes null statistics from genuine zero');
