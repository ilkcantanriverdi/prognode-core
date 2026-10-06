from pathlib import Path
from playwright.sync_api import sync_playwright
import re
w=Path('/mnt/data/PROGNODE_RC6_4_7_HF6_3_AUTOSTART_AUTH_CATALOG_FULL/src/Prognode.Host/wwwroot')
s=(w/'trend-hf3plus.html').read_text(encoding='utf-8')
s=re.sub(r'<link rel="stylesheet" href="/([^?" ]+)(?:\?[^" ]+)?"\s*/?>',lambda m:'<style>'+(w/m.group(1)).read_text(encoding='utf-8')+'</style>',s)
mock='''<script>window.fetch=async(input,options={})=>{
let u=String(input),arr=[];
if(u.startsWith('/api/access/status'))arr={authenticated:true};
else if(u.startsWith('/api/license'))arr={isValid:true,assignedUserId:'OWNER',status:'ACTIVE'};
else if(u.startsWith('/api/tags'))arr=Array.from({length:8},(_,i)=>({id:'T'+(i+1),deviceId:'PLC1',name:'Process Signal '+(i+1),unit:i===0?'°C':'bar',dataType:'Float',decimalPlaces:2}));
else if(u.startsWith('/api/devices'))arr=[{id:'PLC1',name:'Factory PLC'}];
else if(u.startsWith('/api/historian/configurations'))arr=Array.from({length:8},(_,i)=>({configuration:{tagId:'T'+(i+1),enabled:true,sampleIntervalSeconds:10},lastValue:5}));
else if(u.startsWith('/api/alarms/definitions'))arr=[];
else if(u.startsWith('/api/alarms/history')||u.startsWith('/api/batches'))arr=[];
else if(u.startsWith('/api/backup/layout'))arr={};
else if(u.startsWith('/api/trend-studio/series')){
 const q=new URL('https://example.test'+u).searchParams,id=q.get('tagIds'),start=Date.parse(q.get('from')),end=Date.parse(q.get('to'));
 const pts=Array.from({length:110},(_,i)=>({timestamp:new Date(start+(end-start)*i/109).toISOString(),value:30+Math.sin(i/10),quality:i>49&&i<60&&id==='T2'?'STALE':'GOOD'}));
 arr={series:[{tagId:id,points:pts,sampleCount:110,minimum:29,maximum:31,average:30}]};
}
return new Response(JSON.stringify(arr),{status:200,headers:{'Content-Type':'application/json'}})
};</script>'''
s=re.sub(r'<script src="/trend-hf3plus\.js\?[^\"]+"></script>',lambda m:mock+'<script>'+ (w/'trend-hf3plus.js').read_text(encoding='utf-8').replace('</script>','<\\/script>')+'</script>',s)
with sync_playwright() as pw:
 browser=pw.chromium.launch(headless=True,executable_path='/usr/bin/chromium',args=['--no-sandbox','--disable-dev-shm-usage','--disable-background-networking'])
 page=browser.new_page(viewport={'width':1500,'height':900},device_scale_factor=1)
 errors=[];page.on('pageerror',lambda e:errors.append(str(e)))
 page.set_content(s,wait_until='domcontentloaded',timeout=30000);page.wait_for_function('window.__PGN_TEST__?.Core.ready && window.__PGN_TEST__?.Core.authenticated',timeout=12000)
 assert page.evaluate('window.__PGN_TEST__.Core.ready'), 'Catalog not loaded'
 assert page.locator('#hf62-layout-select').input_value()=='auto','Default layout not auto'
 assert page.locator('#grid [data-drop-slot]').count()>=1,'No empty slot for first chart'
 page.locator('#hf62-more-toggle').click()
 assert page.locator('#hf62-advanced').is_visible(), 'Tools row not visible'
 assert page.locator('#save-layout').is_visible(), 'Save is clipped'
 assert page.locator('#export-csv').is_visible(), 'CSV is clipped'
 box=page.locator('#hf62-advanced').bounding_box();assert box['x']>=0 and box['x']+box['width']<=1501,'Tools outside viewport'
 page.locator('#hf62-more-toggle').click();assert not page.locator('#hf62-advanced').is_visible()
 page.locator('#hf61-explorer-list [data-signal-id="T1"]').click();page.wait_for_timeout(240)
 assert page.locator('.chart').count()==1,'Click did not add chart'
 page.locator('#hf61-explorer-list [data-signal-id="T2"]').drag_to(page.locator('#grid [data-drop-slot]').first)
 page.wait_for_timeout(350)
 assert page.locator('.chart').count()==2,'Drag from left did not add chart'
 page.locator('#hf62-layout-select').select_option('columns')
 assert page.locator('#grid').get_attribute('data-layout')=='columns','Manual layout ignored'
 assert page.locator('#grid [data-drop-slot="3"]').count()==1,'Fourth slot not available'
 page.locator('#hf61-explorer-list [data-signal-id="T3"]').drag_to(page.locator('#grid [data-drop-slot="3"]'))
 page.wait_for_timeout(300)
 assert page.evaluate('window.__PGN_TEST__.S.charts.find(c=>c.id==="T3").slot')==3,'Specific target cell ignored'
 assert page.locator('.chart').count()==3
 page.locator('.chart[data-chart="T1"] .move-handle').drag_to(page.locator('#grid [data-drop-slot="2"]'))
 assert page.evaluate('window.__PGN_TEST__.S.charts.find(c=>c.id==="T1").slot')==2,'Chart placement did not update'
 page.locator('#hf62-layout-select').select_option('three')
 assert page.locator('#grid').get_attribute('data-layout')=='three'
 page.locator('#hf62-layout-select').select_option('auto')
 assert page.locator('#grid').get_attribute('data-layout')=='auto'
 page.locator('#hf62-layout-select').select_option('columns')
 page.locator('#hf62-more-toggle').click();page.locator('#save-layout').click();page.wait_for_timeout(2900)
 assert not errors,errors
 page.screenshot(path='/mnt/data/HF63_TREND_LAYOUT_TEST.png',full_page=True)
 print('PASS: API catalog; AUTO default; full-width advanced tools; draggable explorer; explicit cell target; chart move; layout selection; save; JS errors=',len(errors))
 mobile=browser.new_page(viewport={'width':620,'height':830}); mobile_errors=[];mobile.on('pageerror',lambda e:mobile_errors.append(str(e)))
 mobile.set_content(s,wait_until='domcontentloaded');mobile.wait_for_function('window.__PGN_TEST__?.Core.ready && window.__PGN_TEST__?.Core.authenticated',timeout=12000)
 mobile.locator('#hf62-layout-select').select_option('three')
 assert mobile.evaluate('window.__PGN_TEST__.effectiveColumns()')==1,'Mobile must force 1 column'
 mobile.locator('#hf62-more-toggle').click();assert mobile.locator('#save-layout').is_visible()
 assert not mobile_errors,mobile_errors
 print('PASS: mobile single column and advanced tools accessible; JS errors=',len(mobile_errors))
 browser.close()
