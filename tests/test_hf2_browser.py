"""HF2 offline browser acceptance (mocked API, no PLC/database/network changes)."""
from pathlib import Path
from http.server import ThreadingHTTPServer, SimpleHTTPRequestHandler
from threading import Thread
from urllib.parse import urlsplit,parse_qs
from playwright.sync_api import sync_playwright
import json,time
root=Path(__file__).resolve().parents[1]/'src/Prognode.Host/wwwroot'
class Static(SimpleHTTPRequestHandler):
    def __init__(self,*args,**kw): super().__init__(*args, directory=str(root),**kw)
    def log_message(self,*args):pass
srv=ThreadingHTTPServer(('127.0.0.1',0),Static)
Thread(target=srv.serve_forever,daemon=True).start();origin=f'http://127.0.0.1:{srv.server_port}'
tagids=['12345678-1234-1234-1234-000000000001','12345678-1234-1234-1234-000000000002']
tags=[{'id':t,'name':n,'unit':'°C' if i==0 else 'bar','deviceId':'11111111-1111-1111-1111-111111111111','decimalPlaces':2,'dataType':'Real'} for i,(t,n) in enumerate(zip(tagids,['Subzero Temp','Process Pressure']))]
base=int(time.time()//60*60)*1000
responses={'/api/tags':tags,'/api/devices':[{'id':'11111111-1111-1111-1111-111111111111','name':'PLC1'}],'/api/historian/configurations':[{'configuration':{'tagId':tid,'enabled':True,'sampleIntervalSeconds':10},'lastValue':10} for tid in tagids],'/api/alarms/definitions':[],'/api/alarms/history':[],'/api/batches':[]}
with sync_playwright() as p:
 browser=p.chromium.launch(executable_path='/usr/bin/chromium',headless=True,args=['--no-sandbox','--disable-dev-shm-usage'])
 page=browser.new_page(viewport={'width':1560,'height':920})
 errors=[];page.on('pageerror',lambda e: errors.append(str(e)))
 def route_req(route):
  url=urlsplit(route.request.url);path=url.path
  if path=='/api/trend-studio/series':
   qs=parse_qs(url.query);tid=qs['tagIds'][0];start=int(__import__('datetime').datetime.fromisoformat(qs['from'][0].replace('Z','+00:00')).timestamp()*1000)
   end=int(__import__('datetime').datetime.fromisoformat(qs['to'][0].replace('Z','+00:00')).timestamp()*1000)
   points=[{'timestamp':__import__('datetime').datetime.fromtimestamp(t/1000,__import__('datetime').timezone.utc).isoformat(),'quality':('Bad' if i==20 else 'Stale' if i==21 else 'Good'),'value':(None if i in (20,21) else -15+i*.2 if tid==tagids[0] else 2+i*.05)} for i,t in enumerate(range((start//10000)*10000,end+1,10000))]
   payload={'series':[{'tagId':tid,'points':points,'sampleCount':len(points),'minimum':-15,'maximum':25,'average':3.0}]}
  else:payload=responses.get(path,{})
  route.fulfill(status=200,content_type='application/json',body=json.dumps(payload))
 def load_studio():
  import re
  html=(root/'trend-hf3plus.html').read_text()
  html=re.sub(r'<script src="/trend-hf3plus.js[^>]*></script>', '', html)
  html=re.sub(r'<link rel="stylesheet"[^>]*>', '', html)
  page.set_content(html,wait_until='load')
  page.evaluate('''() => {
     window.__layoutStore = window.__layoutStore || {};
     const mock={getItem:k=>window.__layoutStore[k]??null,setItem:(k,v)=>{window.__layoutStore[k]=v},removeItem:k=>{delete window.__layoutStore[k]}};
     Object.defineProperty(window,'localStorage',{configurable:true,value:mock});
     Object.defineProperty(window,'sessionStorage',{configurable:true,value:mock});
  }''')
  page.evaluate('''(m)=>{
    window.fetch=async(url)=>{
      let path=String(url).split('?')[0], payload=m.responses[path]||{};
      if(path==='/api/trend-studio/series'){
        const qs=new URLSearchParams(String(url).split('?')[1]),tid=qs.get('tagIds');
        const from=Date.parse(qs.get('from')),to=Date.parse(qs.get('to'));
        const points=[];
        let i=0;for(let t=Math.floor(from/10000)*10000;t<=to;t+=10000){
           let quality=i===20?'Bad':i===21?'Stale':'Good';
           points.push({timestamp:new Date(t).toISOString(),quality,
               value:quality==='Good'?(tid===m.tagids[0]?-15+i*.2:2+i*.05):null});i++;
        }
        payload={series:[{tagId:tid,points,sampleCount:points.length,minimum:-15,maximum:25,average:3}]};
      }
      return {ok:true,status:200,text:async()=>JSON.stringify(payload)};
    };
  }''',{'responses':responses,'tagids':tagids})
  page.add_script_tag(path=str(root/'trend-hf3plus.js'))
 load_studio()
 page.wait_for_function('window.__PGN_TEST__?.Core.ready === true',timeout=10000)
 assert page.locator('.chart').count()==0,'No fake/default trends allowed'
 page.locator('#add').click()
 assert page.locator('.sig').count()==2,'Historian-enabled signals only'
 page.locator('#select-all').click();page.locator('#add-selected').click()
 page.wait_for_function('document.querySelectorAll(".chart").length === 2',timeout=10000)
 page.locator('.chart-range').first.select_option('60')
 assert page.locator('.chart-range').nth(0).input_value()=='60'
 assert page.locator('.chart-range').nth(1).input_value()=='15','Time windows must be independent'
 page.locator('#sync-cursor').check()
 page.locator('#col-width').fill('62')
 page.locator('#save-layout').click()
 assert page.evaluate('!!localStorage.getItem("PROGNODE_HF3_PLUS_CORE_LAYOUT_V1")')
 page.evaluate('''() => {window.__PGN_TEST__.S.charts=[];window.__PGN_TEST__.Core.ready=false;window.__PGN_TEST__.loadCatalog()}''')
 page.wait_for_function('window.__PGN_TEST__?.Core.ready === true && window.__PGN_TEST__?.S.charts.length === 2',timeout=10000)
 assert page.locator('.chart-range').first.input_value()=='60'
 assert page.locator('#sync-cursor').is_checked()
 assert page.locator('#col-width').input_value()=='62'
 assert not errors, errors[:4]
 print('PASS: HF3+ loads true catalog, starts without fake charts, add/select, independent ranges, linked cursor, saved layout; no runtime JS errors.')
 # QR panel uses the actual shipped JS, with mocked Core responses and no need for a customer license.
 qr=browser.new_page(viewport={'width':1100,'height':790})
 qrerrors=[];qr.on('pageerror',lambda e:qrerrors.append(str(e)))
 qr.set_content('''<section id="qrPairBox" class="hidden"><select id="qrLanHost"></select>
 <div id="qrTlsGuide" class="hidden"><b>HTTPS required</b><button id="qrTlsRecheck">Recheck</button></div>
 <div id="qrGraphic" class="hidden"></div><p id="qrPairStatus"></p>
 <strong id="qrPairCountdown"></strong><button id="qrPairStart">Start</button><button id="qrPairRenew">Renew</button><button id="qrPairCancel">Cancel</button></section>''')
 qr.add_script_tag(path=str(root/'qr-local.js'))
 qr.evaluate('''() => {window.state={accessSession:{authenticated:true},serverIdentity:{secureApiPort:null}};
 window.$=(x)=>document.getElementById(x); window.t=(x)=>x;
 window.api=async (url,opts)=>{
  if(url.endsWith('/network-options'))return {addresses:[{address:'192.168.1.52',interfaceName:'Wi-Fi'}]};
  if(url.endsWith('/identity'))return {secureApiPort:5443};
  if(url.endsWith('/status'))return {status:'PENDING'};
  if(opts?.method==='POST')return {expiresAtUtc:new Date(Date.now()+120000).toISOString(),qrPayload:'eyJ2IjoxLCJ0eXBlIjoiUFJPR05PREVfTEFOX1BBSVJJTkciLCJ0ZXN0Ijp0cnVlfQ'};
  return {};
 }; }''')
 src=(root/'app.js').read_text();frag=src[src.index('/* QR enrollment: independent of Trends and Remote entitlement') :]
 qr.add_script_tag(content=frag)
 qr.evaluate('qrStart(true)')
 qr.wait_for_function('!document.getElementById("qrTlsGuide").classList.contains("hidden")')
 assert qr.locator('#qrGraphic svg').count()==0,'Cannot show an unsafe HTTP QR'
 assert qr.locator('#qrPairRenew').is_disabled(),'Renew should not retry endlessly with no HTTPS'
 qr.locator('#qrTlsRecheck').click()
 qr.wait_for_selector('#qrGraphic svg')
 assert qr.locator('#qrTlsGuide').get_attribute('class')=='hidden'
 assert not qr.locator('#qrPairRenew').is_disabled()
 qr.locator('#qrPairCancel').click();assert qr.locator('#qrGraphic svg').count()==0
 assert not qrerrors,qrerrors
 print('PASS: QR blocked until HTTPS, guided setup/recheck, local renderer produces actual SVG, cancel clears QR.')
 browser.close()
srv.shutdown()
