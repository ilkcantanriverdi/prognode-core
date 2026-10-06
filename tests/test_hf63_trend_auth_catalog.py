"""Deterministic Chromium test: anonymous readonly, login transition and same-process Historian refresh.
Uses mock API responses, NOT a Windows/PLC/customer production test.
"""
from pathlib import Path
from playwright.sync_api import sync_playwright
import re

web = Path(__file__).resolve().parents[1] / 'src' / 'Prognode.Host' / 'wwwroot'
src = (web / 'trend-hf3plus.html').read_text(encoding='utf-8')
src = re.sub(r'<link rel="stylesheet" href="/([^?" ]+)(?:\?[^" ]+)?"\s*/?>',
             lambda m: '<style>' + (web / m.group(1)).read_text(encoding='utf-8') + '</style>', src)
mock = '''<script>
window.testEnv={auth:false,tags:['T1'],recorded:['T1'],writes:[],cache:new Map()};
for(const [prop,map] of [['localStorage',testEnv.cache],['sessionStorage',new Map()]]){
  Object.defineProperty(window,prop,{configurable:true,value:{
    getItem:k=>map.get(k)||null,setItem:(k,v)=>map.set(k,v),removeItem:k=>map.delete(k)
  }});
}
window.fetch=async(input,options={})=>{
 const u=String(input), e=testEnv;let response=[];const method=options.method||'GET';
 if(method!=='GET')e.writes.push([u,method]);
 if(u.startsWith('/api/access/status'))response={authenticated:e.auth};
 else if(u.startsWith('/api/license'))response={isValid:e.auth,assignedUserId:e.auth?'USER-1':null};
 else if(u.startsWith('/api/tags'))response=e.tags.map(id=>({id,deviceId:'PLC1',name:id==='T1'?'Steam temperature':'Flow rate',unit:'°C',dataType:'Float',decimalPlaces:1}));
 else if(u.startsWith('/api/devices'))response=[{id:'PLC1',name:'Plant PLC'}];
 else if(u.startsWith('/api/historian/configurations'))response=e.recorded.map(id=>({configuration:{tagId:id,enabled:true,sampleIntervalSeconds:10},lastValue:74}));
 else if(u.startsWith('/api/alarms/definitions'))response=[];
 else if(u.startsWith('/api/alarms/history')||u.startsWith('/api/batches'))response=[];
 else if(u.startsWith('/api/backup/layout')){
   response={};if(method!=='GET'&&!e.auth)return new Response(JSON.stringify({message:'Sign in required'}),{status:401});
 }
 else if(u.startsWith('/api/trend-studio/series')){
   const q=new URL('https://test.local'+u).searchParams,id=q.get('tagIds'),start=Date.parse(q.get('from')),end=Date.parse(q.get('to'));
   response={series:[{tagId:id,points:Array.from({length:20},(_,i)=>({timestamp:new Date(start+(end-start)*i/19).toISOString(),value:74+Math.sin(i),quality:'GOOD'})),sampleCount:20,minimum:70,maximum:80,average:74}]};
 }
 return new Response(JSON.stringify(response),{status:200,headers:{'Content-Type':'application/json'}});
};</script>'''
src = re.sub(r'<script src="/trend-hf3plus\.js\?[^"]+"></script>',
             lambda m: mock + '<script>' + (web / 'trend-hf3plus.js').read_text(encoding='utf-8').replace('</script>', '<\\/script>') + '</script>', src)

with sync_playwright() as pw:
    browser = pw.chromium.launch(headless=True, executable_path='/usr/bin/chromium', args=['--no-sandbox', '--disable-dev-shm-usage','--disable-background-networking'])
    page = browser.new_page(viewport={'width': 1440, 'height': 900})
    errors=[]
    page.on('pageerror',lambda e: errors.append(str(e)))
    page.set_content(src,wait_until='domcontentloaded',timeout=30000)
    page.wait_for_function('window.__PGN_TEST__?.Core.ready && !window.__PGN_TEST__.Core.authPending',timeout=15000)
    assert not page.evaluate('window.__PGN_TEST__.Core.authenticated')
    assert page.locator('#add').is_disabled()
    assert page.locator('#hf62-layout-select').is_disabled()
    assert page.locator('#hf63-access-hint').is_visible()
    page.evaluate("window.__PGN_TEST__.appendSignals(['T1'])")
    assert page.locator('.chart').count()==0
    page.evaluate('window.__PGN_TEST__.storeLayout()')
    page.wait_for_timeout(100)
    assert page.evaluate('testEnv.writes.length')==0
    print('PASS anonymous: layout/add disabled, no in-memory chart mutation, no API write or localStorage save')

    page.evaluate('testEnv.auth=true')
    page.evaluate('window.__PGN_TEST__.refreshTrendAuth()')
    page.wait_for_function('window.__PGN_TEST__.Core.authenticated',timeout=8000)
    page.locator('#hf61-explorer-list [data-signal-id="T1"]').click()
    assert page.locator('.chart').count()==1
    page.locator('#hf62-layout-select').select_option('columns')
    page.locator('#hf62-more-toggle').click()
    page.locator('#save-layout').click()
    page.wait_for_function('testEnv.writes.some(r=>r[0].includes("/api/backup/layout")&&r[1]==="POST")',timeout=8000)
    assert page.evaluate("testEnv.cache.has('PROGNODE_HF3_PLUS_CORE_LAYOUT_V1')")
    print('PASS authenticated: chart and layout editable; explicit save persisted to server before browser')

    page.evaluate('testEnv.tags.push("T2");testEnv.recorded.push("T2")')
    # Imitate the Historian-save notification from the Core host without refreshing the browser.
    page.evaluate("window.dispatchEvent(new MessageEvent('message',{data:{type:'pgn:historian-changed'},origin:location.origin,source:window.parent}))")
    page.wait_for_function('window.__PGN_TEST__.signalDefs().length===2',timeout=8000)
    assert page.locator('#hf61-explorer-list [data-signal-id="T2"]').count()==1
    print('PASS Historian added while running: second tag appears without Core/browser restart')

    # Session expiry/revocation: next check must lock controls and reject direct handlers.
    page.evaluate('testEnv.auth=false')
    page.evaluate('window.__PGN_TEST__.refreshTrendAuth()')
    page.wait_for_function('!window.__PGN_TEST__.Core.authPending && !window.__PGN_TEST__.Core.authenticated',timeout=8000)
    assert page.locator('#save-layout').is_disabled()
    before=page.evaluate('testEnv.writes.length')
    page.evaluate('window.__PGN_TEST__.storeLayout()')
    assert page.evaluate('testEnv.writes.length') == before
    page.screenshot(path=str(Path('/mnt/data/HF63_TREND_READONLY_TEST.png')),full_page=True)
    assert not errors,errors
    print('PASS sign-out: edit controls immediately disabled, no further writes. Browser JS errors:',len(errors))
    browser.close()
