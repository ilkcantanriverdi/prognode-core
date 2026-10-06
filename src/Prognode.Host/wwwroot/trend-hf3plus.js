
(() => {
'use strict';
const byId=id=>document.getElementById(id), $=s=>document.querySelector(s);
const T={tr:{industrial:'Endüstriyel İzleme',operations:'OPERASYON',overview:'Genel Bakış',devices:'Cihazlar',tags:'Taglar',alarms:'Alarmlar',historian:'Geçmiş Veri',notifications:'Bildirimler',license:'Lisans',settings:'Ayarlar',demo:'Tasarım önizlemesi · Örnek veri',prototype:'PROGNODE · TREND STUDIO',add:'+ Historian’dan ekle',compare:'⇄ Karşılaştır',reset:'↺ Tümünü sıfırla',zoomhint:'Sol sürükle: yakınlaştır · Sağ tık: geri',charts:'grafik',independent:'Her grafiğin zoom’u ve ayarları bağımsız',selected:'seçili',showCompare:'Karşılaştırmayı aç',multiLayout:'Otomatik yerleşim',selectSignals:'Historian sinyalleri',dragGuide:'Tekli veya çoklu seç, ekle ya da sinyalleri grafik alanına sürükle. Yalnız Historian’da aktif Tag’ler listelenir.',selectAll:'Tümünü seç',addSelected:'Seçilenleri ekle',search:'Tag veya cihaz ara...',overlayTitle:'Sinyal karşılaştırma',normalized:'Normalize yüzde · farklı birimler yan yana',maximize:'Büyüt',restore:'Çoklu ekrana dön',settingsChart:'Grafik ayarları',remove:'Kaldır',last:'Son kayıt',samples:'nokta',flags:'Alarm bayrakları',threshold:'Alarm eşikleri',points:'Kayıt noktaları',batch:'Batch / Lot alanı',smooth:'Yumuşak eğri',freeze:'Bu grafiği dondur',resetChart:'Bu grafiğin zoom’unu sıfırla',emptyTitle:'Henüz trend açılmadı',emptyDesc:'Historian’dan tekli veya çoklu Tag ekle. Sürükleyip bırakabilirsin.',normal:'NORMAL',high:'YÜKSEK',low:'DÜŞÜK',drawHint:'Bir grafiğin üstünde sol tuşu basılı tutup aralık seç',chooseTwo:'Karşılaştırmak için en az iki trend seç',limit:'En fazla 15 grafik açılabilir',already:'Bu sinyaller zaten açık',added:'Sinyaller eklendi',undo:'Önceki yakınlaştırmaya dönüldü',noUndo:'Bu grafik zaten tam aralıkta',mock:'Bu yalnız etkileşimli örnek veri önizlemesidir.',helpTitle:'Trend Studio kullanım kılavuzu',linked:'Ortak imleç',saveLayout:'▣ Düzeni kaydet',restoreDefault:'↺ Varsayılan',colWidth:'Sol sütun',left:'Sol',held:'DURAKLATILDI',follow:'CANLI',wide:'İki sütun genişliği',reorder:'Paneli taşı',maxSpan:'Geniş panel',highDetail:'Yakınlaşınca gerçek kayıt',downsample:'Görsel örnek azaltma',layoutSaved:'Düzen kaydedildi',layoutReset:'Varsayılan düzen geri yüklendi'},en:{industrial:'Industrial Monitoring',operations:'OPERATIONS',overview:'Overview',devices:'Devices',tags:'Tags',alarms:'Alarms',historian:'Historian',notifications:'Notifications',license:'License',settings:'Settings',demo:'Design preview · Sample data',prototype:'PROGNODE · TREND STUDIO',add:'+ Add from Historian',compare:'⇄ Compare',reset:'↺ Reset all',zoomhint:'Left drag: zoom · Right-click: back',charts:'charts',independent:'Independent zoom and settings for each chart',selected:'selected',showCompare:'Open comparison',multiLayout:'Auto layout',selectSignals:'Historian signals',dragGuide:'Select one or multiple signals, add them or drag directly into the chart grid. Only Historian-enabled Tags are listed.',selectAll:'Select all',addSelected:'Add selected',search:'Search tags or devices...',overlayTitle:'Signal comparison',normalized:'Normalized % · different units together',maximize:'Maximize',restore:'Back to grid',settingsChart:'Chart settings',remove:'Remove',last:'Last sample',samples:'samples',flags:'Alarm flags',threshold:'Alarm thresholds',points:'Recorded sample points',batch:'Batch / Lot band',smooth:'Smooth curve',freeze:'Freeze this chart',resetChart:'Reset this chart zoom',emptyTitle:'No trends open',emptyDesc:'Add one or more Historian Tags. You can drag and drop.',normal:'NORMAL',high:'HIGH',low:'LOW',drawHint:'Hold the left mouse button and drag a range on a chart',chooseTwo:'Select at least two trends to compare',limit:'Up to 15 charts can be opened',already:'Those signals are already open',added:'Signals added',undo:'Previous zoom restored',noUndo:'This chart already shows its full range',mock:'This is an interactive preview using illustrative data.',helpTitle:'Trend Studio guide',linked:'Linked cursor',saveLayout:'▣ Save layout',restoreDefault:'↺ Defaults',colWidth:'Left column',left:'Left',held:'PAUSED',follow:'LIVE',wide:'Span two columns',reorder:'Move panel',maxSpan:'Wide panel',highDetail:'Raw samples on zoom',downsample:'Visual downsampling',layoutSaved:'Layout saved',layoutReset:'Default layout restored'}};
let signalDefs=[];
const MAX_CHARTS=15;
const palette=['#27c8e0','#c392f5','#76d0a3','#f2ad72','#e88fb9','#79b8ff','#d1cc74','#78cbd3'];
const allData=new Map();
const metaById=new Map();
let alarmEvents=[];
let activeAlarmCount=0;
let batches=[];
let coreVisible=false;
const Core={ready:false,refreshing:false,error:'',lastCatalogAt:0,seq:0,authenticated:false,authPending:true};
let catalogPromise=null;
let authPromise=null;
let pendingHistorianPromise=null;
function requireTrendEdit(){
 if(Core.authenticated && !Core.authPending)return true;
 toast(S.lang==='tr'?'Trend düzenlemek için lisanslı hesabınızla giriş yapın.':'Sign in with the licensed account to edit Trend Studio.');
 return false;
}
async function refreshTrendAuth(){
 if(authPromise)return authPromise;
 Core.authPending=true;
 authPromise=(async()=>{
  let authenticated=false;
  try{const status=await api('/api/access/status');authenticated=status.authenticated===true;
    if(authenticated){const license=await api('/api/license');authenticated=license.isValid===true && !!license.assignedUserId;}}
  catch(error){console.warn('Trend permission check:',error)}
  const changed=Core.authenticated!==authenticated;
  Core.authenticated=authenticated;Core.authPending=false;
  if(changed&&!authenticated){closeDrawer();S.selected.clear();S.targetSlot=null;}
  applyTrendPermissions();
  if(changed&&authenticated&&Core.ready&&!S.charts.length){await restoreLayout();renderLayout();applyTrendPermissions();}
  return authenticated;
 })().finally(()=>{authPromise=null});
 return authPromise;
}
function applyTrendPermissions(){
 const editable=Core.authenticated&&!Core.authPending;
 const controlled=['add','hf61-explorer-add','select-all','add-selected','save-layout','reset-layout','hf62-layout-select','col-width'];
 for(const id of controlled){const el=byId(id);if(el){el.disabled=!editable;el.title=editable?'':(S.lang==='tr'?'Düzenlemek için giriş yapın':'Sign in to edit');}}
 document.querySelectorAll('.hf61-signal,.move-handle,.hf62-slot,[data-action="remove"],[data-action="settings"],[data-setting],[data-chart-type],.sig').forEach(el=>{
  if(el.matches('button,input,select'))el.disabled=!editable;
  if(el.hasAttribute('draggable'))el.draggable=editable;
  el.classList.toggle('hf63-readonly-control',!editable);
 });
 const warning=byId('hf63-access-hint');if(warning){warning.hidden=editable;warning.textContent=S.lang==='tr'
 ?'Salt görüntüleme: sinyalleri, panel yerleşimini ve grafik ayarlarını değiştirmek için giriş yapın.'
 :'View only: sign in to add signals or change chart panels, layout and settings.';}
}

const STORE_KEY='PROGNODE_HF3_PLUS_CORE_LAYOUT_V1';
const CORE_VERSION='0.7.2-rc6.4.7-hf3-backup';
async function api(path){
 let token='';try{token=sessionStorage.getItem('prognode.accessSession')||''}catch{}
 const resp=await fetch(path,{cache:'no-store',headers:token?{'X-PROGNODE-Session':token}:{}});
 const text=await resp.text();let json;
 try{json=JSON.parse(text)}catch{throw Error(`Core API: HTTP ${resp.status}, non-JSON response at ${path.split('?')[0]}`)}
 if(!resp.ok)throw Error(json?.message||json?.title||`HTTP ${resp.status}`);
 return json;
}
async function loadCatalog(){
 if(catalogPromise)return catalogPromise;
 catalogPromise=(async()=>{
 try{
  const [tags,devices,recordings,definitions]=await Promise.all([
   api('/api/tags'),api('/api/devices'),api('/api/historian/configurations'),
   api('/api/alarms/definitions').catch(error=>{console.warn('Trend alarm context unavailable:',error);return []})
  ]);
  const devicesById=new Map(devices.map(d=>[d.id,d.name]));
  const active=new Map(recordings.filter(r=>r.configuration?.enabled===true).map(r=>[r.configuration.tagId,r]));
  if(signalDefs.length&&active.size===0)throw Error('Historian catalog temporarily empty; keeping open charts');
  const previous=new Set(signalDefs.map(d=>d.id));
  const nextSignalDefs=tags.filter(t=>active.has(t.id)).map((t,i)=>{
   const alarms=definitions.filter(a=>a.tagId===t.id&&a.enabled!==false&&a.threshold!=null);
   const highs=alarms.filter(a=>a.condition==='GreaterThan'||a.condition==='GreaterThanOrEqual').map(a=>Number(a.threshold));
   const lows=alarms.filter(a=>a.condition==='LessThan'||a.condition==='LessThanOrEqual').map(a=>Number(a.threshold));
   const rec=active.get(t.id);
   return {id:t.id,device:devicesById.get(t.deviceId)||'PLC',tag:t.name,eng:t.name,
    unit:t.unit||'',color:palette[i%palette.length],high:highs.length?Math.min(...highs):null,
    low:lows.length?Math.max(...lows):null,base:Number(rec.lastValue)||0,
    interval:Math.max(1,Number(rec.configuration.sampleIntervalSeconds)||10)*1000,
    decimals:Math.min(4,Math.max(0,Number(t.decimalPlaces)||2)),
    digital:String(t.dataType||'').toLowerCase()==='bool'};
  });
  if(signalDefs.length&&!nextSignalDefs.length)throw Error('Historian catalog temporarily empty; keeping open charts');
  signalDefs=nextSignalDefs;
  const allowed=new Set(signalDefs.map(d=>d.id));
  for(const id of [...allData.keys()])if(!allowed.has(id)){allData.delete(id);metaById.delete(id)}
  for(const d of signalDefs)if(!allData.has(d.id))allData.set(d.id,[]);
  S.charts=S.charts.filter(c=>allowed.has(c.id));
  if(!Core.ready)await restoreLayout();
  if(S.activeId&&!allowed.has(S.activeId))S.activeId=S.charts[0]?.id||null;
  Core.ready=true;Core.error='';Core.lastCatalogAt=Date.now();
  const source=byId('hf61-source');if(source)source.textContent=S.lang==='tr'?`${signalDefs.length} HISTORIAN SİNYALİ`:`${signalDefs.length} HISTORIAN SIGNALS`;
  renderLayout();renderDrawer();applyTrendPermissions();
  void consumePendingHistorianTag();
  for(const c of S.charts)void fetchSeries(c,true);
  if(previous.size!==signalDefs.length)toast(S.lang==='tr'?`${signalDefs.length} etkin Historian sinyali hazır`:`${signalDefs.length} enabled Historian signals`);
 }catch(error){Core.error=String(error.message||error);Core.ready=false;renderDrawer();renderLayout();console.warn('PROGNODE Trend Studio catalog:',Core.error)}
 })().finally(()=>{catalogPromise=null});
 return catalogPromise;
}
async function consumePendingHistorianTag(){
 if(pendingHistorianPromise)return pendingHistorianPromise;
 pendingHistorianPromise=consumePendingHistorianTagOnce().finally(()=>{pendingHistorianPromise=null});
 return pendingHistorianPromise;
}
async function consumePendingHistorianTagOnce(){
 let id='';try{id=sessionStorage.getItem('prognode.pendingHistorianTrendTag')||''}catch{}
 if(!id||!Core.ready)return;
 await refreshTrendAuth();
 if(!Core.authenticated){toast(S.lang==='tr'?'Trend grafiği eklemek için giriş yapın.':'Sign in to add a Trend chart.');return}
 try{id=sessionStorage.getItem('prognode.pendingHistorianTrendTag')||''}catch{}
 if(!id)return;
 try{sessionStorage.removeItem('prognode.pendingHistorianTrendTag')}catch{}
 if(!signalDefs.some(d=>d.id===id)){toast(S.lang==='tr'?'Bu Tag için etkin Historian kaydı bulunamadı.':'No enabled Historian recording for this Tag.');return}
 S.chartFilter='';byId('trend-open-filter').value='';S.maximized=null;
 if(S.charts.some(c=>c.id===id)){S.activeId=id;renderLayout();return}
 appendSignals([id]);
}
async function restoreLayout(){
 let saved=null;try{saved=JSON.parse(localStorage.getItem(STORE_KEY)||'null')}catch{}
 if(!saved){try{saved=await api('/api/backup/layout');if(saved?.charts)localStorage.setItem(STORE_KEY,JSON.stringify(saved))}catch{}}
 if(saved&&Array.isArray(saved.charts)){
  S.charts=saved.charts.filter(x=>signalDefs.some(d=>d.id===x.id)).slice(0,MAX_CHARTS).map(x=>{
   const c=makeChart(x.id);c.slot=Number.isInteger(x.slot)&&x.slot>=0&&x.slot<MAX_CHARTS?x.slot:null;c.windowMin=[5,15,60,480,1440,10080,43200,525600].includes(x.windowMin)?x.windowMin:15;
   c.range=[S.now-c.windowMin*60000,S.now];
   for(const k of ['flags','threshold','points','batch','smooth','wide'])if(typeof x[k]==='boolean')c[k]=x[k];
   c.chartType=['line','smooth','area'].includes(x.chartType)?x.chartType:(c.smooth?'smooth':'line');
   return c;
  });
  S.leftPercent=Math.min(70,Math.max(30,Number(saved.leftPercent)||50));
  S.linkedCursor=!!saved.linkedCursor;S.savedAt=saved.savedAt||null;S.layout=['auto','rows','columns','three','four','five'].includes(saved.layout)?saved.layout:'auto';
 }
 S.activeId=S.charts[0]?.id||null;
}
async function fetchContext(){
 const [a,b]=await Promise.allSettled([api('/api/alarms/history?limit=500'),api('/api/batches?limit=100')]);
 if(a.status==='fulfilled')alarmEvents=Array.isArray(a.value)?a.value:[];
 if(b.status==='fulfilled')batches=Array.isArray(b.value)?b.value:[];
 for(const c of S.charts)if(coreVisible)drawChart(c);
 renderProcessEvents();
}
async function refreshFullscreenAlarmIndicator(){
 if(!coreVisible||document.hidden)return;
 try{
  const active=await api('/api/alarms/active');
  activeAlarmCount=Array.isArray(active)?active.length:0;
  const indicator=byId('fullscreen-alarm-indicator');
  indicator.hidden=activeAlarmCount===0;
  byId('fullscreen-alarm-count').textContent=String(activeAlarmCount);
  indicator.setAttribute('aria-label',S.lang==='tr'?`${activeAlarmCount} aktif alarm; Alarm sayfasını aç`:`${activeAlarmCount} active alarms; open Alarms`);
 }catch(error){console.warn('Trend active alarms:',error)}
}
byId('fullscreen-alarm-indicator').addEventListener('click',()=>{
 if(window.parent!==window&&typeof window.parent.navigate==='function'){
  if(document.fullscreenElement)void document.exitFullscreen();
  window.parent.navigate('alarms');
 }else{window.location.href='/?page=alarms'}
});
async function fetchSeries(c,force=false){
 if(!coreVisible||document.hidden||!Core.ready||!signalDefs.some(d=>d.id===c.id)||c.loading)return;
 const now=Date.now(),age=now-(c.lastFetch||0);
 const pollMs=c.windowMin>=10080?60000:c.windowMin>=1440?30000:10000;
 if(!force&&!c.needsFetch&&age<pollMs)return;
 const [from,to]=c.range,request=++c.requestId;
 if(!(from<to)||to-from>366*86400000)return;
 c.loading=true;c.needsFetch=false;
 try{
  const query=new URLSearchParams({tagIds:c.id,from:new Date(from).toISOString(),to:new Date(to).toISOString(),maxPoints:String(c.windowMin>=10080?1400:c.windowMin>=1440?1800:2400)});
  const result=await api('/api/trend-studio/series?'+query);
  if(request!==c.requestId||c.range[0]!==from||c.range[1]!==to){c.needsFetch=true;return;}
  const s=(result.series||[]).find(x=>x.tagId===c.id);
  if(!s)throw Error('Requested series missing');
  const points=(s.points||[]).map(p=>({t:Date.parse(p.timestamp),v:p.value===null?null:Number(p.value),quality:String(p.quality||'BAD').toUpperCase(),intervalMs:signalDefs.find(d=>d.id===c.id)?.interval||10000})).filter(p=>Number.isFinite(p.t)).sort((a,b)=>a.t-b.t);
  const previous=allData.get(c.id)||[];
  if(!points.length&&previous.some(p=>p.t>=from&&p.t<=to)){
   c.lastError='Historian returned an empty result for previously recorded timestamps';
   c.lastFetch=Date.now();drawChart(c);return;
  }
  allData.set(c.id,points);
  metaById.set(c.id,{sampleCount:Number(s.sampleCount)||0,minimum:s.minimum,maximum:s.maximum,average:s.average,summarized:Number(s.sampleCount)>points.length});
  c.lastFetch=Date.now();c.fetchedRange=[from,to];c.lastError=null;
  const d=signalDefs.find(d=>d.id===c.id);if(d&&points.length){const good=points.filter(p=>p.quality==='GOOD'&&Number.isFinite(p.v)).at(-1);if(good)d.base=good.v}
  drawChart(c);
 }catch(error){c.lastError=String(error.message||error);c.lastFetch=Date.now();console.warn('Historian query:',c.lastError);drawChart(c)}
 finally{c.loading=false;if(c.needsFetch)void fetchSeries(c,true)}
}
function refreshPanel(c){c.needsFetch=true;if(coreVisible)void fetchSeries(c,true)}
function coreVisibleChanged(visible){coreVisible=visible;if(visible){S.now=Date.now();syncAppearance();void refreshTrendAuth();void fetchContext();void refreshFullscreenAlarmIndicator();
 // Switching back from Historian must always fetch the fresh configurations.
 // The old "Core.ready" check left newly recorded tags invisible until restart.
 void loadCatalog();for(const c of S.charts)void fetchSeries(c,true)}}
function syncAppearance(){
 try{const root=parent.document.documentElement;
   const theme=root.dataset.theme||(localStorage.getItem('prognode.theme')||'dark');
   if(theme!==S.theme){S.theme=theme;document.documentElement.dataset.theme=theme;renderLayout()}
   if(S.lang!=='en')setLang('en');
 }catch{}
}
window.addEventListener('message',e=>{if(e.origin!==location.origin||e.source!==window.parent)return;
 if(e.data?.type==='pgn:trend-visible')coreVisibleChanged(!!e.data.visible);
 if(e.data?.type==='pgn:historian-changed')void loadCatalog();
 if(e.data?.type==='pgn:open-historian-tag'&&typeof e.data.tagId==='string'){
  try{sessionStorage.setItem('prognode.pendingHistorianTrendTag',e.data.tagId)}catch{}
  if(Core.ready)void consumePendingHistorianTag();else void loadCatalog();
 }
 if(e.data?.type==='pgn:access-changed')void refreshTrendAuth();
});
const S={lang:'tr',theme:'dark',windowMin:15,live:true,compare:false,maximized:null,drawer:false,selected:new Set(),compareIds:new Set(),charts:[],help:false,now:Date.now(),overlay:false,activeId:null,linkedCursor:false,linkedTime:null,leftPercent:50,savedAt:null,layout:'auto',chartFilter:''};
const tx=k=>T[S.lang][k]||k;const esc=s=>String(s).replace(/[&<>"']/g,c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
function makeChart(id){return {id,windowMin:15,range:[S.now-15*60000,S.now],history:[],flags:true,threshold:true,points:true,batch:false,smooth:true,chartType:'smooth',freeze:false,settings:false,hover:null,wide:false,renderStats:null,requestId:0,lastFetch:0,loading:false,needsFetch:true,lastError:null,fetchedRange:null}}

const name=d=>`${d.device} · ${d.tag}`;
const val=(v,d)=>Number(v).toFixed(d.decimals??2);
function fmtTime(t,full=false){if(!Number.isFinite(t))return '–';return new Date(t).toLocaleTimeString(S.lang==='tr'?'tr-TR':'en-GB',full?{hour:'2-digit',minute:'2-digit',second:'2-digit'}:{hour:'2-digit',minute:'2-digit'})}
function fmtTick(t,span){if(span>=120*86400000)return new Date(t).toLocaleDateString(S.lang==='tr'?'tr-TR':'en-GB',{month:'short',year:'numeric'});if(span>=3*86400000)return new Date(t).toLocaleDateString(S.lang==='tr'?'tr-TR':'en-GB',{day:'2-digit',month:'short'});if(span>=86400000)return new Date(t).toLocaleDateString(S.lang==='tr'?'tr-TR':'en-GB',{day:'2-digit',month:'short'})+' '+fmtTime(t);return fmtTime(t)}
function setLang(){S.lang='en';document.documentElement.lang='en';document.querySelectorAll('[data-i18n]').forEach(e=>e.textContent=tx(e.dataset.i18n));document.querySelectorAll('[data-ph]').forEach(e=>e.placeholder=tx(e.dataset.ph));document.querySelectorAll('[data-lang]').forEach(e=>e.classList.toggle('active',e.dataset.lang==='en'));renderLayout();renderDrawer();renderProcessEvents();}
function toast(message){const el=byId('toast');el.textContent=message;el.classList.remove('hidden');clearTimeout(toast.t);toast.t=setTimeout(()=>el.classList.add('hidden'),2700)}


function hf61Translate(){
 document.querySelectorAll('[data-hf61-tr]').forEach(el=>{el.textContent=el.getAttribute('data-hf61-'+S.lang)||el.textContent});
 document.querySelectorAll('[data-hf61-placeholder-tr]').forEach(el=>{el.placeholder=el.getAttribute('data-hf61-placeholder-'+S.lang)||el.placeholder});
 const e=byId('hf62-layout-select');if(e)e.value=S.layout;const more=byId('hf62-more-toggle');if(more)more.textContent=byId('hf62-advanced')?.hidden?(S.lang==='tr'?'Diğer araçlar ↓':'More tools ↓'):(S.lang==='tr'?'Diğer araçları kapat ↑':'Close tools ↑');
}
function renderExplorer(){
 const el=byId('hf61-explorer-list');if(!el)return;
 const filter=(byId('hf61-explorer-search')?.value||'').toLocaleLowerCase(S.lang==='tr'?'tr-TR':'en-US');
 const filtered=signalDefs.filter(d=>(d.device+' '+d.tag+' '+d.unit).toLocaleLowerCase(S.lang==='tr'?'tr-TR':'en-US').includes(filter));
 if(!filtered.length){el.innerHTML='<p class="hf61-muted">'+(Core.error?esc(Core.error):signalDefs.length?(S.lang==='tr'?'Sinyal bulunamadı.':'No matching signals.'):(S.lang==='tr'?'Henüz etkin Historian sinyali yok.':'No enabled Historian signals yet.'))+'</p>';return;}
 el.innerHTML=filtered.map(d=>{const selected=S.charts.some(c=>c.id===d.id);return `<button type="button" class="hf61-signal ${selected?'is-selected':''}" data-signal-id="${esc(d.id)}" draggable="true" title="${S.lang==='tr'?'Bu sinyali seçilen grafik hücresine sürükleyin':'Drag to a chart slot'}" aria-pressed="${selected?'true':'false'}"><span class="hf61-series-dot" style="--hf61-series:${d.color}"></span><span class="hf61-signal-copy"><strong>${esc(d.tag)}</strong><small>${esc(d.device)}${d.unit?' · '+esc(d.unit):''}</small></span><span class="hf61-signal-state" aria-hidden="true">${selected?'✓':'+'}</span></button>`}).join('');
}
function renderProcessEvents(){
 const tbody=byId('hf61-events-body');if(!tbody)return;
 const events=[];
 for(const a of alarmEvents){const t=Date.parse(a.activeAt||a.activatedAt||'');if(!Number.isFinite(t))continue;const d=signalDefs.find(d=>d.id===a.tagId);events.push({t,text:String(a.text||'Alarm'),source:d?.tag||a.sourceName||'—',state:String(a.state||'ACTIVE')});}
 for(const b of batches){const t=Date.parse(b.startedAt||'');if(!Number.isFinite(t))continue;events.push({t,text:(S.lang==='tr'?'Parti başladı: ':'Batch started: ')+String(b.batchNo||'—'),source:b.recipeName||b.batchNo||'—',state:String(b.state===0?'RUNNING':b.state===1?'COMPLETED':b.state===2?'ABORTED':b.state||'RECORDED')});}
 events.sort((a,b)=>b.t-a.t);
 const latest=events.slice(0,12);
 if(!latest.length){tbody.innerHTML=`<tr><td colspan="4" class="hf61-events-empty">${Core.error?esc(Core.error):S.lang==='tr'?'Henüz kaydedilmiş proses olayı bulunmuyor.':'No recorded process events yet.'}</td></tr>`;return;}
 tbody.innerHTML=latest.map(e=>`<tr><td>${esc(fmtTime(e.t,true))}</td><td>${esc(e.text)}</td><td>${esc(e.source)}</td><td><span class="hf61-event-status">${esc(e.state)}</span></td></tr>`).join('');
}

function effectiveColumns(){
 const width=byId('grid')?.getBoundingClientRect().width||byId('grid')?.parentElement?.clientWidth||850;
 if(width<660)return 1;
 if(S.layout==='rows')return 1;
 if(S.layout==='columns')return 2;
 if(S.layout==='three')return 3;
 if(S.layout==='four')return 4;
 if(S.layout==='five')return 5;
 // Automatic selection is viewport-aware and needs no user configuration.
 const n=filteredCharts().length;
 return n>=13&&width>=1500?5:n>=9&&width>=1350?4:n>=5&&width>=1100?3:n>=2&&width>=690?2:1;
}
function filteredCharts(){const q=String(S.chartFilter||'').trim().toLocaleLowerCase(S.lang==='tr'?'tr-TR':'en-US');if(!q)return S.charts;return S.charts.filter(c=>{const d=signalDefs.find(x=>x.id===c.id);return d&&(d.device+' '+d.tag+' '+d.unit).toLocaleLowerCase(S.lang==='tr'?'tr-TR':'en-US').includes(q)})}
function normalizeSlots(){
 const used=new Set();
 for(const c of S.charts){if(!Number.isInteger(c.slot)||c.slot<0||c.slot>=MAX_CHARTS||used.has(c.slot))c.slot=null;else used.add(c.slot)}
 for(const c of S.charts)if(c.slot===null){let slot=0;while(used.has(slot)&&slot<MAX_CHARTS)slot++;c.slot=Math.min(slot,MAX_CHARTS-1);used.add(c.slot)}
}
function slotItems(){
 const charts=filteredCharts();
 if(S.layout==='auto'||S.chartFilter)return charts;
 normalizeSlots();const cols=effectiveColumns();
 const maxSlot=Math.max(-1,...charts.map(c=>c.slot));
 const capacity=Math.min(MAX_CHARTS,Math.max(maxSlot+1,0));
 const used=new Map(S.charts.map(c=>[c.slot,c]));
 return Array.from({length:capacity},(_,i)=>used.get(i)).filter(Boolean);
}
function resizeLayout(){const grid=byId('grid'),n=filteredCharts().length;
 grid.dataset.n=Math.min(n,MAX_CHARTS);grid.classList.toggle('maximized',!!S.maximized);byId('board-count').textContent=S.chartFilter?`${n}/${S.charts.length}`:n;
 byId('available-signal-count').textContent=String(signalDefs.length);
 const cols=effectiveColumns(),rows=Math.ceil(n/cols);
 grid.dataset.layout=S.layout;grid.style.setProperty('--hf62-cols',String(cols));
 grid.style.gridTemplateColumns=S.maximized?'minmax(0,1fr)':cols===1?'minmax(0,1fr)':cols===2?`${S.leftPercent}fr ${100-S.leftPercent}fr`:`repeat(${cols},minmax(0,1fr))`;
 const chartsOnly=document.documentElement.classList.contains('charts-only');
 const compactAfterRemoval=!chartsOnly&&!S.maximized&&n<=1;
 grid.style.gridAutoRows=compactAfterRemoval?'max-content':!S.maximized&&rows>3?'minmax(220px,1fr)':'minmax(0,1fr)';
 grid.style.alignContent=compactAfterRemoval?'start':'';
 grid.style.overflowY=compactAfterRemoval||!S.maximized&&rows>3?'auto':'hidden';
 hf61Translate();
 const width=byId('col-width');if(width){width.value=S.leftPercent;width.disabled=cols!==2||!!S.maximized;byId('col-width-value').textContent=S.leftPercent+'%';}
 const label=S.layout==='auto'?(S.lang==='tr'?'Otomatik':'Auto'):S.layout==='rows'?(S.lang==='tr'?'Alt alta':'Stacked'):S.layout==='three'?(S.lang==='tr'?'3 sütun':'3 columns'):S.layout==='four'?(S.lang==='tr'?'4 sütun':'4 columns'):S.layout==='five'?(S.lang==='tr'?'5 sütun':'5 columns'):(S.lang==='tr'?'2 sütun':'2 columns');
 byId('layout-tag').textContent=S.maximized?(S.lang==='tr'?'Büyütülmüş grafik':'Maximized chart'):`${label} · ${cols} × ${rows}`;
 byId('active-chart').innerHTML=S.activeId?`<strong>${esc(name(signalDefs.find(d=>d.id===S.activeId)||signalDefs[0]))}</strong>`:'';
 document.querySelectorAll('[data-window]').forEach(b=>b.classList.toggle('active',+b.dataset.window===(S.charts.find(c=>c.id===S.activeId)?.windowMin||15)));
 const hint=byId('saved-hint');if(hint)hint.textContent=S.savedAt?'● '+(S.lang==='tr'?'Kaydedildi':'Saved'):'';
}

function stateFor(d,v){return !Number.isFinite(v)?'unknown':d.high!==null&&v>=d.high?'high':d.low!==null&&v<=d.low?'low':'normal'}

function renderLayout(){const grid=byId('grid');grid.innerHTML='';resizeLayout();
 if(!S.charts.length&&!Core.ready){renderExplorer();renderProcessEvents();grid.innerHTML=`<div class="empty"><strong>${Core.error?esc(Core.error):tx('emptyTitle')}</strong><div>${Core.ready?tx('emptyDesc'):(S.lang==='tr'?'Historian bağlantısı bekleniyor':'Waiting for Historian')}</div><button class="btn primary" id="emptyAdd">${tx('add')}</button></div>`;byId('emptyAdd').onclick=()=>openDrawer();return}
 grid.classList.toggle('compare-mode',S.compare);byId('compare-actions').classList.toggle('hidden',!S.compare);
 byId('compare').classList.toggle('active',S.compare);byId('compare-count').textContent=`${S.compareIds.size} ${tx('selected')}`;
 byId('compare-open').disabled=S.compareIds.size<2;byId('live').textContent=S.live?'● LIVE Ⅱ':'▷ PAUSED';byId('live').classList.toggle('live',S.live);
 byId('sync-cursor').checked=S.linkedCursor;
 const visibleCharts=filteredCharts();
 if(S.charts.length&&S.chartFilter&&!visibleCharts.length){grid.innerHTML=`<div class="empty"><strong>${S.lang==='tr'?'Filtreye uyan açık trend yok':'No open trends match the filter'}</strong><div>${S.lang==='tr'?'Filtreyi temizleyerek tüm trendleri gösterin.':'Clear the filter to show every open trend.'}</div></div>`;return}
 for(const c of (S.maximized?visibleCharts:slotItems())){
  const d=signalDefs.find(s=>s.id===c.id),all=allData.get(c.id)||[],last=all.filter(p=>p.quality==='GOOD'&&Number.isFinite(p.v)).at(-1),st=stateFor(d,last?.v),label=name(d);const el=document.createElement('section');
 el.className='chart'+(S.maximized===c.id?' is-max':'')+(S.activeId===c.id?' is-active':'')+(c.wide&&!S.maximized&&S.layout==='auto'?' wide':'');el.dataset.chart=c.id;el.style.setProperty('--series',d.color);
 if(S.layout!=='auto'&&!S.chartFilter&&!S.maximized){const cols=effectiveColumns(),position=S.charts.filter(x=>x.slot<c.slot).length;el.style.gridColumnStart=String(position%cols+1);el.style.gridRowStart=String(Math.floor(position/cols)+1)}
 el.innerHTML=`<div class="chart-head"><button class="move-handle" data-action="move" draggable="true" title="${tx('reorder')}" aria-label="${tx('reorder')}">⠿</button><span class="seriesdot"></span><input type="checkbox" class="compare-check" data-action="compare-check" ${S.compareIds.has(c.id)?'checked':''} title="${tx('compare')}"><span class="chart-title" data-action="focus" title="${S.lang==='tr'?'Bu grafiği seç':'Select this chart'}">${esc(label)}</span><span class="value" id="v-${c.id}">${last?val(last.v,d):'—'}</span><span class="unit">${esc(d.unit)}</span><span class="state ${st}" id="s-${c.id}">${tx(st)}</span><span class="chart-ctrl"><span class="hf61-range-pills">${[[15,'15m'],[60,'1h'],[1440,'24h'],[10080,'7d']].map(([n,l])=>`<button class="hf61-range ${c.windowMin===n?'selected':''}" data-chart-window-btn="${n}" type="button">${l}</button>`).join('')}</span><select class="chart-range" data-chart-window="${c.id}" aria-label="${esc(label)} time range">${[[5,'5m'],[15,'15m'],[60,'1h'],[480,'8h'],[1440,'24h'],[10080,'7d'],[43200,'30d'],[525600,'1y']].map(([n,l])=>`<option value="${n}" ${c.windowMin===n?'selected':''}>${l}</option>`).join('')}</select><button class="chart-hold ${c.freeze?'paused':''}" data-action="live-chart" title="${S.lang==='tr'?'Yalnızca bu grafik için canlı/duraklat':'Live/pause this chart'}">${c.freeze?'▶ LIVE':'Ⅱ PAUSE'}</button><button class="small-action" data-action="maximize" title="${S.maximized===c.id?tx('restore'):tx('maximize')}">${S.maximized===c.id?'▣':'⛶'}</button><button class="small-action" data-action="settings" title="${tx('settingsChart')}">⚙</button><button class="small-action" data-action="remove" title="${tx('remove')}">×</button></span></div><div class="plot-frame"><svg class="chart-svg" data-id="${c.id}" aria-label="${esc(label)} chart"></svg><div class="tooltip"></div></div><div class="chart-foot"><span id="meta-${c.id}"></span><span class="quality-chip" id="quality-${c.id}"></span><span class="zoom-depth" id="zoom-${c.id}"></span></div>`;
 el.querySelector('[data-action="maximize"]').textContent=S.maximized===c.id?'Restore':'Expand';
 el.querySelector('[data-action="settings"]').textContent='Settings';
 el.querySelector('[data-action="remove"]').textContent='Remove';
 el.querySelector('.chart-range').title='Time range';
 grid.appendChild(el);if(c.settings)openSettings(el,c);drawChart(c);
 }
 renderExplorer();renderProcessEvents();
}
function openSettings(el,c){const prior=el.querySelector('.settings');if(prior)prior.remove();const panel=document.createElement('div');panel.className='settings';const choices=[['flags',tx('flags')],['threshold',tx('threshold')],['points',tx('points')],['batch',tx('batch')],['freeze',tx('freeze')],['wide',tx('wide')]];
 const types=[['line',S.lang==='tr'?'Düz çizgi':'Straight line'],['smooth',tx('smooth')],['area',S.lang==='tr'?'Altı dolgulu çizgi':'Shaded area']];
 panel.innerHTML=`<div class="settings-head"><strong>${tx('settingsChart')} · ${esc(name(signalDefs.find(d=>d.id===c.id)))}</strong><button type="button" data-action="close-settings" aria-label="${S.lang==='tr'?'Ayarları kapat':'Close settings'}" title="${S.lang==='tr'?'Kapat':'Close'}">×</button></div><label class="setting">${S.lang==='tr'?'Grafik tipi':'Chart type'} <select data-chart-type="${esc(c.id)}" aria-label="${S.lang==='tr'?'Grafik tipi':'Chart type'}">${types.map(([value,label])=>`<option value="${value}" ${c.chartType===value?'selected':''}>${label}</option>`).join('')}</select></label>${choices.map(([k,label])=>`<label class="setting"><input type="checkbox" data-setting="${k}" ${c[k]?'checked':''}><span>${label}</span></label>`).join('')}<hr><div class="setting-tools"><button data-action="undo">↩ ${S.lang==='tr'?'Önceki zoom':'Previous zoom'}</button><button data-action="reset-chart">${tx('resetChart')}</button></div>`;
 el.appendChild(panel);
 const close=panel.querySelector('[data-action="close-settings"]');
 close.textContent='Close';
 close.setAttribute('aria-label','Close chart settings');
 const rect=el.getBoundingClientRect();
 panel.style.position='fixed';
 panel.style.right='auto';
 const panelHeight=Number(panel.offsetHeight)||360,panelWidth=Number(panel.offsetWidth)||280;
 panel.style.top=`${Math.max(12,Math.min(rect.top+44,window.innerHeight-panelHeight-12))}px`;
 panel.style.left=`${Math.max(12,Math.min(rect.right-panelWidth-12,window.innerWidth-panelWidth-12))}px`;
}

const P={l:42,r:12,t:16,b:30};
function dims(svg){const rr=svg.getBoundingClientRect();return {W:Math.max(260,rr.width),H:Math.max(150,rr.height),pw:Math.max(180,rr.width-P.l-P.r),ph:Math.max(100,rr.height-P.t-P.b)}}

function visible(c){const a=allData.get(c.id)||[],left=lowerBound(a,c.range[0]),right=upperBound(a,c.range[1]);return a.slice(left,right);}
function lowerBound(a,t){let l=0,r=a.length;while(l<r){const m=(l+r)>>1;if(a[m].t<t)l=m+1;else r=m;}return l;}
function upperBound(a,t){let l=0,r=a.length;while(l<r){const m=(l+r)>>1;if(a[m].t<=t)l=m+1;else r=m;}return l;}
function nearestValid(c,t){if(t<c.range[0]||t>c.range[1])return null;
 const a=allData.get(c.id)||[],pos=lowerBound(a,t),d=signalDefs.find(s=>s.id===c.id);let best=null,dist=Infinity;
 for(let i=Math.max(0,pos-5);i<Math.min(a.length,pos+5);i++){let p=a[i];if(p.quality!=='GOOD'||p.t<c.range[0]||p.t>c.range[1])continue;let delta=Math.abs(p.t-t);if(delta<dist){dist=delta;best=p}}
 // Do not invent values in BAD/STALE gaps; linked cursors require a genuinely nearby recording.
 return best&&dist<=Math.max(d.interval,best.intervalMs||d.interval)*1.15?best:null;
}

function capRange(c){const all=allData.get(c.id)||[],left=all[0]?.t??S.now-c.windowMin*60000,right=Math.max(S.now,all[all.length-1]?.t??S.now);if(c.range[1]<=c.range[0])c.range=[Math.max(left,right-60000),right];}
function computeY(c,points,d){let mm=points.filter(p=>p.quality==='GOOD').map(p=>p.v);if(c.threshold)mm=mm.concat([d.high,d.low].filter(Number.isFinite));if(!mm.length)mm=[d.base-.5,d.base+.5];let low=Math.min(...mm),high=Math.max(...mm),span=Math.max(1e-6,high-low);let pad=Math.max(span*.13,(Number.isFinite(d.amp)?d.amp*.07:0),.15);return [low-pad,high+pad]}
function monotonePath(points,xScale,yScale){if(points.length<2)return '';const X=points.map(p=>xScale(p.t)),Y=points.map(p=>yScale(p.v)),dx=[],m=[];for(let i=0;i<X.length-1;i++)dx.push(Math.max(.0001,X[i+1]-X[i]));for(let i=0;i<X.length-1;i++)m.push((Y[i+1]-Y[i])/dx[i]);const tangent=[];tangent[0]=m[0];tangent[points.length-1]=m[m.length-1];for(let i=1;i<points.length-1;i++){if(m[i-1]*m[i]<=0){tangent[i]=0;continue}let w1=2*dx[i]+dx[i-1],w2=dx[i]+2*dx[i-1];tangent[i]=(w1+w2)/(w1/m[i-1]+w2/m[i])}let path=`M ${X[0].toFixed(2)} ${Y[0].toFixed(2)}`;for(let i=0;i<X.length-1;i++){let h=dx[i];path+=` C ${(X[i]+h/3).toFixed(2)} ${(Y[i]+tangent[i]*h/3).toFixed(2)},${(X[i+1]-h/3).toFixed(2)} ${(Y[i+1]-tangent[i+1]*h/3).toFixed(2)},${X[i+1].toFixed(2)} ${Y[i+1].toFixed(2)}`;}return path}
function seriesSegments(points,intervalMs,summarized=false){let seg=[],all=[];for(const p of points){if(p.quality==='GOOD'){
 if(!summarized&&seg.length&&p.t-seg.at(-1).t>intervalMs*1.7){all.push(seg);seg=[]}
 seg.push(p)}else if(seg.length){all.push(seg);seg=[]}}if(seg.length)all.push(seg);return all}

function decimateSegment(segment,limit,range){if(segment.length<=limit)return segment;
 // Allocate horizontal buckets by elapsed time, not by sample count: dense recent records must not dominate yearly views.
 const out=[],bucketWidth=(range[1]-range[0])/Math.max(16,Math.floor(limit/4));let bucket=[],current=-Infinity;
 function flush(){if(!bucket.length)return;let min=bucket[0],max=bucket[0];for(const p of bucket){if(p.v<min.v)min=p;if(p.v>max.v)max=p;}
  for(const p of [...new Set([bucket[0],min,max,bucket.at(-1)])].sort((a,b)=>a.t-b.t))if(!out.length||p.t>out.at(-1).t)out.push(p);
  bucket=[];
 }
 for(const p of segment){const k=Math.floor((p.t-range[0])/bucketWidth);if(k!==current){flush();current=k;}bucket.push(p)}flush();return out;
}
function qualityWindows(points,intervalMs,summarized=false){let windows=[],current=null,lastGood=null;for(const p of points){if(p.quality==='GOOD'&&lastGood&&!summarized&&p.t-lastGood.t>intervalMs*1.7)
 windows.push({start:lastGood.t+intervalMs*.5,last:p.t-intervalMs*.5,type:'NO DATA'});
 if(p.quality!=='GOOD'){
 if(current&&current.type===p.quality)current.last=p.t;else{if(current)windows.push(current);current={start:p.t,last:p.t,type:p.quality}};
 }else if(current){windows.push(current);current=null}
 if(p.quality==='GOOD')lastGood=p;else lastGood=null}
 if(current)windows.push(current);return windows;}
const pendingDraws=new Map();let drawFrame=0;
function drawChart(c){if(!coreVisible||document.hidden)return;pendingDraws.set(c.id,c);if(drawFrame)return;drawFrame=requestAnimationFrame(()=>{drawFrame=0;const queue=[...pendingDraws.values()];pendingDraws.clear();for(const chart of queue)drawChartNow(chart)})}
function trendStrokePath(c,ds,x,y){return c.chartType==='line'?`M ${ds.map(p=>`${x(p.t).toFixed(2)} ${y(p.v).toFixed(2)}`).join(' L ')}`:monotonePath(ds,x,y)}
function trendAreaPath(c,ds,x,y,bottom){if(c.chartType!=='area'||ds.length<2)return null;const first=ds[0],last=ds.at(-1);return `${trendStrokePath(c,ds,x,y)} L ${x(last.t).toFixed(2)} ${bottom.toFixed(2)} L ${x(first.t).toFixed(2)} ${bottom.toFixed(2)} Z`}
function drawChartNow(c){const el=byId('grid').querySelector(`[data-chart="${c.id}"]`);if(!el)return;const svg=el.querySelector('svg');const d=signalDefs.find(s=>s.id===c.id),points=visible(c),di=dims(svg);
 const headerGood=points.filter(p=>p.quality==='GOOD'&&Number.isFinite(p.v)).at(-1);
 const valueEl=byId('v-'+c.id),statusEl=byId('s-'+c.id);
 if(valueEl)valueEl.textContent=headerGood?val(headerGood.v,d):'—';
 if(statusEl){const state=stateFor(d,headerGood?.v);statusEl.className='state '+state;statusEl.textContent=tx(state)}
 svg.setAttribute('viewBox',`0 0 ${di.W} ${di.H}`);svg.setAttribute('preserveAspectRatio','none');const [yMin,yMax]=computeY(c,points,d);
 const x=t=>P.l+(t-c.range[0])/(c.range[1]-c.range[0])*di.pw,y=v=>P.t+(yMax-v)/(yMax-yMin)*di.ph,plotBottom=P.t+di.ph,plotRight=P.l+di.pw;
 let out=`<defs><clipPath id="cl-${c.id}"><rect x="${P.l}" y="${P.t}" width="${di.pw}" height="${di.ph}"/></clipPath><pattern id="gap-${c.id}" patternUnits="userSpaceOnUse" width="9" height="9" patternTransform="rotate(40)"><line x1="0" x2="0" y1="0" y2="9" stroke="var(--amber)" stroke-width="2" opacity=".22"/></pattern></defs>`;
 for(let i=0;i<=4;i++){let yy=P.t+di.ph*i/4,v=yMax-(yMax-yMin)*i/4;out+=`<line x1="${P.l}" x2="${plotRight}" y1="${yy}" y2="${yy}" stroke="var(--grid)" stroke-width="1"/><text x="${P.l-7}" y="${yy+3}" font-size="10" text-anchor="end" fill="var(--muted)">${Number(v.toFixed(1))}</text>`}
 for(let i=0;i<=4;i++){const xx=P.l+di.pw*i/4,t=c.range[0]+(c.range[1]-c.range[0])*i/4;out+=`<line x1="${xx}" x2="${xx}" y1="${P.t}" y2="${plotBottom}" stroke="var(--grid)" stroke-width="1"/><text x="${xx}" y="${di.H-9}" font-size="10" text-anchor="middle" fill="var(--muted)">${fmtTick(t,c.range[1]-c.range[0])}</text>`}
 if(c.batch){for(const batch of batches){const a=Date.parse(batch.startedAt||''),b=Date.parse(batch.endedAt||'')||S.now;if(!Number.isFinite(a)||b<c.range[0]||a>c.range[1])continue;
 const x0=Math.max(P.l,x(a)),x1=Math.min(plotRight,x(b));out+=`<rect x="${x0}" y="${P.t}" width="${Math.max(0,x1-x0)}" height="${di.ph}" fill="var(--cyan)" opacity=".055"/><text x="${Math.min(x0+4,plotRight-90)}" y="${P.t+12}" font-size="9" fill="var(--cyan)">${esc(batch.batchNo||batch.lotNo||'BATCH')}</text>`;
 }}
 if(c.threshold)for(const [level,color,label] of [[d.high,'var(--red)','HIGH'],[d.low,'var(--amber)','LOW']].filter(([v])=>Number.isFinite(v))){let yy=y(level);out+=`<line x1="${P.l}" x2="${plotRight}" y1="${yy}" y2="${yy}" stroke="${color}" stroke-width="1.15" stroke-dasharray="6 4"/><text x="${plotRight-3}" y="${yy-3}" text-anchor="end" font-size="10" fill="${color}">${label} ${level}</text>`}
 const summarized=!!metaById.get(c.id)?.summarized;
 const gaps=qualityWindows(points,d.interval,summarized);for(const win of gaps){let x0=Math.max(P.l,x(win.start-d.interval*.48)),x1=Math.min(plotRight,x(win.last+d.interval*.48));out+=`<rect x="${x0}" y="${P.t}" width="${Math.max(2,x1-x0)}" height="${di.ph}" fill="url(#gap-${c.id})" stroke="var(--amber)" stroke-dasharray="3 3" opacity=".85"/><text x="${Math.max(P.l+5,x0+5)}" y="${P.t+13}" fill="var(--amber)" font-size="10" font-weight="800">${x1-x0>20?win.type:''}</text>`}
 const segments=seriesSegments(points,d.interval,summarized),displayLimit=Math.max(160,Math.round(di.pw*1.45));let plotted=0;
 out+=`<g clip-path="url(#cl-${c.id})">`;
 for(const seg of segments){const ds=decimateSegment(seg,Math.max(40,Math.round(displayLimit*seg.length/Math.max(1,points.length))),c.range);plotted+=ds.length;if(ds.length===1){out+=`<circle cx="${x(ds[0].t)}" cy="${y(ds[0].v)}" r="2.5" fill="${d.color}"/>`;continue}
 const path=trendStrokePath(c,ds,x,y),area=trendAreaPath(c,ds,x,y,plotBottom);
 if(area)out+=`<path d="${area}" fill="${d.color}" fill-opacity=".16" stroke="none"/>`;
 out+=`<path d="${path}" fill="none" stroke="${d.color}" stroke-width="2.0" stroke-linecap="round" stroke-linejoin="round"/>`;
 for(let i=0;i<ds.length-1;i++){const a=ds[i],b=ds[i+1],lvl=Number.isFinite(d.high)&&a.v>d.high&&b.v>d.high?'var(--red)':Number.isFinite(d.low)&&a.v<d.low&&b.v<d.low?'var(--amber)':null;if(lvl)out+=`<line x1="${x(a.t)}" y1="${y(a.v)}" x2="${x(b.t)}" y2="${y(b.v)}" stroke="${lvl}" stroke-width="2.3"/>`}
 if(c.points&&(c.range[1]-c.range[0])<48*3600000){const markerPoints=ds.length>400?ds.filter((_,i)=>i%Math.ceil(ds.length/350)===0):ds;for(const p of markerPoints)out+=`<circle cx="${x(p.t)}" cy="${y(p.v)}" r="${ds.length>140?1.5:2.4}" fill="${d.color}" stroke="var(--panel)" stroke-width=".55"/>`}
 }
 out+='</g>';
 if(c.flags){for(const ev of alarmEvents.filter(e=>e.tagId===c.id)){
 const at=Date.parse(ev.activeAt||ev.activatedAt||'');if(!Number.isFinite(at)||at<c.range[0]||at>c.range[1])continue;
 out+=`<path class="event-flag" data-event="${esc(ev.occurrenceId||ev.alarmKey)}" d="M${x(at)-4} ${P.t-3} l8 0 -4 7 z" fill="var(--red)"/>`;
 }}
 out+=`<g class="crosshair" pointer-events="none"></g><rect class="plot-hit" x="${P.l}" y="${P.t}" width="${di.pw}" height="${di.ph}" fill="transparent" style="cursor:crosshair"/><rect class="selection" x="0" y="${P.t}" width="0" height="${di.ph}" fill="var(--cyan)" opacity=".18" stroke="var(--cyan)" stroke-dasharray="3 3" visibility="hidden" pointer-events="none"/>`;
 svg.innerHTML=out;svg.dataset.w=di.W;svg.dataset.h=di.H;
 const last=points.filter(p=>p.quality==='GOOD').at(-1),bad=points.filter(p=>p.quality!=='GOOD').length;
 byId('meta-'+c.id).textContent=`${last?tx('last')+': '+fmtTime(last.t,true):'—'} · ${(metaById.get(c.id)?.sampleCount??points.length).toLocaleString()} ${tx('samples')}${c.lastError?' · API ERROR':''}`;
 const q=byId('quality-'+c.id);q.classList.toggle('bad',!!bad);q.textContent=bad?`⚠ ${bad} BAD/STALE`:`✓ ${S.lang==='tr'?'Kalite iyi':'Quality good'}`;
 byId('zoom-'+c.id).textContent=metaById.get(c.id)?.summarized?`${metaById.get(c.id).sampleCount.toLocaleString()} → ${plotted.toLocaleString()} · ${S.lang==='tr'?'özet · yakınlaş: detay':'summary · zoom for detail'}`:points.length>plotted*1.3?`${points.length.toLocaleString()} → ${plotted.toLocaleString()} · ${S.lang==='tr'?'yakınlaş: detay':'zoom: detail'}`:c.history.length?`ZOOM ×${c.history.length+1}`:(c.freeze?'HOLD':'LIVE');
 c.renderStats={raw:points.length,rendered:plotted,gaps:gaps.length,lastGood:last?.t||null};if(c.hover)showCross(c,c.hover,false);
 if(S.linkedCursor&&S.linkedTime!==null)drawLinkedMarker(c,S.linkedTime,false);
 if(c.needsFetch&&coreVisible&&!c.loading)void fetchSeries(c,true);
}

function chartAt(svg){return S.charts.find(c=>c.id===svg.dataset.id)}
function eventCoords(svg,e){let b=svg.getBoundingClientRect(),x=(e.clientX-b.left)/b.width*+svg.dataset.w,y=(e.clientY-b.top)/b.height*+svg.dataset.h;return {x,y,svgW:+svg.dataset.w,svgH:+svg.dataset.h};}

function showCross(c,pt,popup=true){if(!pt)return;const el=$(`[data-chart="${c.id}"]`);if(!el)return;const svg=el.querySelector('svg'),di=dims(svg),d=signalDefs.find(s=>s.id===c.id),ys=computeY(c,visible(c),d),x=P.l+(pt.t-c.range[0])/(c.range[1]-c.range[0])*di.pw,y=P.t+(ys[1]-pt.v)/(ys[1]-ys[0])*di.ph;
 const cross=svg.querySelector('.crosshair');if(cross)cross.innerHTML=`<line x1="${x}" x2="${x}" y1="${P.t}" y2="${P.t+di.ph}" stroke="var(--muted)" stroke-width="1" stroke-dasharray="3 4"/><circle cx="${x}" cy="${y}" r="5.2" fill="${d.color}" stroke="var(--text)" stroke-width="1.6"/>`;
 const tip=el.querySelector('.tooltip');tip.innerHTML=`<b>${esc(name(d))}</b><br>${fmtTime(pt.t,true)}<br><b style="color:${d.color}">${val(pt.v,d)} ${esc(d.unit)}</b> <span style="color:var(--good)">✓ SAMPLE</span>`;
 tip.style.left=`${Math.max(7,Math.min(di.W-175,x+13))}px`;tip.style.top=`${Math.max(5,Math.min(di.H-65,y-40))}px`;tip.style.display=popup?'block':'none';
}
function drawLinkedMarker(c,time,updateTip=false){const el=$(`[data-chart="${c.id}"]`);if(!el)return null;const svg=el.querySelector('svg');if(time<c.range[0]||time>c.range[1]){svg.querySelector('.crosshair').innerHTML='';return null;}
 const p=nearestValid(c,time),di=dims(svg),t=p?p.t:time,x=P.l+(t-c.range[0])/(c.range[1]-c.range[0])*di.pw;
 const ys=computeY(c,visible(c),signalDefs.find(s=>s.id===c.id)),y=p?P.t+(ys[1]-p.v)/(ys[1]-ys[0])*di.ph:0;
 svg.querySelector('.crosshair').innerHTML=`<line x1="${x}" x2="${x}" y1="${P.t}" y2="${P.t+di.ph}" stroke="var(--cyan)" stroke-width="1.1" stroke-dasharray="4 3" opacity=".9"/>${p?`<circle cx="${x}" cy="${y}" r="4" fill="${signalDefs.find(d=>d.id===c.id).color}" stroke="var(--text)" stroke-width="1.2"/>`:`<text x="${Math.min(x+5,di.W-65)}" y="${P.t+16}" fill="var(--amber)" font-size="10">NO SAMPLE</text>`}`;
 if(updateTip&&p)showCross(c,p,false);return p;
}
function updateLinkedCursor(time,sourceId){S.linkedTime=time;const summary=[];for(const c of S.charts){if(c.id===sourceId)continue;const p=drawLinkedMarker(c,time);if(p){const d=signalDefs.find(d=>d.id===c.id);summary.push(`${d.tag}: ${val(p.v,d)} ${d.unit}`)}else summary.push(`${signalDefs.find(d=>d.id===c.id).tag}: —`)}
 byId('sync-info').textContent=(S.lang==='tr'?'Ortak kayıt':'Linked samples')+` · ${fmtTime(time,true)} · `+summary.slice(0,3).join(' | ');
}

let drag=null;
byId('grid').addEventListener('pointerdown',e=>{const svg=e.target.closest('.chart-svg');if(!svg||e.button!==0||!e.target.classList.contains('plot-hit'))return;const c=chartAt(svg),pt=eventCoords(svg,e);S.activeId=c.id;document.querySelectorAll('.chart').forEach(el=>el.classList.toggle('is-active',el.dataset.chart===c.id));resizeLayout();drag={id:c.id,svg,start:Math.max(P.l,Math.min(+svg.dataset.w-P.r,pt.x)),last:pt.x,pid:e.pointerId};svg.setPointerCapture(e.pointerId);svg.classList.add('dragging');e.preventDefault()});
byId('grid').addEventListener('pointermove',e=>{const svg=e.target.closest('.chart-svg');if(!svg)return;const c=chartAt(svg),pt=eventCoords(svg,e);if(!c)return;if(drag&&drag.id===c.id){drag.last=Math.max(P.l,Math.min(pt.svgW-P.r,pt.x));const sel=svg.querySelector('.selection');if(sel){sel.setAttribute('x',Math.min(drag.start,drag.last));sel.setAttribute('width',Math.abs(drag.last-drag.start));sel.setAttribute('visibility','visible')}return}if(pt.x<P.l||pt.x>pt.svgW-P.r||pt.y<P.t||pt.y>pt.svgH-P.b){const t=svg.closest('.chart').querySelector('.tooltip');t.style.display='none';return}const t=c.range[0]+(pt.x-P.l)/(pt.svgW-P.l-P.r)*(c.range[1]-c.range[0]);const nearest=nearestValid(c,t);if(nearest){c.hover=nearest;S.activeId=c.id;showCross(c,nearest);if(S.linkedCursor)updateLinkedCursor(nearest.t,c.id)}else{c.hover=null;svg.closest('.chart').querySelector('.tooltip').style.display='none';svg.querySelector('.crosshair').innerHTML='';if(S.linkedCursor)updateLinkedCursor(t,c.id)}});
byId('grid').addEventListener('pointerup',e=>{if(!drag)return;const {id,svg,start,last}=drag,c=S.charts.find(x=>x.id===id);svg.classList.remove('dragging');try{svg.releasePointerCapture(e.pointerId)}catch{}const width=+svg.dataset.w-P.l-P.r;if(Math.abs(last-start)>14&&c){let low=Math.min(start,last),high=Math.max(start,last);let r0=c.range[0],dur=c.range[1]-r0;const t0=r0+(low-P.l)/width*dur,t1=r0+(high-P.l)/width*dur;if(t1-t0>=1000){c.history.push([...c.range]);c.range=[t0,t1];c.freeze=true;c.hover=null;refreshPanel(c);drawChart(c)}}else if(c){const pt=eventCoords(svg,e),time=c.range[0]+(pt.x-P.l)/width*(c.range[1]-c.range[0]);c.hover=nearestValid(c,time);showCross(c,c.hover)}drag=null;});
byId('grid').addEventListener('pointercancel',()=>{drag=null;});
byId('grid').addEventListener('pointerleave',e=>{if(drag)return;const c=e.target.closest('.chart');if(c){let tip=c.querySelector('.tooltip');if(tip)tip.style.display='none'}} ,true);
byId('grid').addEventListener('contextmenu',e=>{const svg=e.target.closest('.chart-svg');if(!svg)return;e.preventDefault();const c=chartAt(svg);if(!c)return;undo(c)});
function undo(c){if(c.history.length){c.range=c.history.pop();c.hover=null;refreshPanel(c);drawChart(c);toast(tx('undo'))}else{toast(tx('noUndo'))}}

byId('grid').addEventListener('click',e=>{
 const flag=e.target.closest('[data-event]');if(flag){const ev=alarmEvents.find(a=>(a.occurrenceId||a.alarmKey)===flag.dataset.event);if(ev)toast(`${ev.text||'Alarm'} · ${fmtTime(Date.parse(ev.activeAt||''),true)} · ${ev.state||'ACTIVE'}`);return}
 let item=e.target.closest('.chart');if(!item)return;const c=S.charts.find(s=>s.id===item.dataset.chart),action=e.target.closest('[data-action]')?.dataset.action;if(!c)return;const directRange=e.target.closest('[data-chart-window-btn]');if(directRange){const min=+directRange.dataset.chartWindowBtn;c.windowMin=min;c.history=[];c.range=[S.now-min*60000,S.now];c.hover=null;c.freeze=false;S.activeId=c.id;refreshPanel(c);renderLayout();return}if(!action)return;e.stopPropagation();
 if(action==='focus'){S.activeId=c.id;renderLayout();return}
 if(action==='maximize'){S.maximized=S.maximized===c.id?null:c.id;S.activeId=c.id;renderLayout();}
 else if(action==='remove'){S.charts=S.charts.filter(x=>x.id!==c.id);S.compareIds.delete(c.id);if(S.maximized===c.id)S.maximized=null;if(S.activeId===c.id)S.activeId=S.charts[0]?.id||null;renderLayout();}
 else if(action==='settings'){c.settings=!c.settings;renderLayout();}
 else if(action==='close-settings'){c.settings=false;renderLayout();}
 else if(action==='reset-chart'){c.windowMin=c.windowMin||15;c.range=[S.now-c.windowMin*60000,S.now];c.history=[];c.freeze=false;c.settings=false;c.hover=null;refreshPanel(c);renderLayout();}
 else if(action==='undo')undo(c);
 else if(action==='live-chart'){c.freeze=!c.freeze;if(!c.freeze){c.history=[];c.range=[S.now-c.windowMin*60000,S.now];refreshPanel(c)}renderLayout();}
});
document.addEventListener('click',e=>{
 if(e.target.closest('.settings,[data-action="settings"]'))return;
 if(S.charts.some(c=>c.settings)){S.charts.forEach(c=>{c.settings=false});renderLayout()}
});
document.addEventListener('keydown',e=>{
 if(e.key==='Escape'&&S.charts.some(c=>c.settings)){
  S.charts.forEach(c=>{c.settings=false});renderLayout();e.preventDefault();
 }
});
byId('grid').addEventListener('change',e=>{const item=e.target.closest('.chart');if(!item)return;const c=S.charts.find(s=>s.id===item.dataset.chart);if(!c)return;
 if(e.target.dataset.chartWindow){c.windowMin=+e.target.value;c.history=[];c.range=[S.now-c.windowMin*60000,S.now];c.hover=null;c.freeze=false;S.activeId=c.id;refreshPanel(c);renderLayout();return}
 if(e.target.dataset.chartType){if(['line','smooth','area'].includes(e.target.value)){c.chartType=e.target.value;c.smooth=c.chartType!=='line';drawChart(c)}return}
 if(e.target.dataset.setting){c[e.target.dataset.setting]=e.target.checked;
 if(e.target.dataset.setting==='freeze'&&!c.freeze){c.history=[];c.range=[S.now-c.windowMin*60000,S.now]}
 if(e.target.dataset.setting==='wide')renderLayout();else{drawChart(c);if(c.settings)openSettings(item,c);}}
 else if(e.target.dataset.action==='compare-check'){if(e.target.checked)S.compareIds.add(c.id);else S.compareIds.delete(c.id);byId('compare-count').textContent=`${S.compareIds.size} ${tx('selected')}`;byId('compare-open').disabled=S.compareIds.size<2}
});


function setWindow(min){S.now=Date.now();let c=S.charts.find(x=>x.id===S.activeId)||S.charts[0];if(!c)return;c.windowMin=min;c.history=[];c.range=[S.now-min*60000,S.now];c.hover=null;c.freeze=false;S.activeId=c.id;refreshPanel(c);renderLayout();}
document.querySelectorAll('[data-window]').forEach(b=>b.onclick=()=>setWindow(+b.dataset.window));
byId('live').onclick=()=>{S.live=!S.live;S.now=Date.now();if(S.live)for(const c of S.charts)if(!c.freeze&&!c.history.length){c.range=[S.now-c.windowMin*60000,S.now];refreshPanel(c)}renderLayout();};
byId('resetAll').onclick=()=>{S.maximized=null;S.now=Date.now();for(const c of S.charts){c.history=[];c.range=[S.now-c.windowMin*60000,S.now];c.freeze=false;c.hover=null;refreshPanel(c)}renderLayout()};

byId('compare').onclick=()=>{S.compare=!S.compare;if(!S.compare)S.compareIds.clear();renderLayout()};
byId('compare-open').onclick=()=>{if(S.compareIds.size<2)return toast(tx('chooseTwo'));S.overlay=true;byId('compare-overlay').classList.remove('hidden');setTimeout(drawComparison,80)};
byId('overlay-close').onclick=()=>{S.overlay=false;byId('compare-overlay').classList.add('hidden')};
function drawComparison(){const ids=[...S.compareIds],svg=byId('compare-svg'),b=svg.getBoundingClientRect(),w=Math.max(350,b.width),h=Math.max(200,b.height),pl=47,pr=20,pt=25,pb=30;svg.setAttribute('viewBox',`0 0 ${w} ${h}`);const all=ids.map(id=>signalDefs.find(x=>x.id===id));const tMin=Math.min(...S.charts.filter(c=>ids.includes(c.id)).map(c=>c.range[0])),tMax=Math.max(...S.charts.filter(c=>ids.includes(c.id)).map(c=>c.range[1]));let str='';for(let i=0;i<=4;i++){let y=pt+i*(h-pt-pb)/4;str+=`<line x1="${pl}" x2="${w-pr}" y1="${y}" y2="${y}" stroke="var(--grid)"/><text x="${pl-8}" y="${y+4}" text-anchor="end" font-size="10" fill="var(--muted)">${100-i*25}%</text>`}all.forEach(d=>{const p=allData.get(d.id).filter(v=>v.t>=tMin&&v.t<=tMax&&v.quality==='GOOD'),mi=Math.min(...p.map(v=>v.v)),ma=Math.max(...p.map(v=>v.v));if(!p.length)return;let x=t=>pl+(t-tMin)/(tMax-tMin)*(w-pl-pr),y=v=>pt+(1-(v-mi)/Math.max(1e-5,ma-mi))*(h-pt-pb);str+=`<path d="${monotonePath(p,x,y)}" fill="none" stroke="${d.color}" stroke-width="2.2"/>`});svg.innerHTML=str;byId('compare-legend').innerHTML=all.map(d=>`<span><i style="background:${d.color}"></i>${esc(name(d))} (${esc(d.unit)})</span>`).join('')}
function openDrawer(){S.drawer=true;byId('drawer').classList.remove('hidden');byId('drawer').setAttribute('aria-hidden','false');renderDrawer()}function closeDrawer(){S.targetSlot=null;S.drawer=false;byId('drawer').classList.add('hidden');byId('drawer').setAttribute('aria-hidden','true')}
function syncChartsOnlyButton(){const button=byId('charts-only-toggle');if(!button)return;const active=document.documentElement.classList.contains('charts-only');button.textContent=active?(S.lang==='tr'?'⛶ Tam ekrandan çık':'⛶ Exit full screen'):(S.lang==='tr'?'⛶ Yalnız grafikler':'⛶ Charts only');button.setAttribute('aria-pressed',String(active))}
async function toggleChartsOnly(){const root=document.documentElement,active=root.classList.toggle('charts-only');syncChartsOnlyButton();setTimeout(()=>{resizeLayout();S.charts.forEach(drawChart)},100);try{if(active&&!document.fullscreenElement&&root.requestFullscreen)await root.requestFullscreen();else if(!active&&document.fullscreenElement&&document.exitFullscreen)await document.exitFullscreen()}catch{toast(S.lang==='tr'?'Grafik görünümü açıldı; tarayıcı tam ekranı engelledi.':'Charts-only view opened; browser full screen was blocked.')}}
async function clearAllCharts(){if(!S.charts.length)return;if(!requireTrendEdit())return;const message=S.lang==='tr'?`Açık ${S.charts.length} trendin tümü kaldırılsın mı?`:`Remove all ${S.charts.length} open trends?`;if(!confirm(message))return;S.charts=[];S.selected.clear();S.compareIds.clear();S.compare=false;S.maximized=null;S.activeId=null;S.chartFilter='';byId('trend-open-filter').value='';renderLayout();await storeLayout();toast(S.lang==='tr'?'Tüm trendler kaldırıldı.':'All trends removed.')}
byId('add').onclick=openDrawer;byId('hf61-explorer-add').onclick=openDrawer;byId('hf61-explorer-search').addEventListener('input',renderExplorer);byId('hf61-explorer-list').addEventListener('click',e=>{const item=e.target.closest('[data-signal-id]');if(!item)return;const id=item.dataset.signalId;if(S.charts.some(c=>c.id===id)){S.charts=S.charts.filter(c=>c.id!==id);S.compareIds.delete(id);if(S.maximized===id)S.maximized=null;if(S.activeId===id)S.activeId=S.charts[0]?.id||null;renderLayout()}else appendSignals([id],null,Number.isInteger(S.targetSlot)?S.targetSlot:null);S.targetSlot=null;});byId('hf62-layout-select').onchange=e=>{S.layout=e.target.value;S.maximized=null;normalizeSlots();renderLayout();};byId('hf62-more-toggle').onclick=()=>{const panel=byId('hf62-advanced');panel.hidden=!panel.hidden;byId('hf62-more-toggle').setAttribute('aria-expanded',String(!panel.hidden));hf61Translate();};byId('drawer-close').onclick=closeDrawer;byId('search').oninput=renderDrawer;
byId('trend-open-filter').oninput=e=>{S.chartFilter=e.target.value;if(S.maximized&&!filteredCharts().some(c=>c.id===S.maximized))S.maximized=null;renderLayout()};
byId('charts-only-toggle').onclick=toggleChartsOnly;byId('clear-all-charts').onclick=clearAllCharts;
document.addEventListener('fullscreenchange',()=>{if(!document.fullscreenElement)document.documentElement.classList.remove('charts-only');syncChartsOnlyButton();setTimeout(()=>{resizeLayout();S.charts.forEach(drawChart)},100)});
function renderDrawer(){let q=(byId('search')?.value||'').toLocaleLowerCase('tr-TR');byId('signal-list').innerHTML=signalDefs.filter(d=>(`${d.device} ${d.tag} ${d.eng}`).toLocaleLowerCase('tr-TR').includes(q)).map(d=>`<div class="sig" draggable="true" data-signal="${d.id}" title="${S.lang==='tr'?'Sürükleyip grafik alanına bırak':'Drag into chart area'}"><input type="checkbox" ${S.selected.has(d.id)?'checked':''} aria-label="${esc(name(d))}"><span class="seriesdot" style="--series:${d.color}"></span><span style="flex:1"><b>${esc(name(d))}</b><br><small>${esc(d.unit)} · ${d.interval/1000}s</small></span><span class="draghandle">⠿</span></div>`).join('');}
byId('signal-list').addEventListener('change',e=>{const sig=e.target.closest('.sig');if(!sig)return;e.target.checked?S.selected.add(sig.dataset.signal):S.selected.delete(sig.dataset.signal)});
byId('signal-list').addEventListener('click',e=>{const sig=e.target.closest('.sig');if(!sig||e.target.matches('input'))return;const box=sig.querySelector('input');box.checked=!box.checked;box.checked?S.selected.add(sig.dataset.signal):S.selected.delete(sig.dataset.signal)});
byId('select-all').onclick=()=>{for(const d of signalDefs)S.selected.add(d.id);renderDrawer()};
function appendSignals(ids,afterId=null,targetSlot=null){
 if(!requireTrendEdit())return;
 const unique=[...new Set(ids)].filter(id=>signalDefs.some(d=>d.id===id)&&!S.charts.some(c=>c.id===id));
 if(!unique.length)return toast(tx('already'));
 const space=MAX_CHARTS-S.charts.length;if(space<=0)return toast(tx('limit'));
 const added=unique.slice(0,space).map(makeChart);
 if(S.layout!=='auto'){
   normalizeSlots();const used=new Set(S.charts.map(c=>c.slot));
   let preferred=Number.isInteger(targetSlot)?targetSlot:null;
   if(preferred===null&&afterId){const ref=S.charts.find(c=>c.id===afterId);preferred=ref?ref.slot+1:null;}
   for(const c of added){let slot=preferred!==null?preferred:0;while(used.has(slot)&&slot<MAX_CHARTS)slot++;if(slot>=MAX_CHARTS){slot=0;while(used.has(slot)&&slot<MAX_CHARTS)slot++;}if(slot>=MAX_CHARTS)break;c.slot=slot;used.add(slot);S.charts.push(c);preferred=slot+1;}
 }else{if(afterId){const index=S.charts.findIndex(c=>c.id===afterId);S.charts.splice(index+1,0,...added)}else S.charts.push(...added);}
 if(!S.activeId)S.activeId=added[0]?.id||null;
 renderLayout();for(const c of added)void fetchSeries(c,true);toast(`${tx('added')}: ${added.length}`);
}

byId('add-selected').onclick=()=>{if(!S.selected.size)return;appendSignals([...S.selected],null,Number.isInteger(S.targetSlot)?S.targetSlot:null);S.targetSlot=null;S.selected.clear();closeDrawer()};
byId('hf61-explorer-list').addEventListener('dragstart',e=>{const item=e.target.closest('[data-signal-id]');if(!item)return;e.dataTransfer.setData('text/plain',JSON.stringify([item.dataset.signalId]));e.dataTransfer.effectAllowed='copy';item.classList.add('dragging')});
byId('hf61-explorer-list').addEventListener('dragend',()=>document.querySelectorAll('.hf61-signal.dragging').forEach(el=>el.classList.remove('dragging')));
byId('grid').addEventListener('click',e=>{const slot=e.target.closest('[data-drop-slot]');if(!slot)return;S.targetSlot=Number(slot.dataset.dropSlot);openDrawer()});
byId('signal-list').addEventListener('dragstart',e=>{const sig=e.target.closest('.sig');if(!sig)return;let ids=S.selected.has(sig.dataset.signal)?[...S.selected]:[sig.dataset.signal];e.dataTransfer.setData('text/plain',JSON.stringify(ids));e.dataTransfer.effectAllowed='copy';sig.classList.add('dragging');});
byId('signal-list').addEventListener('dragend',()=>document.querySelectorAll('.sig.dragging').forEach(el=>el.classList.remove('dragging')));
byId('grid').addEventListener('dragover',e=>{if([...e.dataTransfer.types].includes('application/x-pgn-chart'))return;e.preventDefault();let chart=e.target.closest('.chart,[data-drop-slot]');byId('grid').classList.toggle('drag-target',!chart);document.querySelectorAll('.chart.drop-on,.hf62-slot.drop-on').forEach(el=>el.classList.remove('drop-on'));if(chart)chart.classList.add('drop-on');e.dataTransfer.dropEffect='copy'});
byId('grid').addEventListener('dragleave',e=>{if(!byId('grid').contains(e.relatedTarget)){byId('grid').classList.remove('drag-target');document.querySelectorAll('.chart.drop-on,.hf62-slot.drop-on').forEach(el=>el.classList.remove('drop-on'))}});
byId('grid').addEventListener('drop',e=>{if(e.dataTransfer.getData('application/x-pgn-chart'))return;e.preventDefault();let ids=[];try{ids=JSON.parse(e.dataTransfer.getData('text/plain')||'[]')}catch{}const after=e.target.closest('.chart')?.dataset.chart;const slot=e.target.closest('[data-drop-slot]');const targetSlot=slot?Number(slot.dataset.dropSlot):null;byId('grid').classList.remove('drag-target');document.querySelectorAll('.chart.drop-on,.hf62-slot.drop-on').forEach(el=>el.classList.remove('drop-on'));if(ids.length){appendSignals(ids,after,targetSlot);S.selected.clear();closeDrawer()}});
byId('collapse').onclick=()=>{const collapsed=byId('app').classList.toggle('collapsed');byId('collapse').textContent=collapsed?'»':'«';setTimeout(()=>S.charts.forEach(drawChart),220)};
byId('theme').onclick=()=>{S.theme=S.theme==='dark'?'light':'dark';document.documentElement.dataset.theme=S.theme;byId('theme').textContent=S.theme==='dark'?'☾':'☼';renderLayout()};

// HF3+ controls: aligned by sample timestamp; independent viewports and saved layout only.
byId('sync-cursor').onchange=e=>{S.linkedCursor=e.target.checked;if(!S.linkedCursor){S.linkedTime=null;byId('sync-info').textContent='';for(const c of S.charts)drawChart(c)}else toast(S.lang==='tr'?'Ortak imleç açık: grafiğin üzerine gel':'Linked cursor enabled: hover a chart')};
byId('col-width').oninput=e=>{S.leftPercent=Math.min(70,Math.max(30,+e.target.value));resizeLayout();S.charts.forEach(drawChart)};
async function storeLayout(){if(!requireTrendEdit()||!await refreshTrendAuth())return;const data={savedAt:Date.now(),leftPercent:S.leftPercent,linkedCursor:S.linkedCursor,layout:S.layout,charts:S.charts.map(c=>({id:c.id,windowMin:c.windowMin,flags:c.flags,threshold:c.threshold,points:c.points,batch:c.batch,smooth:c.smooth,chartType:c.chartType,wide:c.wide,slot:c.slot}))};
 try{
   const session=sessionStorage.getItem('prognode.accessSession')||'';
   const resp=await fetch('/api/backup/layout',{method:'POST',headers:{'Content-Type':'application/json','X-PROGNODE-Session':session},body:JSON.stringify(data)});
   if(!resp.ok)throw Error('Core layout save failed (sign in as Core owner)');
   localStorage.setItem(STORE_KEY,JSON.stringify(data));
   S.savedAt=data.savedAt;toast(tx('layoutSaved'));byId('save-layout').classList.add('layout-saved');resizeLayout()}
 catch(e){toast(S.lang==='tr'?'Düzen kaydedilemedi. Yetkili oturumunuzu kontrol edin.':'Layout not saved. Check your authorized session.');void refreshTrendAuth()}}
byId('save-layout').onclick=storeLayout;
const csvButton=byId('export-csv');if(csvButton)csvButton.onclick=()=>{
 if(!S.charts.length)return toast(S.lang==='tr'?'Önce grafik seçin.':'Select a chart first.');
 if(S.charts.some(c=>metaById.get(c.id)?.summarized))return toast(S.lang==='tr'?'Özet veri: eksiksiz CSV için Historian dışa aktarımını kullanın veya yakınlaşın.':'Summarized range: export full CSV from Historian or zoom in.');
 const rows=[['Device / Tag','Time (UTC)','Value','Quality','Unit']];
 for(const c of S.charts){const d=signalDefs.find(s=>s.id===c.id);for(const p of visible(c))rows.push([name(d),new Date(p.t).toISOString().slice(0,19).replace('T',' '),p.v??'',p.quality,d.unit])}
 const csv='\uFEFFsep=;\r\n'+rows.map(row=>row.map(v=>'"'+String(v).replaceAll('"','""')+'"').join(';')).join('\r\n');
 const url=URL.createObjectURL(new Blob([csv],{type:'text/csv;charset=utf-8'}));const a=document.createElement('a');a.href=url;a.download='PROGNODE_Trend_Studio.csv';a.click();setTimeout(()=>URL.revokeObjectURL(url),5000);
};
byId('reset-layout').onclick=async()=>{
 if(!requireTrendEdit()||!await refreshTrendAuth())return;
 try{
  const token=sessionStorage.getItem('prognode.accessSession')||'';
  const response=await fetch('/api/backup/layout',{method:'DELETE',headers:{'X-PROGNODE-Session':token}});
  if(!response.ok)throw Error('Core rejected the reset');
  localStorage.removeItem(STORE_KEY);
  S.charts=[];S.leftPercent=50;S.layout='auto';S.maximized=null;S.activeId=null;
  S.linkedCursor=false;S.linkedTime=null;S.savedAt=null;S.compare=false;S.compareIds.clear();S.live=true;
  renderLayout();applyTrendPermissions();toast(tx('layoutReset'));
 }catch{toast(S.lang==='tr'?'Sıfırlama reddedildi; tekrar giriş yapın.':'Reset rejected; sign in again.');void refreshTrendAuth()}
};
// Pointer fallback for chart handles: Chrome/embedded WebView sometimes suppresses
// native HTML dragstart on buttons when the destination is in a scrollable grid.
// The same exact-cell placement therefore also works with pointer drag/release.
let pointerMovingChart=null, nativeChartDrag=false;
document.addEventListener('pointerdown',e=>{const handle=e.target.closest('.move-handle');if(!handle)return;pointerMovingChart=handle.closest('.chart')?.dataset.chart||null;nativeChartDrag=false},true);
document.addEventListener('dragstart',e=>{if(e.target.closest('.move-handle')){nativeChartDrag=true;pointerMovingChart=null}},true);
document.addEventListener('pointerup',e=>{
 if(!pointerMovingChart||nativeChartDrag)return;
 const moving=S.charts.find(c=>c.id===pointerMovingChart),slot=e.target.closest('[data-drop-slot]'),target=e.target.closest('.chart');
 pointerMovingChart=null;if(!moving)return;
 const dest=S.charts.find(c=>c.id===target?.dataset.chart);
 if(S.layout==='auto'){if(dest&&dest.id!==moving.id){const src=S.charts.findIndex(c=>c.id===moving.id),dst=S.charts.findIndex(c=>c.id===dest.id);S.charts.splice(src,1);S.charts.splice(dst,0,moving);renderLayout();}}
 else{const requested=slot?Number(slot.dataset.dropSlot):dest?.slot;
   if(Number.isInteger(requested)&&requested!==moving.slot){const old=moving.slot;moving.slot=requested;if(dest)dest.slot=old;renderLayout();toast(S.lang==='tr'?'Panel taşındı':'Panel moved');}}
},true);
byId('grid').addEventListener('dragstart',e=>{const move=e.target.closest('.move-handle');if(!move)return;const chart=move.closest('.chart');if(!chart)return;e.dataTransfer.effectAllowed='move';e.dataTransfer.setData('application/x-pgn-chart',chart.dataset.chart);chart.querySelector('.chart-head').classList.add('dragging');});
byId('grid').addEventListener('dragend',()=>{document.querySelectorAll('.chart-head.dragging').forEach(el=>el.classList.remove('dragging'));document.querySelectorAll('.chart.reorder-over').forEach(el=>el.classList.remove('reorder-over'));});
byId('grid').addEventListener('dragover',e=>{if(![...e.dataTransfer.types].includes('application/x-pgn-chart'))return;e.preventDefault();e.stopPropagation();document.querySelectorAll('.chart.reorder-over').forEach(el=>el.classList.remove('reorder-over'));const el=e.target.closest('.chart,[data-drop-slot]');if(el)el.classList.add(el.classList.contains('chart')?'reorder-over':'drop-on');e.dataTransfer.dropEffect='move'},true);
byId('grid').addEventListener('drop',e=>{
 const from=e.dataTransfer.getData('application/x-pgn-chart');if(!from)return;
 e.preventDefault();e.stopPropagation();
 const destination=e.target.closest('.chart'),slot=e.target.closest('[data-drop-slot]');
 const moving=S.charts.find(c=>c.id===from);
 if(!moving)return;
 if(S.layout==='auto'){
   const target=destination?.dataset.chart;
   if(target&&target!==from){const src=S.charts.findIndex(c=>c.id===from),dst=S.charts.findIndex(c=>c.id===target);const chart=S.charts.splice(src,1)[0];S.charts.splice(dst,0,chart)}
 }else{
   normalizeSlots();const targetChart=S.charts.find(c=>c.id===destination?.dataset.chart);
   const targetSlot=slot?Number(slot.dataset.dropSlot):targetChart?.slot;
   if(Number.isInteger(targetSlot)&&targetSlot!==moving.slot){const original=moving.slot;moving.slot=targetSlot;if(targetChart)targetChart.slot=original}
 }
 renderLayout();toast(S.lang==='tr'?'Panel hedef hücreye taşındı':'Chart placed in target cell');
},true);


document.querySelectorAll('[data-lang]').forEach(b=>b.onclick=()=>setLang(b.dataset.lang));
byId('help').onclick=()=>{S.help=!S.help;byId('help-panel').classList.toggle('hidden',!S.help);byId('help-panel').innerHTML=`<strong>${tx('helpTitle')}</strong><br><br>1. ${tx('add')} → ${tx('dragGuide')}<br>2. ${tx('zoomhint')}<br>3. ${tx('maximize')} · ${tx('settingsChart')}<br>4. ${tx('compare')} → ${tx('chooseTwo')}<br><br>${S.lang==='tr'?'Core Historian gerçek verileri':'Live Core Historian data'}`};
document.querySelectorAll('[data-nav]').forEach(b=>{if(b.dataset.nav!=='trend')b.onclick=()=>toast(S.lang==='tr'?'Core menüsünden gezinin.':'Use the Core navigation.')});
document.addEventListener('keydown',e=>{if(e.key==='Escape'){closeDrawer();byId('compare-overlay').classList.add('hidden');S.overlay=false;S.maximized=null;renderLayout()}});
// Dynamic chart cards are rebuilt after every layout operation. Re-apply the
// validated access state without changing page layout or chart rendering.
let permissionRefreshQueued=false;
new MutationObserver(()=>{if(permissionRefreshQueued)return;permissionRefreshQueued=true;queueMicrotask(()=>{
 permissionRefreshQueued=false;applyTrendPermissions();
});}).observe(byId('grid'),{childList:true,subtree:false});
// Central capture-phase gate: disabled controls alone are insufficient because
// delegated drag/drop and pointer handlers also edit in-memory state.
const guardedSelector='.hf61-signal,.hf62-slot,.move-handle,.sig,[data-action="remove"],[data-action="settings"],[data-setting],[data-chart-type],#add,#hf61-explorer-add,#select-all,#add-selected,#save-layout,#reset-layout,#hf62-layout-select,#col-width';
for(const eventName of ['click','change','input','pointerdown','dragstart','drop']){
 document.addEventListener(eventName,e=>{if(Core.authenticated&&!Core.authPending)return;
  if(!e.target.closest(guardedSelector))return;
  e.preventDefault();e.stopImmediatePropagation();
  if(eventName==='click')requireTrendEdit();
 },true);
}
window.addEventListener('resize',()=>{clearTimeout(window.__resizeTimer);window.__resizeTimer=setTimeout(()=>{resizeLayout();S.charts.forEach(drawChart);if(S.overlay)drawComparison()},120)});
// Core is authoritative: update time windows without fabricating any process samples.
setInterval(()=>{
 if(!coreVisible||document.hidden||!S.live)return;S.now=Date.now();
 for(const c of S.charts){if(c.freeze||c.history.length)continue;
  if(c.loading)continue;
  c.range=[S.now-c.windowMin*60000,S.now];
  if(!c.loading&&Date.now()-(c.lastFetch||0)>=(c.windowMin>=10080?60000:c.windowMin>=1440?30000:10000))void fetchSeries(c);
 }
},3000);
setInterval(()=>{if(coreVisible&&!document.hidden){void refreshTrendAuth();if(Core.ready)void fetchContext();if(!Core.ready||Date.now()-Core.lastCatalogAt>15000)void loadCatalog()}syncAppearance()},15000);
setInterval(()=>{void refreshFullscreenAlarmIndicator()},3000);
document.addEventListener('visibilitychange',()=>{if(!document.hidden&&coreVisible){void refreshTrendAuth();void loadCatalog();for(const c of S.charts){drawChart(c);void fetchSeries(c,true)}}});
window.__PGN_TEST__={S,Core,signalDefs:()=>signalDefs,makeChart,appendSignals,undo,setWindow,drawChart,renderLayout,updateLinkedCursor,storeLayout,nearestValid,visible,fetchSeries,loadCatalog,effectiveColumns,filteredCharts,slotItems,normalizeSlots,toggleChartsOnly,clearAllCharts,refreshTrendAuth,coreVisibleChanged};
setLang('en');
void refreshTrendAuth();
void loadCatalog();
window.addEventListener('DOMContentLoaded',()=>{applyTrendPermissions()});
syncAppearance();
setTimeout(()=>{if(window.parent===window&&!coreVisible)coreVisibleChanged(true)},250);

})();
