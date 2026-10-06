from pathlib import Path
from playwright.sync_api import sync_playwright
import re,base64
w=Path(__file__).resolve().parent.parent / 'src/Prognode.Host/wwwroot'
s=(w/'trend-hf3plus.html').read_text(encoding='utf-8')
s=re.sub(r'<link rel="stylesheet" href="/([^?" ]+)(?:\?[^" ]+)?"\s*/?>',lambda m:'<style>'+ (w/m.group(1)).read_text(encoding='utf-8') +'</style>',s)
mock='''<script>
window.fetch=async(input,options={})=>{
let u=String(input);let arr=[];
if(u.startsWith('/api/tags')) arr=[{id:'T1',deviceId:'DEV',name:'Boiler Temperature',unit:'°C',dataType:'Float',decimalPlaces:1},{id:'T2',deviceId:'DEV',name:'Steam Pressure',unit:'bar',dataType:'Float',decimalPlaces:2},{id:'T3',deviceId:'DEV',name:'Flow Rate',unit:'L/min',dataType:'Float',decimalPlaces:0},{id:'T4',deviceId:'DEV',name:'Pump Running',unit:'',dataType:'Bool'},{id:'T5',deviceId:'DEV',name:'Return Water',unit:'°C',dataType:'Float'},{id:'T6',deviceId:'DEV',name:'Supply Water',unit:'°C',dataType:'Float'},{id:'T7',deviceId:'DEV',name:'Flue Temp',unit:'°C',dataType:'Float'},{id:'T8',deviceId:'DEV',name:'Energy Demand',unit:'kW',dataType:'Float'}];
else if(u.startsWith('/api/devices')) arr=[{id:'DEV',name:'BOILER-PLC'}];
else if(u.startsWith('/api/historian/configurations')) arr=['T1','T2','T3','T4','T5','T6','T7','T8'].map(x=>({configuration:{enabled:true,tagId:x,sampleIntervalSeconds:30},lastValue:70}));
else if(u.startsWith('/api/alarms/definitions')) arr=[{tagId:'T1',enabled:true,threshold:80,condition:'GreaterThan'}];
else if(u.startsWith('/api/backup/layout')) arr={};
else if(u.startsWith('/api/alarms/history')||u.startsWith('/api/batches')) arr=[];
else if(u.startsWith('/api/trend-studio/series')){let q=new URL('https://example.com'+u).searchParams,id=q.get('tagIds'),from=Date.parse(q.get('from')),to=Date.parse(q.get('to')),pts=[];for(let i=0;i<150;i++){const t=from+(to-from)*i/149;pts.push({timestamp:new Date(t).toISOString(),value:(id==='T1'?74:id==='T2'?5.7:id==='T3'?128:1)+(id==='T4'?0:Math.sin(i/9)*(id==='T3'?8:1.8)),quality:id==='T2'&&i>75&&i<84?'BAD':'GOOD'});} arr={series:[{tagId:id,points:pts,sampleCount:150,minimum:0,maximum:100,average:60}]};}
return new Response(JSON.stringify(arr),{status:200,headers:{'Content-Type':'application/json'}})
};
</script>'''
s=re.sub(r'<script src="/trend-hf3plus\.js\?[^"]+"></script>',lambda m:mock+'<script>'+ (w/'trend-hf3plus.js').read_text(encoding='utf-8').replace('</script>','<\\/script>')+'</script>',s)
with sync_playwright() as pw:
 b=pw.chromium.launch(headless=True,executable_path='/usr/bin/chromium',args=['--no-sandbox','--disable-dev-shm-usage','--disable-background-networking'])
 page=b.new_page(viewport={'width':1500,'height':900},device_scale_factor=1)
 page.evaluate('''(()=>{const m=new Map(),n=new Map();for(const [name,map] of [['localStorage',m],['sessionStorage',n]])Object.defineProperty(window,name,{configurable:true,value:{getItem:k=>map.get(k)||null,setItem:(k,v)=>map.set(k,v),removeItem:k=>map.delete(k)}})})()''')
 errors=[];page.on('pageerror',lambda e:errors.append(str(e)))
 page.set_content(s,wait_until='domcontentloaded',timeout=30000)
 page.wait_for_timeout(500)
 page.evaluate("window.__PGN_TEST__.appendSignals(['T1','T2','T3','T4','T5','T6','T7','T8'])")
 page.wait_for_timeout(350)
 print('catalog',page.evaluate('window.__PGN_TEST__.Core.ready'),'charts',page.locator('.chart').count())
 assert not errors,errors
 print('errors',errors[:7])
 # no screenshot needed for automated acceptance; previews are in previews/HF6/
 assert page.locator('.chart').count()==8
 if page.locator('.chart').count()==8:
  page.locator('[data-chart-window="T1"]').select_option('1440')
  print('range',page.locator('[data-chart-window="T1"]').input_value())
 b.close()
