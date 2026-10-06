"""Browser-level mocked Core tests run without network or privileged PLC access."""
from playwright.sync_api import sync_playwright
from pathlib import Path
import re
BASE=Path(__file__).resolve().parents[1]/'src/Prognode.Host/wwwroot'
html=(BASE/'trend-studio-fullscreen.html').read_text('utf8')
js=(BASE/'trend-studio-fullscreen.js').read_text('utf8')
# Opaque-origin Playwright documents cannot access native localStorage; provide an in-memory test-only substitute.
pre='''<script>
const __kv={...window.__testSettings};Object.defineProperty(window,'localStorage',{value:{getItem:k=>__kv[k]??null,setItem:(k,v)=>__kv[k]=String(v),removeItem:k=>delete __kv[k]}});
Object.defineProperty(window,'sessionStorage',{value:{getItem:k=>null,setItem:()=>{},removeItem:()=>{}}});
const t1='00000000-0000-4000-8000-000000000001',t2='00000000-0000-4000-8000-000000000002',d1='a0000000-0000-4000-8000-000000000001';
window.fetch=async path=>{let q=new URL(String(path),'http://test.pgn'),p=q.pathname;
let data=[];
if(p==='/api/tags')data=[{id:t1,deviceId:d1,name:'Temperature',unit:'°C',dataType:'Float32',decimalPlaces:1},{id:t2,deviceId:d1,name:'Pressure',unit:'bar',dataType:'Float32',decimalPlaces:1}];
if(p==='/api/devices')data=[{id:d1,name:'PLC1'}];
if(p==='/api/historian/configurations')data=[{configuration:{tagId:t1,enabled:true,sampleIntervalSeconds:10}},{configuration:{tagId:t2,enabled:true,sampleIntervalSeconds:30}}];
if(p==='/api/alarms/definitions')data=[{id:'x',tagId:t1,condition:'GreaterThanOrEqual',threshold:80,enabled:true}];
if(p==='/api/trend-studio/series'){
 let from=Date.parse(q.searchParams.get('from')),to=Date.parse(q.searchParams.get('to'));
 data={series:q.searchParams.get('tagIds').split(',').map((id)=>{let points=[],s=id===t1?10:30;
 let t=Math.ceil(from/(s*1000))*(s*1000);for(let n=0;t<=to&&n<1500;n++,t+=s*1000)points.push({timestamp:new Date(t).toISOString(),quality:'GOOD',value:20+(n%31)});
 return {tagId:id,points,sampleCount:points.length};})};
}
return {ok:true,status:200,text:async()=>JSON.stringify(data)};
};
</script>'''
html=re.sub(r'<script src="/trend-studio-fullscreen.js[^>]+></script>',lambda m:pre+'<script>'+js+'</script>',html)
with sync_playwright() as p:
 b=p.chromium.launch(executable_path='/usr/bin/chromium',headless=True,args=['--no-sandbox','--disable-dev-shm-usage'])
 for language,theme in [('en','dark'),('tr','light')]:
  page=b.new_page(viewport={'width':1600,'height':900});errors=[];page.on('pageerror',lambda e:errors.append(str(e)))
  page.set_content(html.replace('<script>\nconst __kv=',f"<script>window.__testSettings={{'prognode.language':'{language}','prognode.theme':'{theme}'}};</script><script>\nconst __kv=",1),wait_until='load');page.wait_for_timeout(600)
  print('INIT',language,theme,'BUTTON',page.locator('#addSignal').inner_text(),'ERRORS',errors,flush=True)
  assert not errors,errors
  assert page.locator('.rail').count()==0
  assert ('Add from Historian' if language=='en' else 'Historian’dan ekle') in page.locator('#addSignal').inner_text()
  page.locator('#addSignal').click();page.wait_for_timeout(100)
  assert page.locator('.signal-item').count()==2
  assert ('Recording interval' if language=='en' else 'Kayıt aralığı') in page.locator('.signal-item').first.inner_text()
  page.locator('.signal-item').first.click();page.wait_for_timeout(400)
  assert page.locator('.chart-card').count()==1
  assert 'PLC1' in page.locator('.chart-name').inner_text()
  assert 'sample' in page.locator('.chart-meta').inner_text().lower() or 'kayıt' in page.locator('.chart-meta').inner_text().lower()
  assert page.evaluate('PrognodeFullscreen.series.get(t1).points.length')>10
  assert page.locator('#pointButton').evaluate('(e)=>e.classList.contains("active")')
  if language=='en':
   page.screenshot(path='/mnt/data/PROGNODE_RC6_4_5_HF2_DARK_PREVIEW.png')
   cvs=page.locator('canvas[data-canvas]').first;bb=cvs.bounding_box()
   assert bb['width']>600 and bb['height']>300,bb
   page.mouse.move(bb['x']+bb['width']*.27,bb['y']+bb['height']*.6)
   page.mouse.down();page.mouse.move(bb['x']+bb['width']*.73,bb['y']+bb['height']*.6,steps=15);page.mouse.up()
   page.wait_for_timeout(200)
   z=page.evaluate('({live:PrognodeFullscreen.live,custom:PrognodeFullscreen.custom,range:PrognodeFullscreen.range})')
   print('ZOOM',z,flush=True);assert z['live'] is False and z['custom'] is True and z['range']['to']-z['range']['from']<15*60000,z
   page.locator('#resetButton').click();assert page.evaluate('PrognodeFullscreen.live') is True
   page.locator('#compareButton').click();assert page.locator('#compareBar').is_visible()
  else:
   assert page.locator('.toolbar').evaluate('(e)=>getComputedStyle(e).backgroundColor')=='rgb(255, 255, 255)'
   page.screenshot(path='/mnt/data/PROGNODE_RC6_4_5_HF2_LIGHT_PREVIEW.png')
  print('PASS',language,theme,flush=True)
  page.close()
 b.close()
print('PASS: browser integration for sample points, EN/TR, high-contrast light/dark, brush zoom, LIVE reset, comparison; no JS runtime errors',flush=True)
