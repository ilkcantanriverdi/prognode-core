from pathlib import Path
from playwright.sync_api import sync_playwright
import re,base64
w=Path(__file__).resolve().parent.parent / 'src/Prognode.Host/wwwroot'
s=(w/'index.html').read_text('utf-8')
s=re.sub(r'<link rel="stylesheet" href="/([^?" ]+)(?:\?[^" ]+)?"\s*/?>',lambda m:'<style>'+(w/m.group(1)).read_text('utf-8')+'</style>',s)
s=re.sub(r'<script(?: defer)? src="/([^?" ]+)(?:\?[^" ]+)?"></script>',lambda m:'<script>'+(w/m.group(1)).read_text('utf-8').replace('</script>','<\\/script>')+'</script>',s)
for f in (w/'assets').glob('*.png'):s=s.replace('/assets/'+f.name,'data:image/png;base64,'+base64.b64encode(f.read_bytes()).decode())
s=s.replace('href="/assets/prognode.ico"','href="data:,"')
with sync_playwright() as pw:
 b=pw.chromium.launch(headless=True,executable_path='/usr/bin/chromium',args=['--no-sandbox','--disable-dev-shm-usage','--disable-background-networking'])
 p=b.new_page(viewport={'width':1366,'height':768},device_scale_factor=1)
 p.evaluate("""(()=>{const a=new Map(),c=new Map();Object.defineProperty(window,'localStorage',{configurable:true,value:{getItem:k=>a.get(k)||null,setItem:(k,v)=>a.set(k,v),removeItem:k=>a.delete(k)}});Object.defineProperty(window,'sessionStorage',{configurable:true,value:{getItem:k=>c.get(k)||null,setItem:(k,v)=>c.set(k,v),removeItem:k=>c.delete(k)}});})()""")
 errors=[];p.on('pageerror',lambda e:errors.append(str(e)))
 p.set_content(s,wait_until='domcontentloaded');p.wait_for_timeout(400)
 assert p.locator('#brandWebsite').get_attribute('href')=='https://prognode.io/'
 assert p.locator('#brandWebsite .external-arrow').count()==0
 # Guard false ready label on invalid or revoked licenses.
 p.evaluate("window.customerV2Refresh({devices:0,tags:0,alarms:0,recording:0,licensed:false,coreHealthy:false})")
 assert p.locator('#customerStepLicense').inner_text()=='Pending'
 assert p.locator('#customerCoreText').inner_text()=='Core unavailable'
 p.locator('#customerAddFirstDevice').click()
 assert p.locator('.nav-item[data-page="license"]').evaluate('(x)=>x.classList.contains("active")')
 p.locator('.nav-item[data-page="settings"]').click()
 for section in ('general','lan','remote','backup'):
  p.locator(f'[data-open-settings="{section}"]').click()
  assert p.locator(f'.settings-tab[data-settings-tab="{section}"]').evaluate('(x)=>x.classList.contains("active")'),section
  assert p.locator(f'[data-settings-panel="{section}"]').first.is_visible(),section
  p.locator('#customerSettingsBack').click()
  assert p.locator('#customerSettingsLanding').is_visible()
 print('PASS: .io logo, no external icon, invalid license stays pending, Core offline, all 4 Settings routes')
 # set language with original Core controls; navigation breadcrumb follows.
 p.locator('#langTR').click()
 assert p.locator('#customerPageName').inner_text()=='Ayarlar'
 p.locator('#langEN').click()
 assert p.locator('#customerPageName').inner_text()=='Settings'
 print('PASS: TR/EN synced with customer breadcrumb')
 for width,height in ((390,844),(768,1024),(1366,768),(1920,1080)):
  p.set_viewport_size({'width':width,'height':height})
  assert p.evaluate('document.documentElement.scrollWidth-window.innerWidth')==0,(width,'overflow')
 print('PASS: 390,768,1366,1920 without horizontal overflow')
 assert not errors,errors
 print('PASS: zero JavaScript page errors')
 b.close()
