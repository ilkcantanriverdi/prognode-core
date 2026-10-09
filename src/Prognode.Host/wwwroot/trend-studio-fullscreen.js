/* PROGNODE RC6.4.5 — real Core/Historian fullscreen Trend Studio.
 * Zero demo samples, zero PLC writes, no third-party chart CDN.
 * The smooth spline alters pixels only. Exports use the original returned samples.
 */
(()=>{'use strict';
const $=id=>document.getElementById(id);
const esc=x=>String(x??'').replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
const palette=['#2acfe1','#b993f9','#e8b36a','#62db99','#e991bc','#77b6ff','#e7d06c','#92e0d4'];
const durations={'5m':5,'15m':15,'1h':60,'8h':480,'24h':1440,'7d':10080};

const store={catalog:[], selected:[], series:new Map(), definitions:[], alarms:[], batches:[], live:true, visible:false,
 period:'15m', custom:false, range:null, flags:true, levels:true, points:true, compareMode:false, comparison:false,
 comparing:new Set(), maxed:null, cursorMode:null, cursors:{A:null,B:null}, requestSeq:0, pending:false,
 time:Date.now(), lastSynced:null, chartState:new WeakMap(), error:null, locale:'tr', responseCount:0, drag:null, skipClickUntil:0};
window.PrognodeFullscreen=store;
let noticeTimer=null;
const L=(tr,en)=>en;

const enSpans={'5m':'5 minutes','15m':'15 minutes','1h':'1 hour','8h':'8 hours','24h':'24 hours','7d':'7 days'};
function translateStatic(){
 document.documentElement.lang=store.locale;
 document.querySelectorAll('[data-tr][data-en]').forEach(el=>{el.textContent=el.getAttribute('data-en')});
 for(const b of document.querySelectorAll('[data-period]'))b.textContent=({en:{'5m':'5m','15m':'15m','1h':'1h','8h':'8h','24h':'24h','7d':'7d'}}[store.locale])[b.dataset.period];
 const search=$('signalSearch');if(search)search.placeholder='Search signals or devices…';
 const events=$('eventModal')?.querySelector('[data-event-label]');if(events)events.textContent='ALARM EVENT';
 syncButtons();
}
function notify(s){const n=$('notice');n.textContent=s;n.classList.add('show');clearTimeout(noticeTimer);noticeTimer=setTimeout(()=>n.classList.remove('show'),4000)}
const idTag=id=>store.catalog.find(s=>s.id===id);
const localDate=n=>new Date(n).toLocaleString('en-US',{day:'2-digit',month:'2-digit',hour:'2-digit',minute:'2-digit',second:'2-digit'});
const localTime=n=>new Date(n).toLocaleTimeString('en-US',{hour:'2-digit',minute:'2-digit'});
const isoLocal=n=>{const d=new Date(n);return [d.getFullYear(),String(d.getMonth()+1).padStart(2,'0'),String(d.getDate()).padStart(2,'0')].join('-')+'T'+[d.getHours(),d.getMinutes()].map(v=>String(v).padStart(2,'0')).join(':')};
const clamp=(v,a,b)=>Math.max(a,Math.min(b,v));
function periodRange(){if(!store.live&&store.range)return store.range;const now=Date.now();return {from:now-durations[store.period]*60000,to:now}}
const real=p=>p && String(p.quality||'').toUpperCase()==='GOOD' && p.value!==null && p.value!==undefined && Number.isFinite(+p.value) && Number.isFinite(Date.parse(p.timestamp));
function lastGood(data){return [...(data?.points||[])].reverse().find(real)||null}
function alertLimits(s){return store.definitions.filter(a=>a.tagId===s.id && a.enabled!==false &&
 ['GreaterThan','GreaterThanOrEqual','LessThan','LessThanOrEqual'].includes(a.condition) && a.threshold!=null && Number.isFinite(+a.threshold)).map(a=>({...a,threshold:+a.threshold,high:a.condition.startsWith('Greater')}));}
function severity(s,v){if(!Number.isFinite(v))return 'UNKNOWN';const l=alertLimits(s);return l.some(a=>a.high&&(a.condition==='GreaterThan'?v>a.threshold:v>=a.threshold))?'HIGH':l.some(a=>!a.high&&(a.condition==='LessThan'?v<a.threshold:v<=a.threshold))?'LOW':'NORMAL'}
function reportError(error){store.error=String(error?.message||error);$('statusText').textContent='Core: '+store.error;notify('Core API: '+store.error)}
async function api(path){const token=sessionStorage.getItem('prognode.accessSession')||'';const r=await fetch(path,{headers:token?{'X-PROGNODE-Session':token}:{},cache:'no-store'});
 const raw=await r.text();let obj;try{obj=JSON.parse(raw)}catch{throw new Error('Core API did not return JSON: '+path+' ('+r.status+')')}
 if(!r.ok)throw new Error(obj?.message||obj?.title||`HTTP ${r.status} ${path}`);return obj;}
function move(page){try{if(window.parent&&window.parent!==window&&typeof window.parent.navigate==='function')window.parent.navigate(page);else window.location.href='/?page='+encodeURIComponent(page)}catch{window.location.href='/?page='+encodeURIComponent(page)}}
function syncParentAppearance(){try{const p=window.parent;if(!p||p===window){
  document.documentElement.dataset.theme=localStorage.getItem('prognode.theme')||'dark';store.locale='en';
 }else{const root=p.document.documentElement;document.documentElement.dataset.theme=root.dataset.theme||'dark';store.locale='en';}
 translateStatic();
 }catch{}}
function contextSignals(){return store.catalog.filter(s=>store.selected.includes(s.id))}
async function catalog(){const [tags,devices,historian,defs]=await Promise.all([
 api('/api/tags'),api('/api/devices'),api('/api/historian/configurations'),api('/api/alarms/definitions')
]);
 const devmap=new Map(devices.map(d=>[d.id,d.name]));
 const cfgs=new Map(historian.filter(h=>h.configuration?.enabled===true).map(h=>[h.configuration.tagId,h.configuration]));const recorded=new Set(cfgs.keys());
 const before=new Set(store.selected);
 store.catalog=tags.filter(t=>recorded.has(t.id)).map((t,i)=>({id:t.id,name:[devmap.get(t.deviceId)||'PLC',t.name].join(' · '),unit:t.unit||'',digital:String(t.dataType||'').toLowerCase()==='bool',digits:Math.min(4,Math.max(0,+t.decimalPlaces||0)),sampleSeconds:Math.max(1,Number(cfgs.get(t.id)?.sampleIntervalSeconds)||10),color:palette[i%palette.length]}));
 store.selected=store.selected.filter(id=>store.catalog.some(t=>t.id===id));
 store.comparing=new Set([...store.comparing].filter(id=>store.selected.includes(id)));
 store.definitions=defs;
 if([...before].some(id=>!store.selected.includes(id)))notify('Chart removed because Historian recording was disabled.');
 renderDrawer();render();
}
function renderDrawer(){const q=($('signalSearch').value||'').toLocaleLowerCase('en-US').trim();const shown=store.catalog.filter(s=>s.name.toLocaleLowerCase('en-US').includes(q));
 $('historianList').innerHTML=shown.length?shown.map(s=>`<button class="signal-item" data-add="${esc(s.id)}" ${store.selected.includes(s.id)?'disabled':''} style="--c:${s.color}"><span style="width:5px;height:30px;border-radius:3px;background:${s.color}"></span><span style="flex:1"><strong>${esc(s.name)}</strong><div class="desc">${esc('Recording interval')}: ${s.sampleSeconds} ${esc('s')}</div></span><span class="number">${store.selected.includes(s.id)?'✓':'＋'}</span></button>`).join(''):(store.error?`<p class="helper" style="padding:12px">${esc(store.error)}</p>`:`<p class="helper" style="padding:12px">${'No recorded signals. Enable recording in Historian first.'}</p>`);
}
function drawer(open){$('drawer').classList.toggle('visible',typeof open==='boolean'?open:!$('drawer').classList.contains('visible'));if($('drawer').classList.contains('visible')){renderDrawer();$('signalSearch').focus()}}
function chartRange(){return store.range||periodRange()}
function eventsFor(s){const r=chartRange();return store.alarms.filter(a=>{const tag=a.tagId||store.definitions.find(d=>d.id===a.definitionId)?.tagId;const t=Date.parse(a.activeAt||a.activatedAt||'');return tag===s.id&&Number.isFinite(t)&&t>=r.from&&t<=r.to})}
function chartMarkup(s){const data=store.series.get(s.id);const last=lastGood(data);const state=last?severity(s,+last.value):'NO DATA';const val=last?(+last.value).toLocaleString('en-US',{maximumFractionDigits:s.digits,minimumFractionDigits:Math.min(1,s.digits)}):'—';
 const total=(data?.points||[]).length,stale=last?Math.round((Date.now()-Date.parse(last.timestamp))/1000):null,rawCount=data?.sampleCount??total,aggregated=rawCount>total;
 const meta=(!total?'No samples yet':last?`${s.sampleSeconds}${' s interval'} · ${aggregated?rawCount+' '+'samples / summarized':total+' '+'saved points'} · ${'Last sample'} ${stale}${' s ago'}`:'No valid sample · BAD/STALE');
 const limits=alertLimits(s),low=limits.filter(x=>!x.high),high=limits.filter(x=>x.high);
 const stats=data&&data.minimum!=null&&data.maximum!=null&&Number.isFinite(+data.minimum)&&Number.isFinite(+data.maximum)?{min:+data.minimum,max:+data.maximum,avg:data.average==null?null:Number(data.average)}:null;
 return `<article class="chart-card ${store.maxed===s.id?'maximized':''}" data-id="${esc(s.id)}">
 <div class="chart-header"><span class="signal-marker" style="background:${s.color}"></span>${store.compareMode?`<input class="compare-check" type="checkbox" data-select="${esc(s.id)}" aria-label="${esc(s.name)} karşılaştır" ${store.comparing.has(s.id)?'checked':''}>`:''}
 <span class="chart-name">${esc(s.name)}</span><span class="reading" style="color:${state==='HIGH'?'var(--red)':state==='LOW'?'var(--amber)':s.color}">${val}</span><span class="units">${esc(s.unit)}</span><span class="chart-meta ${last?'':'state-bad'}">${esc(state)} · ${esc(meta)}</span>
 <span class="chart-tools"><button class="mini" title="Expand / restore" data-zoom="${esc(s.id)}">${store.maxed===s.id?'▣':'⛶'}</button><button class="mini remove" title="Close chart" data-remove="${esc(s.id)}">×</button></span></div>
 <div class="chart-plot"><canvas data-canvas="${esc(s.id)}" aria-label="${esc(s.name)} Historian"></canvas><div class="chart-select"></div><div class="chart-tooltip"></div></div>
 <div class="chart-footer">${stats?`<span title="${'Recorded sample statistics for selected range'}">${'Min'} <b>${stats.min.toFixed(s.digits)}</b> · ${'Max'} <b>${stats.max.toFixed(s.digits)}</b> · ${'Avg'} <b>${stats.avg!=null&&Number.isFinite(stats.avg)?stats.avg.toFixed(s.digits):'—'}</b></span>`:''}${low.map(a=>`<span>LOW <b style="color:var(--amber)">${a.threshold} ${esc(s.unit)}</b></span>`).join('')}${high.map(a=>`<span>HIGH <b style="color:var(--red)">${a.threshold} ${esc(s.unit)}</b></span>`).join('')}<span style="margin-left:auto">${esc((enSpans)[store.period]||'Custom range')} · ${store.live?'LIVE':'PAUSED'} · ${'Drag: zoom'}</span></div></article>`}
function render(){syncButtons();const w=$('workspace');if(store.comparison){renderComparison();return}
 const arr=store.maxed?store.selected.filter(id=>id===store.maxed):store.selected;
 w.className='workspace '+(store.maxed?'maxed':'count-'+clamp(arr.length,1,8));
 w.innerHTML=arr.length?arr.map(id=>chartMarkup(idTag(id))).join(''):`<div class="empty"><svg viewBox="0 0 56 56" width="56" fill="none" stroke-width="1.8"><rect x="4" y="6" width="48" height="40" rx="6"/><path d="M11 35 22 25l9 5 13-16"/></svg><strong>${'Chart workspace ready'}</strong><p>${'Choose an enabled Historian recording to open its chart.'}</p><button class="action primary" id="emptyAdd">＋ ${'Add from Historian'}</button></div>`;
 if(!arr.length)$('emptyAdd').onclick=()=>drawer(true);
 requestAnimationFrame(drawAll);}
function syncButtons(){document.querySelectorAll('[data-period]').forEach(b=>b.classList.toggle('active',b.dataset.period===store.period&&!store.custom));
 $('liveButton').textContent=store.live?'● LIVE · PAUSE':'Ⅱ PAUSED · RESUME';$('liveButton').className='action '+(store.live?'live':'pause');
 $('flagButton').classList.toggle('active',store.flags);$('thresholdButton').classList.toggle('active',store.levels);$('pointButton').classList.toggle('active',store.points);$('compareButton').classList.toggle('active',store.compareMode||store.comparison);
 $('compareBar').classList.toggle('visible',store.compareMode);$('statusText').textContent=store.error?'Core connection: '+store.error:`${store.live?'LIVE':'PAUSED'} · ${store.selected.length} ${'charts'} · ${store.lastSynced?localDate(store.lastSynced):'Waiting for data'}`;
 $('launchCompare').textContent='Compare selected'+(store.comparing.size?' ('+store.comparing.size+')':'');
 $('cursorA').classList.toggle('active',store.cursorMode==='A');$('cursorB').classList.toggle('active',store.cursorMode==='B');
}
const css=x=>getComputedStyle(document.documentElement).getPropertyValue(x).trim();
function calcBounds(s,data){const values=(data?.points||[]).filter(real).map(p=>+p.value);const limits=alertLimits(s);const all=[...values,...limits.map(a=>a.threshold)];let min=all.length?Math.min(...all):0,max=all.length?Math.max(...all):1;
 if(!Number.isFinite(min)||!Number.isFinite(max)){min=0;max=1}const d=max-min||Math.max(1,Math.abs(min)*.2);return {min:min-d*.1,max:max+d*.1};}
// Real timestamp spacing. BAD/STALE split the spline; no interpolation across invalid quality.
function validRuns(points,x,y,gapMs=Infinity){const runs=[];let run=[];
 for(const p of points||[]){if(!real(p)){if(run.length){runs.push(run);run=[]}continue}
 const t=Date.parse(p.timestamp);if(run.length&&t<=run[run.length-1].t)continue;if(run.length&&t-run[run.length-1].t>gapMs){runs.push(run);run=[];}
 run.push({x:x(t),y:y(+p.value),v:+p.value,t});}
 if(run.length)runs.push(run);return runs;}
function spline(ctx,pts){if(pts.length<2)return;let n=pts.length,d=[],m=new Array(n);
 for(let i=0;i<n-1;i++){const h=pts[i+1].x-pts[i].x;if(h<=0)return;d.push((pts[i+1].y-pts[i].y)/h)}
 m[0]=d[0];m[n-1]=d[n-2];for(let i=1;i<n-1;i++)m[i]=d[i-1]*d[i]<=0?0:2*d[i-1]*d[i]/(d[i-1]+d[i]);
 for(let i=0;i<n-1;i++){if(d[i]===0){m[i]=m[i+1]=0;continue}const a=m[i]/d[i],b=m[i+1]/d[i],sq=a*a+b*b;if(sq>9){const t=3/Math.sqrt(sq);m[i]=t*a*d[i];m[i+1]=t*b*d[i]}}
 ctx.beginPath();ctx.moveTo(pts[0].x,pts[0].y);for(let i=0;i<n-1;i++){const p=pts[i],q=pts[i+1],h=q.x-p.x;ctx.bezierCurveTo(p.x+h/3,p.y+m[i]*h/3,q.x-h/3,q.y-m[i+1]*h/3,q.x,q.y)}}
function strokeSeries(ctx,runs,regions,isDigital,width){for(const r of runs){if(r.length===1){ctx.fillStyle=regions[0].color;ctx.fillRect(r[0].x-2,r[0].y-2,4,4);continue}
 for(const region of regions){ctx.save();ctx.beginPath();ctx.rect(region.left,region.top,region.width,Math.max(0,region.bottom-region.top));ctx.clip();
 ctx.strokeStyle=region.color;ctx.lineWidth=width;ctx.lineJoin='round';ctx.lineCap='round';ctx.globalAlpha=1;
 if(isDigital){ctx.beginPath();ctx.moveTo(r[0].x,r[0].y);for(let i=1;i<r.length;i++){ctx.lineTo(r[i].x,r[i-1].y);ctx.lineTo(r[i].x,r[i].y)}}else spline(ctx,r);
 ctx.stroke();ctx.restore();}}
}
function viewTime(t){return clamp((t-chartRange().from)/Math.max(1,chartRange().to-chartRange().from),0,1)}
function drawChart(canvas,s,hoverTime=null){if(!canvas?.isConnected)return;const wrap=canvas.parentElement,box=wrap.getBoundingClientRect();if(box.width<80||box.height<60)return;
 const ratio=Math.min(2,window.devicePixelRatio||1),W=box.width,H=box.height;canvas.width=Math.round(W*ratio);canvas.height=Math.round(H*ratio);const ctx=canvas.getContext('2d');ctx.scale(ratio,ratio);
 const pad={l:15,r:57,t:16,b:28},pw=W-pad.l-pad.r,ph=H-pad.t-pad.b;
 const data=store.series.get(s.id),bounds=calcBounds(s,data),r=chartRange(),x=t=>pad.l+(t-r.from)/Math.max(1,r.to-r.from)*pw,y=v=>pad.t+ph*(bounds.max-v)/(bounds.max-bounds.min);
 const levels=alertLimits(s);ctx.font='10px Segoe UI';ctx.fillStyle=css('--muted');ctx.lineWidth=1;ctx.textBaseline='middle';
 for(let i=0;i<=4;i++){const yy=pad.t+ph*i/4;ctx.beginPath();ctx.strokeStyle=css('--grid');ctx.moveTo(pad.l,yy);ctx.lineTo(pad.l+pw,yy);ctx.stroke();const val=bounds.max-(bounds.max-bounds.min)*i/4;ctx.fillText(val.toLocaleString('tr-TR',{maximumFractionDigits:2}),W-pad.r+7,yy)}
 for(let i=0;i<=4;i++){const xx=pad.l+pw*i/4,t=r.from+(r.to-r.from)*i/4;ctx.strokeStyle=css('--grid');ctx.beginPath();ctx.moveTo(xx,pad.t);ctx.lineTo(xx,pad.t+ph);ctx.stroke();ctx.fillStyle=css('--muted');ctx.textAlign=i===0?'left':i===4?'right':'center';ctx.fillText(localTime(t),xx,H-11)}ctx.textAlign='left';
 if(store.levels)for(const lv of levels){const yy=y(lv.threshold);if(yy<pad.t-3||yy>pad.t+ph+3)continue;ctx.save();ctx.strokeStyle=lv.high?css('--red'):css('--amber');ctx.setLineDash([4,4]);ctx.beginPath();ctx.moveTo(pad.l,yy);ctx.lineTo(pad.l+pw,yy);ctx.stroke();ctx.setLineDash([]);ctx.font='10px Segoe UI';ctx.fillStyle=lv.high?css('--red'):css('--amber');ctx.textAlign='right';ctx.fillText((lv.high?'HIGH ':'LOW ')+lv.threshold,pad.l+pw-4,yy-7);ctx.restore()}
 const highs=levels.filter(a=>a.high).map(a=>a.threshold),lows=levels.filter(a=>!a.high).map(a=>a.threshold),hi=highs.length?Math.min(...highs):null,lo=lows.length?Math.max(...lows):null;
 const bands=[];const top=pad.t-2,bottom=pad.t+ph+2,left=pad.l-2,width=pw+4;
 if(hi!==null&&lo!==null&&lo<hi){bands.push({left,width,top,bottom:y(hi),color:css('--red')},{left,width,top:y(hi),bottom:y(lo),color:s.color},{left,width,top:y(lo),bottom,color:css('--amber')})}
 else if(hi!==null){bands.push({left,width,top,bottom:y(hi),color:css('--red')},{left,width,top:y(hi),bottom,color:s.color})}
 else if(lo!==null){bands.push({left,width,top,bottom:y(lo),color:s.color},{left,width,top:y(lo),bottom,color:css('--amber')})}
 else bands.push({left,width,top,bottom,color:s.color});
 const runs=validRuns(data?.points||[],x,y,Math.max(s.sampleSeconds*2.5*1000,1000));strokeSeries(ctx,runs,bands,s.digital,2.25);
 // Dots represent exactly recorded samples; bucketed long-range results are labeled summarized and never masquerade as raw samples.
 if(store.points&&(data?.sampleCount??(data?.points||[]).length)===(data?.points||[]).length){
  const valid=(data?.points||[]).filter(real);if(valid.length<=900){ctx.save();ctx.lineWidth=1.1;ctx.strokeStyle=css('--card');
  for(const point of valid){const xx=x(Date.parse(point.timestamp)),yy=y(+point.value);if(xx<pad.l||xx>pad.l+pw||yy<pad.t||yy>pad.t+ph)continue;
   const sev=severity(s,+point.value);ctx.fillStyle=sev==='HIGH'?css('--red'):sev==='LOW'?css('--amber'):s.color;
   ctx.beginPath();ctx.arc(xx,yy,valid.length>400?1.9:2.7,0,Math.PI*2);ctx.fill();if(valid.length<=400)ctx.stroke();}
  ctx.restore();}}

 const hits=[];if(store.flags){for(const e of eventsFor(s)){const xx=x(Date.parse(e.activeAt||e.activatedAt));if(xx<pad.l||xx>pad.l+pw)continue;ctx.strokeStyle=css('--red');ctx.globalAlpha=.55;ctx.setLineDash([4,4]);ctx.beginPath();ctx.moveTo(xx,pad.t+8);ctx.lineTo(xx,pad.t+ph);ctx.stroke();ctx.setLineDash([]);ctx.globalAlpha=1;ctx.fillStyle=css('--red');ctx.beginPath();ctx.moveTo(xx-5,pad.t+3);ctx.lineTo(xx+5,pad.t+3);ctx.lineTo(xx,pad.t+12);ctx.fill();hits.push({x:xx,y:pad.t+8,event:e})}}
 for(const key of ['A','B'])if(store.cursors[key]!=null){const xx=x(store.cursors[key]);if(xx>=pad.l&&xx<=pad.l+pw){ctx.strokeStyle=key==='A'?css('--cyan'):css('--amber');ctx.setLineDash([2,4]);ctx.beginPath();ctx.moveTo(xx,pad.t);ctx.lineTo(xx,pad.t+ph);ctx.stroke();ctx.setLineDash([]);ctx.font='bold 10px Segoe UI';ctx.fillStyle=key==='A'?css('--cyan'):css('--amber');ctx.fillText(key,xx+3,pad.t+19)}}
 if(hoverTime!=null){const xx=x(hoverTime);ctx.strokeStyle=css('--muted');ctx.setLineDash([3,4]);ctx.beginPath();ctx.moveTo(xx,pad.t);ctx.lineTo(xx,pad.t+ph);ctx.stroke();ctx.setLineDash([])}
 store.chartState.set(canvas,{s,data,range:r,pad,pw,ph,x,y,W,H,hits});}
function drawAll(){document.querySelectorAll('canvas[data-canvas]').forEach(c=>drawChart(c,idTag(c.dataset.canvas)))}
function boundsForCompare(s){return calcBounds(s,store.series.get(s.id))}
function drawComparison(){const ids=[...store.comparing].filter(id=>store.selected.includes(id));const canvas=$('compareCanvas');if(!canvas?.isConnected)return;const box=canvas.parentElement.getBoundingClientRect();if(box.width<100)return;
 const rat=Math.min(2,window.devicePixelRatio||1),W=box.width,H=box.height;canvas.width=Math.round(W*rat);canvas.height=Math.round(H*rat);const ctx=canvas.getContext('2d');ctx.scale(rat,rat);const pad={l:48,r:24,t:25,b:35},pw=W-pad.l-pad.r,ph=H-pad.t-pad.b,r=chartRange();
 for(let i=0;i<=5;i++){const yy=pad.t+ph*i/5;ctx.beginPath();ctx.strokeStyle=css('--grid');ctx.moveTo(pad.l,yy);ctx.lineTo(pad.l+pw,yy);ctx.stroke();ctx.font='10px Segoe UI';ctx.fillStyle=css('--muted');ctx.fillText((100-20*i)+'%',8,yy+3)}
 for(const id of ids){const s=idTag(id),b=boundsForCompare(s),x=t=>pad.l+((t-r.from)/Math.max(1,r.to-r.from))*pw,y=v=>pad.t+ph*(b.max-v)/(b.max-b.min);
 const runs=validRuns(store.series.get(id)?.points||[],x,y,Math.max(s.sampleSeconds*2.5*1000,1000));strokeSeries(ctx,runs,[{left:pad.l,top:pad.t,width:pw,bottom:pad.t+ph,color:s.color}],s.digital,2.2)}
 for(let i=0;i<=4;i++){const xx=pad.l+pw*i/4;ctx.fillStyle=css('--muted');ctx.fillText(localTime(r.from+(r.to-r.from)*i/4),xx-10,H-12)}store.chartState.set(canvas,{range:r,pad,pw,ph});}
function renderComparison(){const ids=[...store.comparing].filter(id=>store.selected.includes(id));if(ids.length<2){store.comparison=false;return render()}
 const w=$('workspace');w.className='workspace count-1';w.innerHTML=`<article class="chart-card maximized"><div class="chart-header"><span class="chart-name">${'Comparison'} · ${ids.map(id=>esc(idTag(id).name)).join(' + ')}</span><span class="chart-tools"><button class="action outline" id="leaveCompare">← ${'Separate charts'}</button></span></div><div class="chart-plot"><canvas id="compareCanvas"></canvas><div class="chart-select"></div><div class="chart-tooltip"></div></div><div class="chart-footer">${ids.map(id=>`<span style="color:${idTag(id).color}">━━ ${esc(idTag(id).name)}</span>`).join('')}<span style="margin-left:auto">${'Independent scaling per signal'}</span></div></article>`;
 $('leaveCompare').onclick=()=>{store.comparison=false;store.compareMode=false;store.comparing.clear();render()};requestAnimationFrame(drawComparison);}
function eventDetail(e){$('eventTitle').textContent=(idTag(e.tagId)?.name||e.sourceName||'Alarm')+' · '+(e.text||'Alarm');
 $('eventDescription').textContent=`${localDate(Date.parse(e.activeAt||e.activatedAt))} · ${e.state||'ACTIVE'} · ${e.priority||''}${e.clearedAt?' · '+'Cleared: '+localDate(Date.parse(e.clearedAt)):''}`;
 $('eventModal').classList.add('visible')}
function nearest(s,t){let arr=store.series.get(s.id)?.points?.filter(real)||[];if(!arr.length)return null;let lo=0,hi=arr.length-1;while(lo<hi){const mid=(lo+hi)>>1;if(Date.parse(arr[mid].timestamp)<t)lo=mid+1;else hi=mid}const a=arr[lo],b=arr[Math.max(0,lo-1)];return b&&Math.abs(Date.parse(b.timestamp)-t)<Math.abs(Date.parse(a.timestamp)-t)?b:a}
function cursorSummary(){const a=store.cursors.A,b=store.cursors.B;if(a==null&&b==null){$('cursorInfo').hidden=true;return}
 $('cursorInfo').hidden=false;let html=`<strong>${'Cursor comparison'}</strong><br>A: ${a?localDate(a):'—'} · B: ${b?localDate(b):'—'}`;if(a&&b){const secs=Math.round(Math.abs(b-a)/1000);html+=`<br>Δt: ${secs>=3600?(secs/3600).toFixed(2)+' sa':secs>=60?(secs/60).toFixed(2)+' dk':secs+' sn'}`;
 for(const id of store.selected){const s=idTag(id),p=nearest(s,a),q=nearest(s,b);if(p&&q)html+=`<br>${esc(s.name)}: A ${(+p.value).toFixed(s.digits)} → B ${(+q.value).toFixed(s.digits)} · Δ ${(+q.value-p.value).toFixed(s.digits)} ${esc(s.unit)}`}}
 $('cursorInfo').innerHTML=html}
function refreshClock(){if(store.live&&store.selected.length){const now=Date.now();store.range={from:now-durations[store.period]*60000,to:now}}}
async function context(){if(!store.selected.length)return;const r=chartRange();const [alarms,batches]=await Promise.allSettled([api('/api/alarms/history?limit=500'),api('/api/batches?limit=100')]);
 if(alarms.status==='fulfilled')store.alarms=alarms.value.filter(a=>{const t=Date.parse(a.activeAt||a.activatedAt||'');return Number.isFinite(t)&&t>=r.from&&t<=r.to});
 if(batches.status==='fulfilled')store.batches=batches.value.filter(b=>{const t=Date.parse(b.startedAt||'');return Number.isFinite(t)&&t<=r.to&&(Date.parse(b.endedAt||'')||Date.now())>=r.from});
}
async function refresh(force=false){if(!store.visible||!store.selected.length||(!store.live&&!force)||store.pending)return;
 const ids=store.selected.filter(id=>store.catalog.some(s=>s.id===id));if(!ids.length)return;refreshClock();const r=chartRange(),seq=++store.requestSeq;store.pending=true;
 try{const q=new URLSearchParams({tagIds:ids.join(','),from:new Date(r.from).toISOString(),to:new Date(r.to).toISOString(),maxPoints:'1800'});
 const payload=await api('/api/trend-studio/series?'+q.toString());if(seq!==store.requestSeq)return;
 for(const item of payload.series||[])store.series.set(item.tagId,item);
 store.error=null;store.lastSynced=Date.now();store.responseCount++;
 if(store.responseCount%5===1)await context();render();cursorSummary();
 }catch(e){if(seq===store.requestSeq)reportError(e)}finally{store.pending=false;syncButtons()}}
function pause(){if(store.live){store.live=false;store.range=chartRange()}else{store.live=true;store.custom=false;store.range=null;store.cursors={A:null,B:null}}syncButtons();if(store.live)void refresh(true);else render()}
function reset(){store.live=true;store.custom=false;store.range=null;store.maxed=null;store.comparison=false;store.compareMode=false;store.comparing.clear();store.cursors={A:null,B:null};$('timeCustom').hidden=true;void refresh(true);render()}
function exportCsv(){if(store.selected.some(id=>{const d=store.series.get(id);return d&&d.sampleCount>d.points.length;})){
 notify('This long-range view is summarized. Use Historian export for complete raw CSV, or zoom in.');return;}
 const rows=[['Device / Tag','Timestamp UTC','Value','Quality','Unit']];for(const id of store.selected){const s=idTag(id);for(const p of store.series.get(id)?.points||[])rows.push([s.name,new Date(p.timestamp).toISOString().slice(0,19).replace('T',' '),p.value??'',p.quality||'',s.unit])}
 if(rows.length===1){notify('Open a chart before exporting CSV.');return}const csv='\uFEFF'+rows.map(r=>r.map(v=>'"'+String(v).replaceAll('"','""')+'"').join(';')).join('\r\n');save(new Blob([csv],{type:'text/csv;charset=utf-8'}),'PROGNODE_Visible_Trend.csv')}
function save(blob,name){const url=URL.createObjectURL(blob),a=document.createElement('a');a.href=url;a.download=name;document.body.appendChild(a);a.click();a.remove();setTimeout(()=>URL.revokeObjectURL(url),1000)}
function exportPng(){const cvs=[...$('workspace').querySelectorAll('canvas')].filter(c=>c.width>0);if(!cvs.length){notify('Open a chart first.');return}
 const w=Math.max(...cvs.map(c=>c.width)),h=cvs.reduce((a,c)=>a+c.height,0);const out=document.createElement('canvas');out.width=Math.min(4096,w);out.height=Math.min(8192,h);const ctx=out.getContext('2d');ctx.fillStyle=css('--bg');ctx.fillRect(0,0,out.width,out.height);let y=0;for(const c of cvs){ctx.drawImage(c,0,y,out.width,Math.min(c.height,out.height-y));y+=c.height;if(y>=out.height)break}
 out.toBlob(blob=>blob&&save(blob,'PROGNODE_Trend_Studio.png'),'image/png')}
// TradingView-style range selection: left-button drag across the actual plot pauses LIVE and refetches that exact period.
// Wheel remains untouched so ordinary vertical scrolling works throughout Core.
$('workspace').addEventListener('pointerdown',e=>{if(e.button!==0||store.cursorMode||store.compareMode)return;
 const cv=e.target.closest('canvas[data-canvas],canvas#compareCanvas');if(!cv)return;
 const cs=store.chartState.get(cv);if(!cs)return;const plot=cv.closest('.chart-plot'),rc=cv.getBoundingClientRect();
 const xx=clamp(e.clientX-rc.left,cs.pad.l,cs.pad.l+cs.pw);if(e.clientY-rc.top<cs.pad.t+17||e.clientY-rc.top>cs.pad.t+cs.ph)return;
 store.drag={cv,plot,cs,startX:xx,lastX:xx};const overlay=plot.querySelector('.chart-select');overlay.style.display='block';overlay.style.left=xx+'px';overlay.style.width='0px';plot.classList.add('zoom-dragging');
 cv.setPointerCapture(e.pointerId);
});
$('workspace').addEventListener('pointermove',e=>{const d=store.drag;if(!d||e.target!==d.cv)return;const rc=d.cv.getBoundingClientRect();d.lastX=clamp(e.clientX-rc.left,d.cs.pad.l,d.cs.pad.l+d.cs.pw);
 const o=d.plot.querySelector('.chart-select');o.style.left=Math.min(d.startX,d.lastX)+'px';o.style.width=Math.abs(d.lastX-d.startX)+'px';});
function endDrag(cancel=false){const d=store.drag;if(!d)return;store.drag=null;d.plot.classList.remove('zoom-dragging');d.plot.querySelector('.chart-select').style.display='none';
 if(cancel||Math.abs(d.lastX-d.startX)<20)return;
 const {pad,pw,range}=d.cs,start=clamp((Math.min(d.startX,d.lastX)-pad.l)/pw,0,1),end=clamp((Math.max(d.startX,d.lastX)-pad.l)/pw,0,1);
 const from=range.from+(range.to-range.from)*start,to=range.from+(range.to-range.from)*end;if(to-from<1000)return;
 store.live=false;store.custom=true;store.range={from,to};store.maxed=null;store.skipClickUntil=Date.now()+500;
 render();void refresh(true);notify('Zoomed to selected range. Use ↻ to return LIVE.');
}
$('workspace').addEventListener('pointerup',()=>endDrag());$('workspace').addEventListener('pointercancel',()=>endDrag(true));
// UI events: no mouse-wheel zoom. Graph click only enlarges outside cursor/flag selection.
$('workspace').addEventListener('click',e=>{if(Date.now()<store.skipClickUntil)return;const rm=e.target.closest('[data-remove]');if(rm){const id=rm.dataset.remove;store.selected=store.selected.filter(x=>x!==id);store.comparing.delete(id);if(store.maxed===id)store.maxed=null;renderDrawer();render();return}
 if(e.target.closest('.compare-check'))return;const z=e.target.closest('[data-zoom]');if(z){const id=z.dataset.zoom;store.maxed=store.maxed===id?null:id;render();return}
 const plot=e.target.closest('.chart-plot');if(!plot||store.comparison)return;const canvas=plot.querySelector('[data-canvas]');if(!canvas)return;
 const cs=store.chartState.get(canvas),rect=canvas.getBoundingClientRect();if(!cs)return;const xx=e.clientX-rect.left;
 const flag=store.flags&&cs.hits.find(hit=>Math.abs(hit.x-xx)<12&&e.clientY-rect.top<32);if(flag){eventDetail(flag.event);return}
 if(store.cursorMode){store.cursors[store.cursorMode]=cs.range.from+(xx-cs.pad.l)/Math.max(1,cs.pw)*(cs.range.to-cs.range.from);store.cursors[store.cursorMode]=clamp(store.cursors[store.cursorMode],cs.range.from,cs.range.to);store.cursorMode=null;render();cursorSummary();return}
 const card=plot.closest('.chart-card');store.maxed=store.maxed===card.dataset.id?null:card.dataset.id;render()});
$('workspace').addEventListener('change',e=>{const item=e.target.closest('[data-select]');if(!item)return;if(item.checked)store.comparing.add(item.dataset.select);else store.comparing.delete(item.dataset.select);syncButtons()});
$('workspace').addEventListener('mousemove',e=>{const cv=e.target.closest('canvas[data-canvas]');if(!cv)return;const st=store.chartState.get(cv);if(!st||!st.s)return;const rc=cv.getBoundingClientRect(),px=e.clientX-rc.left,py=e.clientY-rc.top,t=st.range.from+(px-st.pad.l)/Math.max(1,st.pw)*(st.range.to-st.range.from),nearestPt=nearest(st.s,t);
 drawChart(cv,st.s,t);const tip=cv.parentElement.querySelector('.chart-tooltip');tip.style.display='block';tip.style.left=clamp(px+12,4,Math.max(4,rc.width-178))+'px';tip.style.top=clamp(py-49,4,Math.max(4,rc.height-77))+'px';tip.innerHTML=`<strong>${esc(nearestPt?localDate(Date.parse(nearestPt.timestamp)):localDate(t))} · ${esc('Sample time')}</strong><br>${esc(st.s.name)}: ${nearestPt?esc((+nearestPt.value).toFixed(st.s.digits)+' '+st.s.unit):'NO DATA'}${nearestPt?' · '+esc(nearestPt.quality):''}`});
$('workspace').addEventListener('mouseout',e=>{const cv=e.target.closest('canvas[data-canvas]');if(cv&&!cv.contains(e.relatedTarget)){cv.parentElement.querySelector('.chart-tooltip').style.display='none';drawChart(cv,idTag(cv.dataset.canvas))}});
$('historianList').addEventListener('click',e=>{const b=e.target.closest('[data-add]');if(!b||b.disabled)return;if(store.selected.length>=8){notify('Maximum 8 charts at once.');return}
 store.selected.push(b.dataset.add);store.maxed=null;store.comparison=false;renderDrawer();drawer(false);render();void refresh(true)});
$('addSignal').onclick=()=>drawer();$('closeDrawer').onclick=()=>drawer(false);$('signalSearch').addEventListener('input',renderDrawer);
document.querySelectorAll('[data-go]').forEach(b=>b.addEventListener('click',()=>move(b.dataset.go)));
$('periods').onclick=e=>{const b=e.target.closest('[data-period]');if(!b)return;store.period=b.dataset.period;store.custom=false;store.range=store.live?null:{from:(store.range?.to||Date.now())-durations[store.period]*60000,to:store.range?.to||Date.now()};render();void refresh(true)};
$('liveButton').onclick=pause;$('resetButton').onclick=reset;$('flagButton').onclick=()=>{store.flags=!store.flags;render()};$('pointButton').onclick=()=>{store.points=!store.points;render()};$('thresholdButton').onclick=()=>{store.levels=!store.levels;render()};
$('compareButton').onclick=()=>{if(store.comparison){store.comparison=false;store.compareMode=false;store.comparing.clear()}else{store.compareMode=!store.compareMode;store.comparing.clear()}store.maxed=null;render()};
$('cancelCompare').onclick=()=>{store.compareMode=false;store.comparing.clear();render()};$('launchCompare').onclick=()=>{if(store.comparing.size<2){notify('Select at least two charts.');return}store.comparison=true;store.compareMode=false;render()};
$('clearButton').onclick=()=>{store.selected=[];store.maxed=null;store.compareMode=false;store.comparison=false;store.comparing.clear();store.series.clear();render();renderDrawer();drawer(true)};
$('cursorA').onclick=()=>{if(store.live){store.live=false;store.range=periodRange()}store.cursorMode=store.cursorMode==='A'?null:'A';render()};
$('cursorB').onclick=()=>{if(store.live){store.live=false;store.range=periodRange()}store.cursorMode=store.cursorMode==='B'?null:'B';render()};
$('customRangeButton').onclick=()=>{if(store.live){notify('Pause LIVE before selecting a custom range.');return}const r=chartRange();$('rangeFrom').value=isoLocal(r.from);$('rangeTo').value=isoLocal(r.to);$('timeCustom').hidden=!$('timeCustom').hidden};
$('rangeCancel').onclick=()=>$('timeCustom').hidden=true;
$('rangeApply').onclick=()=>{const from=new Date($('rangeFrom').value).getTime(),to=new Date($('rangeTo').value).getTime();if(!Number.isFinite(from)||!Number.isFinite(to)||to-from<1000||to-from>366*86400000){notify('Choose a valid range (up to 366 days).');return}
 store.live=false;store.custom=true;store.range={from,to};$('timeCustom').hidden=true;render();void refresh(true)};
$('csvButton').onclick=exportCsv;$('pngButton').onclick=exportPng;
$('closeEvent').onclick=()=>$('eventModal').classList.remove('visible');
// Theme and language are controlled exclusively by the original PROGNODE Core header.
try{if(window.parent&&window.parent!==window){
 const root=window.parent.document.documentElement;new MutationObserver(()=>{const oldLang=store.locale,oldTheme=document.documentElement.dataset.theme;syncParentAppearance();if(store.locale!==oldLang)render();else if(document.documentElement.dataset.theme!==oldTheme){if(store.comparison)drawComparison();else drawAll();}}).observe(root,{attributes:true,attributeFilter:['lang','data-theme']});
 }}catch{}
window.addEventListener('resize',()=>{if(store.comparison)drawComparison();else drawAll()});
if('ResizeObserver' in window)new ResizeObserver(()=>{if(store.comparison)drawComparison();else drawAll()}).observe($('workspace'));
window.addEventListener('message',e=>{if(e.origin!==location.origin||e.data?.type!=='pgn:trend-visible')return;store.visible=!!e.data.visible;if(store.visible){syncParentAppearance();render();void catalog().then(()=>refresh(true)).catch(reportError)}});
document.addEventListener('visibilitychange',()=>{if(!document.hidden&&store.visible&&store.live)void refresh(true)});
// Safe default if opened directly in browser instead of as an embedded Core page.
store.visible=window.parent===window;
syncParentAppearance();render();
void catalog().then(()=>store.visible&&refresh(true)).catch(reportError);
setInterval(()=>{if(store.visible&&!document.hidden&&store.live)void refresh(false)},4200);
setInterval(()=>{if(store.visible&&!document.hidden)void catalog().catch(e=>{store.error=String(e);renderDrawer()})},45000);
})();
