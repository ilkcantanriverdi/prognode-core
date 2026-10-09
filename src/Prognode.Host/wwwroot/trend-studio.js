/* PROGNODE Trend Studio RC6.4 preview. Zero external chart dependencies.
   All data comes from the normal Core API; no process data is simulated/injected. */
(function () {
  'use strict';
  const ui = {
    selected: [], viewId: null, axes: {}, live: true, activeCursor: null, viewMode:'separate',
    smooth:true, compare:false, laneModels:new Map(), splitKey:null, rawRange:null,
    cursors: {A:null,B:null}, hover: null, drag: null, range: null,
    events: {alarms:[], batches:[], definitions:[]}, navigatorPayload: null, selectedEventKey:null, flagHits:[],
    loading: false, nextAllowed:0, requestId:0, initialized:false, liveWindowMinutes:60, lastRefreshAt:null,
    color:'#38d5e5', poolKey:null, cardKey:null, navKey:null, drawQueued:false, palette:['#38d5e5','#86e6ad','#ffc977','#fa839c','#ac9cf5','#68a6ee','#f489cb','#ec9d58']
  };
  window.PrognodeStudio = ui;
  const el = id => document.getElementById(id);
  const safe = value => String(value ?? '').replace(/[&<>"']/g, c=>({'&':'&amp;','<':'&lt;','>':'&gt;','"':'&quot;',"'":'&#39;'}[c]));
  const tr = (a,b) => b;
  const fmtTime = ms => new Date(ms).toLocaleString('en-US',{month:'short',day:'2-digit',hour:'2-digit',minute:'2-digit',second:'2-digit'});
  const ms = p => Date.parse(p?.timestamp || '') || 0;
  const selectedTrend = () => state.trends.find(x=>x.id===state.selectedTrendId);
  const selectedTag = id => state.tags.find(t=>t.id===id);
  const displayName = id => {const tag=selectedTag(id);const device=state.devices.find(d=>d.id===tag?.deviceId);return [device?.name,tag?.name||id].filter(Boolean).join(' — ');};
  const recordedIds = () => new Set((state.historianConfigurations||[]).filter(x=>x.configuration?.enabled===true).map(x=>x.configuration.tagId));
  const canPlot = id => recordedIds().has(id);
  const alarmTagId = alarm => alarm?.tagId || ui.events.definitions.find(d=>d.id===alarm?.definitionId)?.tagId || null;
  const relevantAlarms = tagIds => ui.events.alarms.filter(a=>alarmTagId(a)?tagIds.includes(alarmTagId(a)):Boolean(a.isSystem&&el('studioSystemAlarmToggle')?.checked));
  const visibleIds = () => ui.selected.filter(id=>!state.hiddenTrendTags.has(id));
  function updateCounts(){
    const saved=state.trends?.length||0, active=ui.selected.filter(id=>canPlot(id)).length;
    if(el('trendCount'))el('trendCount').textContent=String(saved);
    if(el('studioOpenCount'))el('studioOpenCount').textContent=active+' '+'active chart'+(active===1?'':'s');
    if(el('navTrendCount')){el('navTrendCount').textContent=String(active);el('navTrendCount').title='Number of active selected charts';}
  }
  function syncViewMode(){
    ui.viewMode=ui.compare?'compare':'separate';
    const control=el('studioCompareCheck');if(control)control.checked=ui.compare;
    el('studioCompareCaption')?.classList.toggle('hidden',!ui.compare||ui.selected.length<2);
    updateCounts();
  }
  const studioStrings={
    en:{signalExplorer:'Signals',searchPlaceholder:'Search tags or devices...',loading:'Loading Historian Tags…',addHistorian:'＋ Add Tag to Historian',compareSignals:'Compare trends',compareHelp:'Keep individual charts and add one overlay for selected signals.',savedViews:'Saved views',saveOptional:'Optional: save this view',localData:'Data stays on this Core',clearSelection:'Clear selection',selectSignals:'Select a signal to see its trend',historianLocal:'LOCAL HISTORIAN',custom:'Custom',from:'From',to:'To',applyRange:'Apply range',alarmScale:'Y axis also includes configured alarm thresholds',compareTitle:'COMPARISON OF SELECTED SIGNALS',navigator:'TIME NAVIGATOR',navigationHelp:'Drag to zoom · Mouse wheel scrolls · Double-click to reset',statistics:'Signal statistics',selectedRange:'SELECTED RANGE',processContext:'Process events',alarmEvents:'Alarm events',alarmFlags:'Chart flags',alarmThresholds:'Alarm thresholds',batch:'Batch / Lot',systemAlarms:'System alarms',lastSample:'Latest sample',noSamples:'No samples in this range',recordedOnly:'Only Tags added to Historian are shown.',noHistorian:'No Tags added to Historian yet.',live:'LIVE · PAUSE',paused:'PAUSED · RESUME',pointCount:'rendered points',ready:'CORE CONNECTED',empty:'First add a Tag to Historian to chart it.'}
  };
  function studioText(key){return (studioStrings[state.language]||studioStrings.en)[key]||studioStrings.en[key]||key;}
  function applyStudioLanguage(){
    document.querySelectorAll('#page-trends [data-studio-i18n]').forEach(n=>{n.textContent=studioText(n.dataset.studioI18n);});
    document.querySelectorAll('#page-trends [data-studio-placeholder]').forEach(n=>{n.placeholder=studioText(n.dataset.studioPlaceholder);});
    el('studioExplorerHint')?.replaceChildren(document.createTextNode(studioText('recordedOnly')));
    syncUi();syncViewMode();ui.poolKey=null;ui.cardKey=null;renderTagPool();renderContext();drawTrendChart();
  }
  const color = id => { const trn=selectedTrend(); const i=trn?.tagIds.indexOf(id)??-1; const palette=state.theme==='light'?['#0086a7','#0a966d','#b66b22','#c44262','#6853ba','#276bb0','#ab367e','#aa6e30']:ui.palette; return i>=0?trn.colors?.[i]||palette[i%8]:palette[Math.max(0,ui.selected.indexOf(id))%8];};
  const good = p => p && String(p.quality||'').toLowerCase()==='good' && p.value!==null && p.value!==undefined && Number.isFinite(Number(p.value));
  const valueText = (id,v,withUnit=true) => {
    const tag=selectedTag(id); if(v==null || !Number.isFinite(Number(v))) return '—';
    if(String(tag?.dataType||'').toLowerCase()==='bool') return Number(v)?'ON':'OFF';
    const digits=Math.max(0,Math.min(4,Number(tag?.decimalPlaces ?? 2)));
    const n=Number(v).toLocaleString('en-US',{maximumFractionDigits:digits,minimumFractionDigits:Math.min(1,digits)});
    return n + (withUnit && tag?.unit?' '+tag.unit:'');
  };
  const numericConditions = new Set(['GreaterThan','GreaterThanOrEqual','LessThan','LessThanOrEqual']);
  const highConditions = new Set(['GreaterThan','GreaterThanOrEqual']);
  const lowConditions = new Set(['LessThan','LessThanOrEqual']);
  function numericLimits(tagId, definitions=ui.events.definitions) {
    return (definitions||[]).filter(d=>d.tagId===tagId && d.enabled!==false && numericConditions.has(d.condition) && d.threshold!==null && d.threshold!==undefined && Number.isFinite(Number(d.threshold)))
      .map(d=>({...d, threshold:Number(d.threshold), zone:highConditions.has(d.condition)?'HIGH':'LOW'}));
  }
  // One consistent classification drives chart segments, legends and A/B values.
  // This visual boundary is the configured setpoint, not the debounced alarm runtime state.
  function valueZone(value, limits) {
    if(!Number.isFinite(Number(value)))return 'UNKNOWN';
    if(limits.some(d=>d.zone==='HIGH' && (d.condition==='GreaterThan' ? +value>d.threshold : +value>=d.threshold)))return 'HIGH';
    if(limits.some(d=>d.zone==='LOW' && (d.condition==='LessThan' ? +value<d.threshold : +value<=d.threshold)))return 'LOW';
    return 'NORMAL';
  }
  function zoneColor(zone,base) {return zone==='HIGH'?'#ff6c82':zone==='LOW'?'#f6af5c':base;}
  function eventKey(e){return String(e.alarmKey||e.definitionId||'alarm')+'|'+String(e.activeAt||'');}
  const title = () => selectedTrend()?.name || 'New analysis';

  function setSelectedFromSaved() {
    if(state.selectedTrendId && ui.viewId!==state.selectedTrendId) {
      const view=selectedTrend(); if(view) { ui.viewId=view.id;ui.selected=[...view.tagIds].filter(canPlot);ui.axes={};ui.cursors={A:null,B:null};ui.navigatorPayload=null;ui.splitKey=null;
        if(!el('trendRange')?.dataset.userChanged && Number(view.defaultWindowMinutes)>0){ui.liveWindowMinutes=Number(view.defaultWindowMinutes);el('trendRange').value=String(ui.liveWindowMinutes);syncTrendRangePills();}
      }
    }
    updateCounts();
  }
  function renderTagPool() {
    const pool=el('studioTagPool');if(!pool)return;
    const search=(el('studioTagSearch')?.value||'').toLocaleLowerCase('en-US').trim();
    const hist=recordedIds();const key=search+'|'+ui.selected.join(',')+'|'+state.tags.length+'|'+[...hist].sort().join(',')+'|'+state.language;
    if(ui.poolKey===key)return;ui.poolKey=key;
    const all=state.tags.filter(tag=>hist.has(tag.id)&&[tag.name,state.devices.find(d=>d.id===tag.deviceId)?.name].some(x=>String(x||'').toLocaleLowerCase('en-US').includes(search)));
    const shown=all.slice(0,160);
    pool.innerHTML=shown.length?shown.map(tag=>{
      const checked=ui.selected.includes(tag.id);
      return `<button type="button" class="studio-tag-row ${checked?'selected':''}" data-studio-tag="${safe(tag.id)}" aria-pressed="${String(checked)}" title="${safe(displayName(tag.id))}">
        <span class="studio-tag-indicator">${checked?'✓':''}</span><span class="studio-tag-meta"><strong>${safe(displayName(tag.id))}</strong></span></button>`;
    }).join('')+(all.length>160?`<div class="studio-muted">${all.length-160} ${'more signals; refine your search.'}</div>`:'')
      :`<div class="studio-muted">${search?'No matching recorded Tags.':studioText('noHistorian')}</div>`;
    pool.querySelectorAll('[data-studio-tag]').forEach(node=>node.addEventListener('click', async ()=>{
      const id=node.dataset.studioTag;if(!canPlot(id))return;
      if(ui.selected.includes(id)) ui.selected=ui.selected.filter(x=>x!==id);
      else if(ui.selected.length>=8){showToast('Choose up to 8 signals.');return;}
      else ui.selected.push(id);
      state.selectedTrendId=null;ui.viewId=null;state.trendPayload=null;ui.navigatorPayload=null;ui.cursors={A:null,B:null};ui.splitKey=null;
      updateCounts();syncViewMode();renderTrendList();renderTagPool();await refreshStudio(true);
    }));
  }

  function renderCards(payload) {
    const cards=el('trendLegend'),stats=el('trendStats');if(!cards||!stats)return;
    const renderKey=ui.requestId+'|'+ui.selected.join(',')+'|'+JSON.stringify(ui.axes)+'|'+[...state.hiddenTrendTags].join(',');
    if(ui.cardKey===renderKey)return;ui.cardKey=renderKey;
    if(!ui.selected.length){cards.innerHTML='<div class="studio-muted">'+'Choose signals on the left.'+'</div>';stats.innerHTML='<div class="studio-muted">No signals selected.</div>';return;}
    cards.innerHTML=ui.selected.map(id=>{
      const tag=selectedTag(id), s=payload?.series.find(x=>x.tagId===id);
      const last=[...(s?.points||[])].reverse().find(good);const hidden=state.hiddenTrendTags.has(id);
      const limits=numericLimits(id),zone=last?valueZone(last.value,limits):'UNKNOWN';
      const limitText=limits.map(d=>d.zone+' '+d.threshold).join(' · ');
      return `<div class="studio-signal-card ${hidden?'hidden-series':''}"><div class="studio-card-row"><div class="studio-signal-label" data-studio-hide="${safe(id)}" title="Toggle signal"><span class="studio-legend-color" style="background:${safe(color(id))}"></span><span>${safe(displayName(id))}</span></div><select data-studio-axis="${safe(id)}" title="Y axis"><option value="left" ${ui.axes[id]!=='right'?'selected':''}>L axis</option><option value="right" ${ui.axes[id]==='right'?'selected':''}>R axis</option></select></div><strong style="color:${safe(zoneColor(zone,color(id)))}">${safe(valueText(id,last?.value))}</strong><div class="studio-card-foot"><span>${safe('Last sample')}</span><span>${s?.sampleCount??0} ${'points'}</span></div>${limits.length?`<div class="studio-alarm-state" data-zone="${zone}"><b>${zone}</b><span title="Configured alarm setpoints">${safe(limitText)}</span></div>`:''}</div>`;
    }).join('');
    cards.querySelectorAll('[data-studio-hide]').forEach(node=>node.addEventListener('click',()=>{
      const id=node.dataset.studioHide;if(state.hiddenTrendTags.has(id)) state.hiddenTrendTags.delete(id);else state.hiddenTrendTags.add(id);drawStudio();
    }));
    cards.querySelectorAll('[data-studio-axis]').forEach(node=>node.addEventListener('change',()=>{ui.axes[node.dataset.studioAxis]=node.value;drawStudio();}));
    stats.innerHTML=ui.selected.map(id=>{
      const t=selectedTag(id),s=payload?.series.find(x=>x.tagId===id);
      return `<div class="studio-stat-line"><div><span class="studio-legend-color" style="width:7px;height:12px;border-radius:3px;background:${safe(color(id))}"></span><b>${safe(displayName(id))}</b></div><div class="studio-quad"><div><span>MIN</span><b>${safe(valueText(id,s?.minimum,false))}</b></div><div><span>MAX</span><b>${safe(valueText(id,s?.maximum,false))}</b></div><div><span>AVG</span><b>${safe(valueText(id,s?.average,false))}</b></div><div><span>POINTS</span><b>${s?.sampleCount??0}</b></div></div></div>`;
    }).join('');
  }

  async function loadContext(start,end,requestId) {
    // Read-only existing Core endpoints; failures never block a trend plot.
    const res=await Promise.allSettled([
      api('/api/alarms/history?limit=500'), api('/api/batches?limit=100'), api('/api/alarms/definitions')
    ]);
    if(requestId!==ui.requestId)return;
    const [alarms,batches,definitions]=res.map(x=>x.status==='fulfilled'&&Array.isArray(x.value)?x.value:[]);
    ui.events.alarms=alarms.filter(a=>{const startMs=Date.parse(a.activeAt||'');const endMs=Date.parse(a.clearedAt||'')||Date.now();return Number.isFinite(startMs)&&startMs<=end&&endMs>=start;});
    ui.events.batches=batches.filter(b=>{const startMs=Date.parse(b.startedAt||'');const endMs=Date.parse(b.endedAt||'')||Date.now();return Number.isFinite(startMs)&&startMs<=end&&endMs>=start;});
    ui.events.definitions=definitions.length?definitions:(state.alarmDefinitions||[]);
    if(ui.selectedEventKey&&!ui.events.alarms.some(a=>eventKey(a)===ui.selectedEventKey))ui.selectedEventKey=null;
    ui.cardKey=null;renderContext();drawStudio();
  }
  function selectAlarmEvent(key) {
    ui.selectedEventKey=key;
    renderContext();drawStudio();
    el('studioEventDetail')?.scrollIntoView?.({block:'nearest',behavior:'smooth'});
  }
  function renderContext() {
    const box=el('studioEventList'),detail=el('studioEventDetail');if(!box||!detail)return;
    const alarms=el('studioAlarmToggle')?.checked!==false?relevantAlarms(ui.selected):[];
    if(el('studioContextScope'))el('studioContextScope').textContent='Selected Tag alarms'+' · '+alarms.length;
    const batches=el('studioBatchToggle')?.checked!==false?ui.events.batches:[];
    const entries=[...alarms.map(a=>({type:'alarm',name:(selectedTag(alarmTagId(a))?.name||a.sourceName||'System')+' · '+(a.text||'Alarm'),time:Date.parse(a.activeAt||''),detail:a.state||'ACTIVE',event:a,key:eventKey(a)})),
      ...batches.map(b=>({type:'batch',name:b.batchNo||'Batch',time:Date.parse(b.startedAt||''),detail:b.state||'RUNNING'}))]
      .sort((a,b)=>b.time-a.time).slice(0,50);
    box.innerHTML=entries.length?entries.map(x=>x.type==='alarm'
      ?`<button type="button" class="studio-event ${ui.selectedEventKey===x.key?'is-selected':''}" data-studio-event="${safe(x.key)}" title="${safe('Show alarm details')}"><span>⚑</span><span class="studio-event-copy"><strong>${safe(x.name)}</strong><small>${Number.isFinite(x.time)?safe(fmtTime(x.time)):''} · ${safe(x.detail)}</small></span><span class="studio-event-chevron">›</span></button>`
      :`<div class="studio-event batch"><span>◈</span><span class="studio-event-copy"><strong>${safe(x.name)}</strong><small>${Number.isFinite(x.time)?safe(fmtTime(x.time)):''} · ${safe(x.detail)}</small></span></div>`).join('')
      :`<div class="studio-muted">${'No events in this range.'}</div>`;
    box.querySelectorAll('[data-studio-event]').forEach(node=>node.addEventListener('click',()=>selectAlarmEvent(node.dataset.studioEvent)));
    const selected=alarms.find(a=>eventKey(a)===ui.selectedEventKey);
    if(!selected){detail.classList.add('hidden');detail.innerHTML='';return;}
    detail.classList.remove('hidden');
    detail.innerHTML=`<div class="studio-detail-head"><strong>⚑ ${safe(selected.text||'Alarm')}</strong><button type="button" id="studioCloseEvent" aria-label="Close event detail">×</button></div>
      <div class="studio-detail-grid"><span>${'Source'}</span><b>${safe(selected.sourceName||selected.tagId||'System')}</b>
      <span>${'Priority'}</span><b>${safe(selected.priority||'—')}</b>
      <span>ACTIVE</span><b>${safe(fmtTime(Date.parse(selected.activeAt)))}</b>
      <span>ACK</span><b>${selected.acknowledgedAt?safe(fmtTime(Date.parse(selected.acknowledgedAt))):'—'}</b>
      <span>CLEARED</span><b>${selected.clearedAt?safe(fmtTime(Date.parse(selected.clearedAt))):'—'}</b>
      <span>${'State'}</span><b>${safe(selected.state||'—')}</b></div>`;
    el('studioCloseEvent')?.addEventListener('click',()=>{ui.selectedEventKey=null;renderContext();drawStudio();});
  }

  async function refreshStudio(force=false) {
    if(!el('page-trends'))return;
    setSelectedFromSaved();renderTagPool();
    if(!ui.selected.length){state.trendPayload=null;updateCounts();drawStudio();return;}
    if(ui.selected.some(id=>!canPlot(id))){
      state.trendPayload=null;el('studioDataStatus').textContent='HISTORIAN CONFIG REQUIRED';
      if(el('studioSampleHealth'))el('studioSampleHealth').textContent='Add selected Tags to Historian first';
      drawStudio();return;
    }
    const now=Date.now();
    if((ui.loading && !force) || (!force && now<ui.nextAllowed))return;
    if(!ui.live && !force)return;
    let range;
    try{
      // LIVE is always a moving window ending NOW; dates apply only when paused.
      if(ui.live){const end=Date.now();range={from:new Date(end-ui.liveWindowMinutes*60000),to:new Date(end)};}
      else if(el('trendRange')?.value==='custom') range=currentTrendRange();
      else {const frozen=ui.range||{from:Date.now()-ui.liveWindowMinutes*60000,to:Date.now()};range={from:new Date(frozen.to-ui.liveWindowMinutes*60000),to:new Date(frozen.to)};}
    }catch(err){showToast(err.message);return;}
    const requestId=++ui.requestId;ui.loading=true;el('studioDataStatus').textContent='READING CORE…';
    try{
      const selected=[...ui.selected];
      const query='?from='+encodeURIComponent(range.from.toISOString())+'&to='+encodeURIComponent(range.to.toISOString())+'&maxPoints=1800';
      const url=(state.selectedTrendId && selectedTrend() && JSON.stringify(selectedTrend().tagIds)===JSON.stringify(selected))
        ?`/api/trends/${state.selectedTrendId}/points${query}`
        :`/api/trend-studio/series?tagIds=${encodeURIComponent(selected.join(','))}&${query.slice(1)}`;
      const payload=await api(url);
      if(requestId!==ui.requestId)return;
      state.trendPayload=payload;ui.range={from:Date.parse(payload.from),to:Date.parse(payload.to)};
      if(!ui.navigatorPayload || ui.live)ui.navigatorPayload=payload;
      ui.nextAllowed=Date.now()+3700;ui.loading=false;ui.lastRefreshAt=Date.now();
      el('studioDataStatus').textContent=studioText('ready');drawStudio();
      void loadContext(ui.range.from,ui.range.to,requestId);
    }catch(error){
      if(requestId!==ui.requestId)return;
      ui.loading=false;el('studioDataStatus').textContent='CORE QUERY FAILED';
      state.trendPayload=null;el('studioTimeText').textContent=error.message||'Could not query historian';
      showToast(error.message||'Trend data unavailable');
      drawStudio();
    }
  }

  function updateSampleHealth(series,windowEnd) {
    const badge=el('studioSampleHealth');if(!badge)return;
    const samples=series.flatMap(s=>(s.points||[]).filter(good).map(p=>ms(p))).filter(t=>Number.isFinite(t)&&t>0);
    if(!samples.length){badge.textContent='No recorded samples';badge.dataset.health='empty';return;}
    const latest=Math.max(...samples),age=Math.max(0,Date.now()-latest);
    // Warning, not a claim about why samples stopped. Core may have slow historian intervals.
    const expected=series.map(s=>state.historianConfigurations?.find?.(c=>c.configuration?.tagId===s.tagId)?.configuration?.sampleIntervalSeconds).filter(x=>Number.isFinite(Number(x))&&Number(x)>0);
    const threshold=expected.length?Math.max(45000,Math.max(...expected)*3000):6*60000;
    const stale=ui.live && age>threshold;
    const secs=Math.round(age/1000),lag=secs<60?secs+'s':Math.round(secs/60)+'m';
    badge.textContent=(stale?'No new samples · ':'Latest sample · ')+fmtTime(latest)+(ui.live?' · '+lag+' ago':'');
    badge.dataset.health=stale?'stale':'good';
    badge.title=stale?'Chart is still running, but Core Historian has no newer samples. Check tag quality and recording settings.':'';
  }

  function updateModeControls(){
    const custom=el('trendCustomRange');
    if(custom){custom.classList.toggle('hidden',ui.live);custom.querySelectorAll('input,button').forEach(n=>n.disabled=ui.live);}
    const apply=el('trendApplyRange');if(apply)apply.disabled=ui.live;
    const navigator=el('studioNavigator');if(navigator){navigator.style.cursor=ui.live?'default':'ew-resize';navigator.title=ui.live?'Pause LIVE to browse historical ranges.':'';}
  }

  function buildAxes(payload,w,h) {
    const margin={left:73,right:ui.selected.some(id=>ui.axes[id]==='right')?75:20,top:29,bottom:42};
    const plot={x:margin.left,y:margin.top,w:Math.max(80,w-margin.left-margin.right),h:h-margin.top-margin.bottom};
    const bounds={left:[],right:[]};
    payload.series.forEach(s=>{
      if(state.hiddenTrendTags.has(s.tagId))return;
      const values=bounds[ui.axes[s.tagId]==='right'?'right':'left'];
      (s.points||[]).filter(good).forEach(p=>{const v=Number(p.value);if(Number.isFinite(v))values.push(v);});
      // Include every configured threshold even when it lies outside the sampled range,
      // and even when threshold *lines* are hidden. This is an alarm-aware Y auto-scale.
      numericLimits(s.tagId).forEach(d=>values.push(d.threshold));
    });
    const axis={};for(const side of ['left','right']){
      const values=bounds[side];let min=values.length?Math.min(...values):0,max=values.length?Math.max(...values):1;
      if(min===max){const span=Math.max(Math.abs(min)*.1,1);min-=span;max+=span;}
      const pad=(max-min)*.1;
      axis[side]={min:min-pad,max:max+pad};
    }
    const from=Date.parse(payload.from),to=Date.parse(payload.to),span=Math.max(1,to-from);
    return {plot,axis,from,to,x:ts=>plot.x+(ts-from)/span*plot.w,y:(v,side='left')=>plot.y+(axis[side].max-v)/(axis[side].max-axis[side].min)*plot.h};
  }
  function canvasSetup(canvas,height) {
    if(!canvas)return null;
    const width=Math.max(300,Math.floor(canvas.getBoundingClientRect().width||canvas.parentElement?.clientWidth||800));
    const dpr=Math.min(2,window.devicePixelRatio||1);
    canvas.width=Math.round(width*dpr);canvas.height=Math.round(height*dpr);canvas.style.height=height+'px';
    const ctx=canvas.getContext('2d');if(!ctx)return null;ctx.setTransform(dpr,0,0,dpr,0,0);ctx.clearRect(0,0,width,height);
    return {ctx,width,height};
  }
  function line(ctx,x1,y1,x2,y2){ctx.beginPath();ctx.moveTo(x1,y1);ctx.lineTo(x2,y2);ctx.stroke();}
  function drawThresholdSegments(ctx,x1,v1,x2,v2,toY,limits,base) {
    if(x2<x1){[x1,x2]=[x2,x1];[v1,v2]=[v2,v1];}
    const fractions=[0,1];
    if(v1!==v2)for(const d of limits){const t=(d.threshold-v1)/(v2-v1);if(t>0&&t<1)fractions.push(t);}
    fractions.sort((a,b)=>a-b);
    const cuts=fractions.filter((v,i)=>i===0||v-fractions[i-1]>1e-9);
    for(let i=1;i<cuts.length;i++){
      const a=cuts[i-1],b=cuts[i],mid=(a+b)/2;
      ctx.strokeStyle=zoneColor(valueZone(v1+(v2-v1)*mid,limits),base);
      line(ctx,x1+(x2-x1)*a,toY(v1+(v2-v1)*a),x1+(x2-x1)*b,toY(v1+(v2-v1)*b));
    }
  }
  // Shape-preserving PCHIP: never invents overshoot between measured values.
  // Smoothing affects display only, never samples, alarms or CSV export.
  function smoothHermite(v0,v1,t0,t1,t2,t3,u) {
    const h=Math.max(1,t2-t1),s=(v1-v0)/h;
    const leftSlope=(v0-t0.v)/Math.max(1,t1-t0.t),rightSlope=(t3.v-v1)/Math.max(1,t3.t-t2);
    const tang0=(leftSlope*s>0)?(2*leftSlope*s/(leftSlope+s)):0;
    const tang1=(s*rightSlope>0)?(2*s*rightSlope/(s+rightSlope)):0;
    const u2=u*u,u3=u2*u;
    const v=(2*u3-3*u2+1)*v0+(u3-2*u2+u)*h*tang0+(-2*u3+3*u2)*v1+(u3-u2)*h*tang1;
    return Math.max(Math.min(v0,v1),Math.min(Math.max(v0,v1),v));
  }
  function drawSignal(ctx,points,mapper,limits,base,isDigital) {
    let runs=[],run=[],gapStart=null;
    for(const pt of points||[]){
      if(!good(pt)){
        if(run.length){runs.push(run);run=[];}
        if(gapStart===null)gapStart=mapper.x(ms(pt));
        continue;
      }
      const x=mapper.x(ms(pt)),v=Number(pt.value),y=mapper.y(v),t=ms(pt);
      if(gapStart!==null){ctx.save();ctx.fillStyle='rgba(241,106,134,.09)';ctx.fillRect(Math.min(gapStart,x),mapper.top,Math.max(3,Math.abs(x-gapStart)),mapper.height);ctx.restore();gapStart=null;}
      if(run.length&&t<=run[run.length-1].t)continue; // ignore duplicate / out-of-order timestamps
      run.push({x,y,v,t});
    }
    if(run.length)runs.push(run);
    ctx.lineWidth=2.4;ctx.lineJoin='round';ctx.lineCap='round';
    for(const r of runs){
      if(r.length===1){ctx.fillStyle=zoneColor(valueZone(r[0].v,limits),base);ctx.fillRect(r[0].x-1.4,r[0].y-1.4,2.8,2.8);continue;}
      for(let i=1;i<r.length;i++){
        const a=r[i-1],b=r[i];
        if(isDigital){ctx.strokeStyle=base;line(ctx,a.x,a.y,b.x,a.y);line(ctx,b.x,a.y,b.x,b.y);continue;}
        if(r.length<3){drawThresholdSegments(ctx,a.x,a.v,b.x,b.v,mapper.y,limits,base);continue;}
        const prev=r[i-2]||a,next=r[i+1]||b;
        const pieces=Math.max(3,Math.min(14,Math.ceil((b.x-a.x)/12)));
        let before=a;
        for(let k=1;k<=pieces;k++){
          const u=k/pieces,v=smoothHermite(a.v,b.v,{v:prev.v,t:prev.t},a.t,b.t,{v:next.v,t:next.t},u),x=a.x+(b.x-a.x)*u;
          drawThresholdSegments(ctx,before.x,before.v,x,v,mapper.y,limits,base);
          before={x,v};
        }
      }
    }
  }
  function axisLabel(n){const a=Math.abs(n);return a>=100000?Math.round(n).toLocaleString():a>=100?Number(n).toFixed(0):a>=10?Number(n).toFixed(1):Number(n).toFixed(2);}
  // Independent Tag lanes. In this mode a source's alarms never appear over another Tag's trace.
  // Compare mode keeps the original shared canvas, enabled only on explicit user selection.
  function drawSeparateCharts(payload,visible) {
    const root=el('studioSplitCharts');if(!root)return;
    const signature=visible.map(s=>s.tagId).join('|');
    if(ui.splitKey!==signature){
      ui.splitKey=signature;ui.laneModels.clear();
      root.innerHTML=visible.map(s=>{
        const tag=selectedTag(s.tagId);
        return `<section class="studio-lane" data-lane="${safe(s.tagId)}">
          <header class="studio-lane-head"><div><span class="studio-legend-color" style="background:${safe(color(s.tagId))}"></span><strong>${safe(displayName(s.tagId))}</strong><small>${safe(tag?.unit||'')}</small></div><span data-lane-last="${safe(s.tagId)}"></span></header>
          <div class="studio-lane-canvas-wrap"><canvas data-studio-lane="${safe(s.tagId)}" aria-label="${safe(displayName(s.tagId))} trend"></canvas><div class="studio-lane-tooltip hidden" data-lane-tip="${safe(s.tagId)}"></div></div>
          <div class="studio-lane-events" data-lane-events="${safe(s.tagId)}"></div>
          </section>`;
      }).join('');
      root.querySelectorAll('canvas[data-studio-lane]').forEach(canvas=>{
        const tagId=canvas.dataset.studioLane;
        canvas.addEventListener('pointerdown',e=>{
          const lane=ui.laneModels.get(tagId);if(!lane)return;
          const point=lanePosition(canvas,lane,e);if(!point)return;
          const hit=lane.flags.find(f=>Math.abs(f.x-point.x)<=13&&Math.abs(f.y-point.y)<=15);
          if(hit){selectAlarmEvent(eventKey(hit.event));return;}
          if(ui.activeCursor){ui.cursors[ui.activeCursor]=point.time;ui.activeCursor=null;syncUi();drawStudio();return;}
          ui.drag={laneId:tagId,start:point.x,startTime:point.time,end:point.x};canvas.setPointerCapture?.(e.pointerId);
        });
        canvas.addEventListener('pointermove',e=>{
          const lane=ui.laneModels.get(tagId);if(!lane)return;
          const point=lanePosition(canvas,lane,e);ui.hover=point?{...point,laneId:tagId}:null;
          canvas.style.cursor=point&&lane.flags.some(f=>Math.abs(f.x-point.x)<=13&&Math.abs(f.y-point.y)<=15)?'pointer':'crosshair';
          if(ui.drag?.laneId===tagId)ui.drag.end=point?.x??ui.drag.end;
          drawTrendChart();
        });
        canvas.addEventListener('pointerup',e=>{
          if(ui.drag?.laneId!==tagId)return;
          const lane=ui.laneModels.get(tagId),end=lane&&lanePosition(canvas,lane,e);
          const start=ui.drag.startTime,diff=Math.abs((end?.x??ui.drag.end)-ui.drag.start);ui.drag=null;
          if(end&&diff>16)setExplicitRange(Math.min(start,end.time),Math.max(start,end.time));
        });
        canvas.addEventListener('pointercancel',()=>{ui.drag=null;});
        canvas.addEventListener('pointerleave',()=>{if(!ui.drag){ui.hover=null;drawTrendChart();}});
        canvas.addEventListener('dblclick',e=>{e.preventDefault();const base=ui.navigatorPayload;if(base)setExplicitRange(Date.parse(base.from),Date.parse(base.to));});
      });
    }
    const canvasNodes=Array.from(root.querySelectorAll('canvas[data-studio-lane]'));
    const light=state.theme==='light';
    for(const s of visible){
      const id=s.tagId,canvas=canvasNodes.find(n=>n.dataset.studioLane===id);if(!canvas)continue;
      const pack=canvasSetup(canvas,window.innerWidth<680?260:326);if(!pack)continue;
      const {ctx,width,height}=pack;
      const m=buildAxes({...payload,series:[s]},width,height),pl=m.plot;
      const limits=numericLimits(id),base=color(id),axisSide=ui.axes[id]==='right'?'right':'left',axis=m.axis[axisSide];
      const lineColor=light?'rgba(73,112,136,.19)':'rgba(115,163,188,.16)';
      ctx.save();ctx.strokeStyle=lineColor;ctx.lineWidth=1;ctx.font='11px Segoe UI, sans-serif';
      for(let i=0;i<=4;i++){
        const y=pl.y+pl.h*i/4;line(ctx,pl.x,y,pl.x+pl.w,y);
        ctx.fillStyle=light?'#41657a':'#84aabd';ctx.textAlign='right';ctx.fillText(axisLabel(axis.max-(axis.max-axis.min)*i/4),pl.x-10,y+4);
      }
      for(let i=0;i<=4;i++){
        const x=pl.x+pl.w*i/4;line(ctx,x,pl.y,x,pl.y+pl.h);
        ctx.textAlign='center';ctx.fillText(new Date(m.from+(m.to-m.from)*i/4).toLocaleTimeString('en-US',{hour:'2-digit',minute:'2-digit'}),x,height-11);
      }ctx.restore();
      ctx.save();ctx.beginPath();ctx.rect(pl.x,pl.y,pl.w,pl.h);ctx.clip();
      if(el('studioBatchToggle')?.checked)for(const b of ui.events.batches){
        const from=Date.parse(b.startedAt),to=Date.parse(b.endedAt)||Date.now();if(to<m.from||from>m.to)continue;
        const x1=m.x(Math.max(from,m.from)),x2=m.x(Math.min(to,m.to));ctx.fillStyle=light?'rgba(12,146,119,.09)':'rgba(49,225,184,.055)';ctx.fillRect(x1,pl.y,Math.max(2,x2-x1),pl.h);
      }
      if(el('studioThresholdToggle')?.checked)for(const d of limits){
        const y=m.y(d.threshold,axisSide);if(y<pl.y||y>pl.y+pl.h)continue;
        ctx.strokeStyle=d.zone==='HIGH'?'#fa647d':'#de9e43';ctx.setLineDash([6,5]);ctx.lineWidth=1.2;line(ctx,pl.x,y,pl.x+pl.w,y);ctx.setLineDash([]);
        ctx.fillStyle=d.zone==='HIGH'?'#ff7e91':light?'#ac691b':'#f6c187';ctx.font='bold 11px Segoe UI';ctx.textAlign='right';ctx.fillText(d.zone+' '+d.threshold,pl.x+pl.w-7,Math.max(pl.y+12,y-6));
      }
      const digital=String(selectedTag(id)?.dataType||'').toLowerCase()==='bool';
      drawSignal(ctx,s.points,{x:m.x,y:v=>m.y(v,axisSide),top:pl.y,height:pl.h},limits,base,digital);
      const flags=[];
      if(el('studioAlarmToggle')?.checked && el('studioFlagToggle')?.checked){
        for(const event of relevantAlarms([id])){
          const t=Date.parse(event.activeAt);if(!Number.isFinite(t)||t<m.from||t>m.to)continue;
          const x=m.x(t),y=pl.y+13,selected=eventKey(event)===ui.selectedEventKey;
          ctx.strokeStyle=selected?'#b98133':'rgba(253,108,131,.68)';ctx.setLineDash([4,4]);line(ctx,x,pl.y+22,x,pl.y+pl.h);ctx.setLineDash([]);
          ctx.fillStyle=selected?'#f4c67c':'#ff879b';ctx.beginPath();ctx.moveTo(x-6,pl.y+6);ctx.lineTo(x+6,pl.y+6);ctx.lineTo(x,pl.y+18);ctx.closePath();ctx.fill();
          flags.push({x,y,event});
        }
      }
      for(const [key,time] of Object.entries(ui.cursors)){
        if(!Number.isFinite(time)||time<m.from||time>m.to)continue;
        const x=m.x(time);ctx.strokeStyle=key==='A'?'#f8c773':'#ba9cfe';ctx.lineWidth=1.5;ctx.setLineDash([5,4]);line(ctx,x,pl.y,x,pl.y+pl.h);ctx.setLineDash([]);
        ctx.fillStyle=key==='A'?'#f8c773':'#ba9cfe';ctx.fillRect(x-10,pl.y,20,16);ctx.fillStyle='#092236';ctx.font='bold 11px Segoe UI';ctx.textAlign='center';ctx.fillText(key,x,pl.y+12);
      }
      if(ui.hover && ui.hover.time>=m.from && ui.hover.time<=m.to){
        const x=m.x(ui.hover.time);ctx.strokeStyle=light?'#246a84':'rgba(208,240,251,.65)';ctx.setLineDash([2,4]);line(ctx,x,pl.y,x,pl.y+pl.h);ctx.setLineDash([]);
      }
      ctx.restore();
      ui.laneModels.set(id,{m,flags});
      const last=[...(s.points||[])].reverse().find(good),zone=last?valueZone(last.value,limits):'UNKNOWN';
      const head=Array.from(root.querySelectorAll('[data-lane-last]')).find(x=>x.dataset.laneLast===id);
      if(head){head.textContent=zone+' · '+valueText(id,last?.value);head.dataset.zone=zone;}
      const tip=Array.from(root.querySelectorAll('[data-lane-tip]')).find(x=>x.dataset.laneTip===id);
      if(tip){if(ui.hover?.laneId!==id){tip.classList.add('hidden');}else{
        const nearest=nearestPoint(s.points,ui.hover.time);tip.innerHTML='<strong>'+safe(fmtTime(ui.hover.time))+'</strong><span>'+safe(good(nearest)?valueText(id,nearest.value):String(nearest?.quality||'NO DATA'))+'</span>';
        tip.style.left=Math.min(ui.hover.x+14,Math.max(8,pl.x+pl.w-195))+'px';tip.style.top=Math.min(ui.hover.y+8,pl.y+pl.h-60)+'px';tip.classList.remove('hidden');
      }}
      const eventBox=Array.from(root.querySelectorAll('[data-lane-events]')).find(x=>x.dataset.laneEvents===id);
      const related=el('studioAlarmToggle')?.checked?relevantAlarms([id]):[];
      if(eventBox){const eventKeyNow=ui.requestId+'|'+ui.selectedEventKey+'|'+related.length+'|'+String(el('studioSystemAlarmToggle')?.checked)+'|'+String(el('studioAlarmToggle')?.checked);
        if(eventBox.dataset.renderKey!==eventKeyNow){eventBox.dataset.renderKey=eventKeyNow;
          eventBox.innerHTML=related.length?related.slice(0,8).map(a=>`<button type="button" class="studio-lane-event ${ui.selectedEventKey===eventKey(a)?'selected':''}" data-event-key="${safe(eventKey(a))}">⚑ ${safe(a.text||'Alarm')} · ${safe(fmtTime(Date.parse(a.activeAt)))}</button>`).join(''):`<span>${'No alarm events for this Tag in the range.'}</span>`;
          eventBox.querySelectorAll('button[data-event-key]').forEach(b=>b.addEventListener('click',()=>selectAlarmEvent(b.dataset.eventKey)));
        }
      }
    }
  }
  function lanePosition(canvas,model,event){
    if(!model?.m)return null;
    const rect=canvas.getBoundingClientRect(),width=canvas.clientWidth||rect.width,height=canvas.clientHeight||rect.height;
    const x=(event.clientX-rect.left)*width/Math.max(1,rect.width),y=(event.clientY-rect.top)*height/Math.max(1,rect.height),m=model.m;
    if(x<m.plot.x||x>m.plot.x+m.plot.w||y<m.plot.y||y>m.plot.y+m.plot.h)return null;
    return {x,y,time:m.from+(x-m.plot.x)/m.plot.w*(m.to-m.from)};
  }

  function drawStudio() {
    const payload=state.trendPayload;
    const compare=ui.compare&&ui.selected.filter(canPlot).length>=2;
    el('studioSplitCharts')?.classList.remove('hidden');
    el('studioChartShell')?.classList.toggle('hidden',!compare);
    el('studioCompareCaption')?.classList.toggle('hidden',!compare);
    el('trendChartTitle').textContent=title();renderCards(payload);
    const visible=payload?.series?.filter(s=>ui.selected.includes(s.tagId)&&!state.hiddenTrendTags.has(s.tagId))||[];
    const hasPoints=visible.some(s=>s.points?.length);
    el('trendChartEmpty').classList.toggle('hidden',!!hasPoints);
    if(!payload||!hasPoints){ui.splitKey=null;ui.laneModels.clear();if(el('studioSplitCharts'))el('studioSplitCharts').innerHTML='';ui.flagHits=[];const badge=el('studioSampleHealth');if(badge){badge.textContent=studioText('noSamples');badge.dataset.health='empty';}state.trendChartModel=null;el('studioPointCount').textContent='0 '+studioText('pointCount');el('studioTimeText').textContent=ui.selected.length?'No Historian samples yet in this time window.':'Select a recorded Tag or open a saved view.';updateCursorResult();drawNavigator();return;}
    const fromMs=Date.parse(payload.from),toMs=Date.parse(payload.to);
    const count=visible.reduce((n,s)=>n+(s.points?.length||0),0);
    drawSeparateCharts(payload,visible);
    if(!compare){state.trendChartModel=null;ui.flagHits=[];el('trendTooltip')?.classList.add('hidden');}
    else { /* shared graph is drawn after always-visible individual charts */ }
    const canvas=el('trendCanvas');
    const pack=compare?canvasSetup(canvas,window.innerWidth<680?345:446):null;
    const {ctx,width,height}=pack||{};
    const m=compare?buildAxes(payload,width,height):null,p=m?.plot;
    if(compare)state.trendChartModel={left:p.x,top:p.y,plotW:p.w,plotH:p.h,fromMs:m.from,toMs:m.to,min:m.axis.left.min,max:m.axis.left.max};
    el('studioTimeText').textContent=fmtTime(fromMs)+'  →  '+fmtTime(toMs)+'  ·  '+(ui.live?'● LIVE · rolling window':'Ⅱ PAUSED · fixed range');
    el('studioPointCount').textContent=count.toLocaleString('en-US')+' '+studioText('pointCount');
    updateSampleHealth(visible,toMs);
    if(!compare){updateCursorResult();drawNavigator();return;}
    ctx.fillStyle=state.theme==='light'?'#517182':'#728fa2';ctx.font='11px Segoe UI, sans-serif';
    ctx.save();ctx.strokeStyle=state.theme==='light'?'rgba(58,103,131,.13)':'rgba(100,151,179,.13)';ctx.lineWidth=1;
    for(let i=0;i<=5;i++){
      const y=p.y+(p.h*i/5);line(ctx,p.x,y,p.x+p.w,y);
      const vl=m.axis.left.max-(m.axis.left.max-m.axis.left.min)*i/5;
      ctx.fillStyle=state.theme==='light'?'#52758b':'#7196a9';ctx.textAlign='right';ctx.fillText(axisLabel(vl),p.x-13,y+4);
      if(ui.selected.some(id=>ui.axes[id]==='right')){const vr=m.axis.right.max-(m.axis.right.max-m.axis.right.min)*i/5;ctx.textAlign='left';ctx.fillText(axisLabel(vr),p.x+p.w+12,y+4);}
    }
    for(let i=0;i<=5;i++){
      const x=p.x+p.w*i/5;line(ctx,x,p.y,x,p.y+p.h);
      ctx.textAlign='center';const stamp=m.from+(m.to-m.from)*i/5;ctx.fillText(new Date(stamp).toLocaleString('en-US',{month:'2-digit',day:'2-digit',hour:'2-digit',minute:'2-digit'}),x,height-12);
    }
    ctx.restore();
    ctx.save();ctx.beginPath();ctx.rect(p.x,p.y,p.w,p.h);ctx.clip();
    // Lot windows are shaded behind signals and can overlap adjacent batches.
    if(el('studioBatchToggle')?.checked){ui.events.batches.forEach(b=>{
      const from=Date.parse(b.startedAt),to=Date.parse(b.endedAt)||Date.now();if(to<m.from||from>m.to)return;
      const x1=m.x(Math.max(from,m.from)),x2=m.x(Math.min(to,m.to));
      ctx.fillStyle=state.theme==='light'?'rgba(37,178,154,.065)':'rgba(49,225,184,.055)';ctx.fillRect(x1,p.y,Math.max(2,x2-x1),p.h);
      ctx.fillStyle='rgba(63,215,177,.62)';ctx.font='10px Segoe UI';ctx.textAlign='left';ctx.fillText((b.batchNo||'LOT').slice(0,22),Math.max(p.x+3,x1+6),p.y+17);
    });}
    // Numeric threshold overlays can be hidden independently of alarm flags.
    if(el('studioThresholdToggle')?.checked)for(const id of ui.selected){if(state.hiddenTrendTags.has(id))continue;
      for(const d of numericLimits(id)){
        const side=ui.axes[id]==='right'?'right':'left',y=m.y(d.threshold,side);if(y<p.y||y>p.y+p.h)continue;
        ctx.strokeStyle=d.zone==='HIGH'?'rgba(255,108,130,.8)':'rgba(246,175,92,.8)';ctx.setLineDash([6,5]);ctx.lineWidth=1.2;line(ctx,p.x,y,p.x+p.w,y);ctx.setLineDash([]);
        ctx.font='bold 10px Segoe UI';ctx.fillStyle=d.zone==='HIGH'?(state.theme==='light'?'#be3553':'#ff9aab'):(state.theme==='light'?'#995b0b':'#f6bf80');ctx.textAlign='right';
        ctx.fillText((d.zone+' '+d.threshold+' · '+(d.text||'')).slice(0,42),p.x+p.w-8,Math.max(p.y+12,y-6));
      }
    }
    // GOOD samples only: threshold crossing is split precisely where it happens;
    // BAD/STALE points break the line and are never connected artificially.
    for(const s of visible){
      const side=ui.axes[s.tagId]==='right'?'right':'left',digital=String(selectedTag(s.tagId)?.dataType||'').toLowerCase()==='bool';
      drawSignal(ctx,s.points,{x:m.x,y:v=>m.y(v,side),top:p.y,height:p.h},numericLimits(s.tagId),color(s.tagId),digital);
    }
    ui.flagHits=[];
    if(el('studioAlarmToggle')?.checked && el('studioFlagToggle')?.checked){
      const grouped=new Map();
      for(const a of relevantAlarms(ui.selected)){const t=Date.parse(a.activeAt);if(!Number.isFinite(t)||t<m.from||t>m.to)continue;
        const bucket=Math.round(m.x(t)/12);if(!grouped.has(bucket))grouped.set(bucket,[]);grouped.get(bucket).push(a);
      }
      for(const events of grouped.values()){
        const a=events[0],time=Date.parse(a.activeAt),x=m.x(time),selected=events.some(e=>eventKey(e)===ui.selectedEventKey);
        ctx.save();ctx.strokeStyle=selected?'#ffe6a1':'rgba(255,105,127,.55)';ctx.setLineDash([3,5]);line(ctx,x,p.y+24,x,p.y+p.h);ctx.setLineDash([]);
        ctx.fillStyle=selected?'#ffe6a1':'#ff879b';ctx.beginPath();ctx.moveTo(x-7,p.y+7);ctx.lineTo(x+7,p.y+7);ctx.lineTo(x,p.y+20);ctx.closePath();ctx.fill();
        if(events.length>1){ctx.fillStyle='#0b2333';ctx.font='bold 10px Segoe UI';ctx.textAlign='center';ctx.fillText(events.length,x,p.y+5);}
        if(selected){ctx.strokeStyle='#ffe6a1';ctx.lineWidth=1.5;ctx.strokeRect(x-10,p.y+2,20,22);}
        ui.flagHits.push({x,y:p.y+12,events});ctx.restore();
      }
    }
    for(const [key,timestamp] of Object.entries(ui.cursors)){
      if(!Number.isFinite(timestamp)||timestamp<m.from||timestamp>m.to)continue;const x=m.x(timestamp);
      ctx.strokeStyle=key==='A'?'#f8c773':'#ba9cfe';ctx.lineWidth=1.6;ctx.setLineDash([6,3]);line(ctx,x,p.y,x,p.y+p.h);ctx.setLineDash([]);
      ctx.fillStyle=key==='A'?'#f8c773':'#ba9cfe';ctx.fillRect(x-11,p.y,22,17);ctx.fillStyle='#0b2333';ctx.font='bold 11px Segoe UI';ctx.textAlign='center';ctx.fillText(key,x,p.y+12);
    }
    if(ui.hover && ui.hover.time>=m.from && ui.hover.time<=m.to){
      const x=m.x(ui.hover.time);ctx.strokeStyle='rgba(212,244,253,.69)';ctx.lineWidth=1;ctx.setLineDash([3,4]);line(ctx,x,p.y,x,p.y+p.h);ctx.setLineDash([]);
      for(const s of visible){const pt=nearestPoint(s.points,ui.hover.time);if(!good(pt))continue;
        const side=ui.axes[s.tagId]==='right'?'right':'left',py=m.y(+pt.value,side),px=m.x(ms(pt));if(Math.abs(px-x)>p.w*.12)continue;
        ctx.beginPath();ctx.fillStyle=color(s.tagId);ctx.strokeStyle='#071b2a';ctx.lineWidth=2;ctx.arc(px,py,4,0,Math.PI*2);ctx.fill();ctx.stroke();
      }
    }
    ctx.restore();updateTooltip(m);updateCursorResult();drawNavigator();
  }

  function nearestPoint(points,at){if(!points?.length)return null;let l=0,r=points.length-1;
    while(l<r){const mid=Math.floor((l+r)/2);if(ms(points[mid])<at)l=mid+1;else r=mid;}
    return l>0&&Math.abs(ms(points[l-1])-at)<Math.abs(ms(points[l])-at)?points[l-1]:points[l];
  }
  function updateTooltip(m) {
    const tip=el('trendTooltip');if(!tip)return;
    if(!ui.hover){tip.classList.add('hidden');return;}
    const rows=(state.trendPayload?.series||[]).filter(s=>ui.selected.includes(s.tagId)&&!state.hiddenTrendTags.has(s.tagId)).map(s=>{
      const pt=nearestPoint(s.points,ui.hover.time),tag=selectedTag(s.tagId);
      return `<div style="display:flex;gap:9px;align-items:center;justify-content:space-between;margin:8px 0"><span style="display:flex;align-items:center;gap:7px;color:#accddb"><i style="display:inline-block;width:7px;height:7px;background:${safe(color(s.tagId))};border-radius:50%"></i>${safe(tag?.name||s.tagId)}</span><b style="color:${good(pt)?'#eaf7fb':'#ff9b9c'}">${safe(good(pt)?valueText(s.tagId,pt.value):String(pt?.quality||'NO DATA').toUpperCase())}</b></div>`;
    }).join('');
    tip.innerHTML=`<div style="color:#79e0ed;font-weight:750">${safe(fmtTime(ui.hover.time))}</div>${rows}`;
    tip.style.left=Math.min(ui.hover.x+14,Math.max(8,m.plot.x+m.plot.w-245))+'px';tip.style.top=Math.min(ui.hover.y+16,m.plot.y+m.plot.h-165)+'px';tip.classList.remove('hidden');
  }
  function updateCursorResult() {
    const box=el('studioCursorResult'),bar=el('studioCompareBar');if(!box||!bar)return;
    const A=ui.cursors.A,B=ui.cursors.B;
    const heading=`<div class="studio-compare-title"><b>${'A / B comparison'}</b><button class="studioResetCursors" type="button">${'Clear cursors'} ×</button></div>`;
    if(A==null&&B==null){
      const hint=ui.activeCursor?ui.activeCursor+' selected: click the chart to place it.'
        :'Click A and place it on the chart, then place B. Time and value differences appear here.';
      box.textContent=hint;bar.classList.toggle('hidden',!ui.activeCursor);if(ui.activeCursor)bar.innerHTML=heading+'<p>'+safe(hint)+'</p>';
      bar.querySelector('.studioResetCursors')?.addEventListener('click',clearCursors);return;
    }
    let html=heading+`<div class="studio-ab-times"><span><b class="cursor-a">A</b> ${A==null?'—':safe(fmtTime(A))}</span><span><b class="cursor-b">B</b> ${B==null?'—':safe(fmtTime(B))}</span></div>`;
    if(A!=null&&B!=null){const dt=B-A,absolute=Math.abs(dt),sign=dt<0?'-':'',h=Math.floor(absolute/3600000),mi=Math.floor(absolute%3600000/60000),sec=Math.floor(absolute%60000/1000);
      html+=`<div class="studio-delta-time">Δt ${sign}${h?h+'h ':''}${mi}m ${sec}s</div>`;
      html+='<div class="studio-ab-table"><div class="studio-ab-header"><span>TAG</span><span>A</span><span>B</span><span>Δ B−A</span></div>';
      for(const s of state.trendPayload?.series||[]){if(!ui.selected.includes(s.tagId)||state.hiddenTrendTags.has(s.tagId))continue;
        const a=nearestPoint(s.points,A),b=nearestPoint(s.points,B),valid=good(a)&&good(b);
        const delta=valid?Number(b.value)-Number(a.value):null;
        html+=`<div class="studio-ab-row"><span>${safe(selectedTag(s.tagId)?.name||s.tagId)}</span><span>${safe(good(a)?valueText(s.tagId,a.value):'—')}</span><span>${safe(good(b)?valueText(s.tagId,b.value):'—')}</span><strong>${safe(delta==null?'—':(delta>=0?'+':'')+valueText(s.tagId,delta))}</strong></div>`;
      }
      html+='</div>';
    }else html+=`<p>${'Choose the other cursor and click the chart.'}</p>`;
    box.innerHTML=html;bar.innerHTML=html;bar.classList.remove('hidden');
    [box,bar].forEach(area=>area.querySelector('.studioResetCursors')?.addEventListener('click',clearCursors));
  }
  function clearCursors(){ui.cursors={A:null,B:null};ui.activeCursor=null;syncUi();drawStudio();}

  function drawNavigator() {
    const navKey=ui.requestId+'|'+ui.range?.from+'|'+ui.range?.to+'|'+ui.selected.join(',');
    if(ui.navKey===navKey)return;ui.navKey=navKey;
    const nav=el('studioNavigator'),set=canvasSetup(nav,58);if(!set)return;const {ctx,width,height}=set;
    const payload=ui.navigatorPayload||state.trendPayload;if(!payload?.series?.length)return;
    const a=Date.parse(payload.from),b=Date.parse(payload.to),span=Math.max(1,b-a),pad=6;
    const s=payload.series.find(x=>x.points?.some(good));if(!s)return;const vals=s.points.filter(good).map(x=>+x.value);if(!vals.length)return;
    const min=Math.min(...vals),max=Math.max(...vals),denom=Math.max(1e-9,max-min);
    ctx.save();ctx.strokeStyle='rgba(51,210,228,.7)';ctx.lineWidth=1.35;ctx.beginPath();let began=false;
    for(const p of s.points){if(!good(p)){began=false;continue;}
      const x=pad+(ms(p)-a)/span*(width-2*pad),y=8+(max-Number(p.value))/denom*(height-17);
      if(!began){ctx.moveTo(x,y);began=true;}else ctx.lineTo(x,y);
    }ctx.stroke();
    const range=ui.range;if(range){const x1=pad+Math.max(0,(range.from-a)/span)*(width-2*pad),x2=pad+Math.min(1,(range.to-a)/span)*(width-2*pad);
      ctx.fillStyle='rgba(66,196,218,.1)';ctx.fillRect(x1,0,Math.max(3,x2-x1),height);ctx.strokeStyle='rgba(105,239,252,.65)';ctx.strokeRect(x1+.5,.5,Math.max(2,x2-x1-1),height-1);
      ctx.fillStyle='rgba(1,11,24,.38)';ctx.fillRect(0,0,Math.max(0,x1),height);ctx.fillRect(x2,0,Math.max(0,width-x2),height);
    }ctx.restore();
  }

  function chartPosition(event){const canvas=el('trendCanvas'),model=state.trendChartModel;if(!canvas||!model)return null;
    const rect=canvas.getBoundingClientRect(),x=(event.clientX-rect.left)*(canvas.clientWidth/Math.max(1,rect.width)),y=(event.clientY-rect.top)*(canvas.clientHeight/Math.max(1,rect.height));
    if(x<model.left||x>model.left+model.plotW||y<model.top||y>model.top+model.plotH)return null;
    const time=model.fromMs+(x-model.left)/model.plotW*(model.toMs-model.fromMs);return {x,y,time};
  }
  function setExplicitRange(from,to) {
    if(!Number.isFinite(from)||!Number.isFinite(to)||to-from<1500)return;
    el('trendRange').value='custom';el('trendRange').dataset.userChanged='1';el('trendFrom').value=dateToLocalInput(new Date(from));el('trendTo').value=dateToLocalInput(new Date(to));
    ui.live=false;ui.range={from,to};syncUi();updateModeControls();syncTrendRangePills();void refreshStudio(true);
  }
  function syncUi(){const button=el('studioLive');button?.classList.toggle('active',ui.live);
    if(button){button.innerHTML='<i></i> '+studioText(ui.live?'live':'paused');button.setAttribute('aria-pressed',String(ui.live));}
    updateModeControls();
    for(const k of ['A','B']){const button=el('studioCursor'+k);button?.classList.toggle('active',ui.activeCursor===k);button?.setAttribute('aria-pressed',String(ui.activeCursor===k));}
    updateCursorResult();
  }

  async function exportCsv(event) {
    event?.preventDefault();event?.stopImmediatePropagation();
    const payload=state.trendPayload;if(!payload){showToast('Select signals first.');return;}
    // Saved views use the Core RAW CSV export (not decimated chart samples).
    if(state.selectedTrendId && selectedTrend() && ui.selected.join()===selectedTrend().tagIds.join()){
      const start=new Date(payload.from).toISOString(),end=new Date(payload.to).toISOString();
      const url=`/api/historian/export.csv?trendId=${encodeURIComponent(state.selectedTrendId)}&from=${encodeURIComponent(start)}&to=${encodeURIComponent(end)}`;
      try{const response=await fetch(url);if(!response.ok)throw Error('CSV export '+response.status);
        const blob=await response.blob();saveBlob(blob,'PROGNODE_TrendStudio_RAW.csv');return;
      }catch(e){showToast(e.message);return;}
    }
    // Ad-hoc views have no saved trend ID: export only displayed/downsampled points.
    const rows=['Timestamp UTC;Tag;Value;Quality;Export detail'];
    for(const s of payload.series||[]){const tag=selectedTag(s.tagId);for(const p of s.points||[]){const q=x=>'"'+String(x??'').replaceAll('"','""')+'"';rows.push([q(new Date(p.timestamp).toISOString().slice(0,19).replace('T',' ')),q(tag?.name||s.tagId),p.value??'',q(p.quality),q('displayed points (downsampled)')].join(';'));}}
    saveBlob(new Blob(['\ufeff'+rows.join('\r\n')],{type:'text/csv;charset=utf-8'}),'PROGNODE_TrendStudio_VISIBLE.csv');
    showToast('Ad-hoc view: exported plotted samples, not full raw historian.');
  }
  function saveBlob(blob,filename){const a=document.createElement('a'),url=URL.createObjectURL(blob);a.href=url;a.download=filename;document.body.appendChild(a);a.click();a.remove();setTimeout(()=>URL.revokeObjectURL(url),1000);}
  function exportPng(event){event?.preventDefault();event?.stopImmediatePropagation();
    if(!state.trendPayload){showToast('Select a signal first.');return;}
    const charts=Array.from(document.querySelectorAll('#studioSplitCharts canvas[data-studio-lane]'));
    if(!charts.length){showToast('No chart to export.');return;}
    const width=Math.max(...charts.map(c=>c.width)),height=charts.reduce((n,c)=>n+c.height+35,28),cv=document.createElement('canvas');
    cv.width=width;cv.height=height;const ctx=cv.getContext('2d');if(!ctx)return;
    ctx.fillStyle=state.theme==='light'?'#fff':'#0b1d2d';ctx.fillRect(0,0,width,height);ctx.font='bold 20px Segoe UI';ctx.fillStyle=state.theme==='light'?'#173f53':'#dceef7';ctx.fillText('PROGNODE Trend Studio',22,27);let y=35;
    for(const c of charts){const label=displayName(c.dataset.studioLane);ctx.fillStyle=state.theme==='light'?'#134a61':'#a9dfea';ctx.font='bold 14px Segoe UI';ctx.fillText(label,22,y+13);ctx.drawImage(c,0,y+19);y+=c.height+35;}
    cv.toBlob(blob=>{if(blob)saveBlob(blob,'PROGNODE_TrendStudio_'+new Date().toISOString().slice(0,10)+'.png');},'image/png');
  }

  function install() {
    if(ui.initialized||!el('page-trends'))return;ui.initialized=true;
    // Legacy shell retains its routes, saved-view editor and access control. We replace
    // only the chart/query renderer and attach Studio-specific interactions.
    drawTrendChart=()=>{if(ui.drawQueued)return;ui.drawQueued=true;requestAnimationFrame(()=>{ui.drawQueued=false;drawStudio();});};
    refreshTrendPoints=()=>refreshStudio(false);
    // The old interval captures refreshTrendPoints at startup, so it now invokes Studio.
    el('studioTagSearch')?.addEventListener('input',renderTagPool);
    el('studioCompareCheck')?.addEventListener('change',event=>{
      ui.compare=Boolean(event.target.checked);ui.hover=null;syncViewMode();drawStudio();
      if(ui.compare&&ui.selected.length<2)showToast('Choose two or more Tags to compare.');
    });
    el('studioGoHistorian')?.addEventListener('click',()=>navigate('historian'));
    window.addEventListener('prognode:languagechange',applyStudioLanguage);
    el('trendsAddTrend')?.addEventListener('click',event=>{
      event.preventDefault();event.stopImmediatePropagation();
      openAddTrendModal();
      if(!el('trendModal')?.classList.contains('hidden') && ui.selected.length)
        populateTrendTagChecklist(ui.selected,ui.selected.map(id=>color(id)));
    },true);
    el('studioClearTags')?.addEventListener('click',async()=>{
      state.selectedTrendId=null;ui.viewId=null;ui.selected=[];state.hiddenTrendTags.clear();state.trendPayload=null;ui.navigatorPayload=null;ui.splitKey=null;ui.events={alarms:[],batches:[],definitions:[]};ui.selectedEventKey=null;ui.flagHits=[];renderTrendList();renderTagPool();updateCounts();drawStudio();
    });
    el('studioLive')?.addEventListener('click',()=>{
      ui.live=!ui.live;
      if(!ui.live){
        // Freeze the exact visible window; only now enable From / To editing.
        const frozen=ui.range||{from:Date.now()-ui.liveWindowMinutes*60000,to:Date.now()};
        el('trendRange').value='custom';el('trendRange').dataset.userChanged='1';
        el('trendFrom').value=dateToLocalInput(new Date(frozen.from));
        el('trendTo').value=dateToLocalInput(new Date(frozen.to));
      }else{
        el('trendRange').value=String([5,15,60,480,1440,10080].includes(ui.liveWindowMinutes)?ui.liveWindowMinutes:60);
        if(![5,15,60,480,1440,10080].includes(ui.liveWindowMinutes))ui.liveWindowMinutes=60;
        ui.hover=null;ui.navigatorPayload=null;
      }
      syncUi();syncTrendRangePills();if(ui.live)void refreshStudio(true);else drawStudio();
    });
    el('studioRefresh')?.addEventListener('click',()=>refreshStudio(true));
    el('studioZoomOut')?.addEventListener('click',()=>{const range=ui.range;if(!range)return;const mid=(range.from+range.to)/2,span=(range.to-range.from)*2;setExplicitRange(mid-span/2,mid+span/2);});
    el('trendAutoFit')?.addEventListener('click',event=>{event.preventDefault();event.stopImmediatePropagation();
      const base=ui.navigatorPayload;if(base && ui.range && (ui.range.from>Date.parse(base.from)||ui.range.to<Date.parse(base.to))){setExplicitRange(Date.parse(base.from),Date.parse(base.to));return;}
      el('trendRange').value='60';ui.live=true;ui.liveWindowMinutes=60;syncUi();syncTrendRangePills();void refreshStudio(true);
    },true);
    for(const key of ['A','B'])el('studioCursor'+key)?.addEventListener('click',()=>{
      ui.activeCursor=ui.activeCursor===key?null:key;
      // Freeze the current LIVE frame for a reliable A/B time comparison.
      if(ui.activeCursor&&ui.live){ui.live=false;const frozen=ui.range||{from:Date.now()-ui.liveWindowMinutes*60000,to:Date.now()};
        el('trendRange').value='custom';el('trendRange').dataset.userChanged='1';
        el('trendFrom').value=dateToLocalInput(new Date(frozen.from));el('trendTo').value=dateToLocalInput(new Date(frozen.to));}
      syncUi();drawStudio();
    });
    el('trendExportCsv')?.addEventListener('click',exportCsv,true);
    el('trendExportPng')?.addEventListener('click',exportPng,true);
    for(const id of ['studioAlarmToggle','studioBatchToggle','studioFlagToggle','studioThresholdToggle','studioSystemAlarmToggle'])el(id)?.addEventListener('change',()=>{renderContext();drawStudio();});
    const applyPreset=minutes=>{
      if(!Number.isFinite(minutes)||minutes<=0)return;
      ui.liveWindowMinutes=minutes;el('trendRange').value=String(minutes);el('trendRange').dataset.userChanged='1';
      ui.cursors={A:null,B:null};ui.navigatorPayload=null;ui.hover=null;ui.nextAllowed=0;
      if(!ui.live){const anchor=ui.range?.to??Date.now();ui.range={from:anchor-minutes*60000,to:anchor};}
      syncUi();syncTrendRangePills();void refreshStudio(true);
    };
    el('trendRangePills')?.addEventListener('click',e=>{
      const button=e.target.closest('[data-range-value]');if(!button)return;
      // The legacy shell also registers bubble handlers. This capture handler is authoritative.
      e.preventDefault();e.stopImmediatePropagation();
      const chosen=button.dataset.rangeValue;
      if(chosen==='custom'){
        if(ui.live){showToast('Pause LIVE before selecting custom dates.');return;}
        el('trendRange').value='custom';el('trendRange').dataset.userChanged='1';
        if(ui.range){el('trendFrom').value=dateToLocalInput(new Date(ui.range.from));el('trendTo').value=dateToLocalInput(new Date(ui.range.to));}
        syncUi();syncTrendRangePills();return;
      }
      applyPreset(Number(chosen));
    },true);
    el('trendRange')?.addEventListener('change',e=>{
      e.stopImmediatePropagation();const value=e.target.value;
      if(value==='custom'){
        if(ui.live){showToast('Pause LIVE first.');el('trendRange').value=String(ui.liveWindowMinutes);return;}
        syncUi();syncTrendRangePills();return;
      }
      applyPreset(Number(value));
    },true);
    el('trendApplyRange')?.addEventListener('click',e=>{
      e.preventDefault();e.stopImmediatePropagation();if(ui.live)return;
      const from=new Date(el('trendFrom').value).getTime(),to=new Date(el('trendTo').value).getTime();
      if(!Number.isFinite(from)||!Number.isFinite(to)||to-from<1500){showToast('Choose a valid date range.');return;}
      setExplicitRange(from,to);
    },true);
    const chart=el('trendCanvas');
    chart?.addEventListener('pointerdown',e=>{
      const pos=chartPosition(e);if(!pos)return;
      // A chart flag is a real interactive event: clicking opens details, never removes it.
      if(el('studioAlarmToggle')?.checked&&el('studioFlagToggle')?.checked){
        const hit=ui.flagHits.find(h=>Math.abs(pos.x-h.x)<=13&&Math.abs(pos.y-h.y)<=15);
        if(hit){selectAlarmEvent(eventKey(hit.events[0]));return;}
      }
      if(ui.activeCursor){ui.cursors[ui.activeCursor]=pos.time;ui.activeCursor=null;syncUi();drawStudio();return;}
      ui.drag={start:pos.x,end:pos.x,startTime:pos.time};chart.setPointerCapture?.(e.pointerId);
    });
    chart?.addEventListener('pointermove',e=>{
      const pos=chartPosition(e);
      ui.hover=pos;
      const flagHover=pos&&ui.flagHits.some(h=>Math.abs(pos.x-h.x)<=13&&Math.abs(pos.y-h.y)<=15);
      chart.style.cursor=flagHover?'pointer':(ui.activeCursor?'crosshair':'crosshair');
      if(ui.drag){ui.drag.end=pos?.x??ui.drag.end;const d=el('studioDragBox');if(d){const x1=Math.min(ui.drag.start,ui.drag.end),x2=Math.max(ui.drag.start,ui.drag.end);d.style.left=x1+'px';d.style.width=Math.max(1,x2-x1)+'px';d.classList.toggle('hidden',Math.abs(x2-x1)<4);}}
      if(state.trendPayload)drawTrendChart();
    });
    chart?.addEventListener('pointerup',e=>{
      if(!ui.drag)return;const end=chartPosition(e),start=ui.drag.startTime;
      el('studioDragBox')?.classList.add('hidden');const dx=Math.abs((end?.x??ui.drag.end)-ui.drag.start);ui.drag=null;
      if(dx>16 && end)setExplicitRange(Math.min(start,end.time),Math.max(start,end.time));
    });
    chart?.addEventListener('pointercancel',()=>{ui.drag=null;el('studioDragBox')?.classList.add('hidden');});
    chart?.addEventListener('pointerleave',()=>{if(!ui.drag){ui.hover=null;drawStudio();}});
    // Intentionally no wheel listener: mouse wheel must scroll the page, never zoom the chart.
    chart?.addEventListener('dblclick',e=>{e.preventDefault();const base=ui.navigatorPayload;
      if(base)setExplicitRange(Date.parse(base.from),Date.parse(base.to));else if(ui.live){ui.liveWindowMinutes=60;el('trendRange').value='60';syncUi();void refreshStudio(true);}
    });
    el('studioNavigator')?.addEventListener('click',e=>{
      if(ui.live){showToast('Pause LIVE to navigate historical time.');return;}
      const base=ui.navigatorPayload||state.trendPayload;if(!base)return;
      const canvas=e.currentTarget,rect=canvas.getBoundingClientRect(),ratio=Math.max(0,Math.min(1,(e.clientX-rect.left)/Math.max(1,rect.width)));
      const start=Date.parse(base.from),end=Date.parse(base.to),span=ui.range?ui.range.to-ui.range.from:(end-start)*.33;
      const at=start+(end-start)*ratio;setExplicitRange(Math.max(start,at-span/2),Math.min(end,at+span/2));
    });
    const priorRender=renderTrendList;
    renderTrendList=function(){priorRender();setSelectedFromSaved();renderTagPool();updateCounts();};
    window.addEventListener('resize',()=>drawStudio());
    const monitor=new MutationObserver(()=>{if(state.page==='trends'&&state.selectedTrendId) setSelectedFromSaved();});
    monitor.observe(el('trendList'),{childList:true});
    applyStudioLanguage();renderTagPool();syncViewMode();syncUi();drawStudio();
  }
  install();
  window.addEventListener('pageshow',()=>{if(!ui.initialized)install();});
  // Expose pure helpers for local smoke tests without PLC data.
  ui._test={nearestPoint,good,numericLimits,valueZone,zoneColor,drawThresholdSegments,buildAxes,eventKey,smoothHermite,recordedIds,relevantAlarms,updateCounts};
})();
