"""Visual layout test using the exact Core shell CSS/markup with mocked child API."""
from pathlib import Path
from playwright.sync_api import sync_playwright
from html import escape
import re,base64
R=Path(__file__).resolve().parents[1]/'src/Prognode.Host/wwwroot'
phtml=(R/'index.html').read_text('utf8')
child=(R/'trend-studio-fullscreen.html').read_text('utf8')
# Child-script stub from the browser test: reuse its source block.
test=(Path(__file__).parent/'test_hf2_inline_browser.py').read_text('utf8')
pre=test.split("pre='''",1)[1].split("'''\nhtml=",1)[0]
child_js=(R/'trend-studio-fullscreen.js').read_text('utf8')
child=re.sub(r'<script src="/trend-studio-fullscreen.js[^>]+></script>',lambda m:pre+'<script>'+child_js+'</script>',child)
for asset in ('prognode.png','prognode-light.png'):
 uri='data:image/png;base64,'+base64.b64encode((R/'assets'/asset).read_bytes()).decode()
 phtml=phtml.replace('src="/assets/'+asset+'"','src="'+uri+'"')
phtml=re.sub(r'<link rel="stylesheet" href="/styles.css[^"]*"\s*/>',lambda m:'<style>'+(R/'styles.css').read_text('utf8')+'</style>',phtml)
phtml=re.sub(r'<link rel="stylesheet" href="/trend-studio.css[^"]*"\s*/>',lambda m:'<style>'+(R/'trend-studio.css').read_text('utf8')+'</style>',phtml)
phtml=re.sub(r'<script src="/[^"]+"\s*></script>','',phtml)
phtml=phtml.replace('<section class="page active" id="page-overview">','<section class="page" id="page-overview">')
phtml=phtml.replace('<section class="page studio-page" id="page-trends"','<section class="page studio-page active" id="page-trends"')
phtml=phtml.replace('<button class="nav-item active" data-page="overview">','<button class="nav-item" data-page="overview">')
phtml=phtml.replace('<button class="nav-item" data-page="trends">','<button class="nav-item active" data-page="trends">')
phtml=phtml.replace('<h1 id="pageTitle">Overview</h1>','<h1 id="pageTitle">Trend Studio</h1>')
phtml=phtml.replace('<script>\n(()=>{const shell=',"<script>Object.defineProperty(window,'localStorage',{value:{getItem:()=>null,setItem:()=>{}}});</script><script>\n(()=>{const shell=")
# Disable the remote src attribute. Use same-origin srcdoc, exact markup and JS with explicit mock HTTP.
phtml=re.sub(r'(<iframe id="fullscreenTrendFrame"[^>]*?)src="[^"]+"',r'\1src="about:blank"',phtml)
with sync_playwright() as p:
 b=p.chromium.launch(executable_path='/usr/bin/chromium',headless=True,args=['--no-sandbox','--disable-dev-shm-usage'])
 page=b.new_page(viewport={'width':1650,'height':930})
 errs=[];page.on('pageerror',lambda e:errs.append(str(e)))
 page.set_content(phtml,wait_until='load')
 page.evaluate("document.documentElement.dataset.theme='dark';document.documentElement.lang='en'")
 page.locator('#fullscreenTrendFrame').evaluate('(el,doc)=>el.srcdoc=doc',child)
 page.wait_for_timeout(550)
 frame=page.frame_locator('#fullscreenTrendFrame')
 assert frame.locator('.rail').count()==0
 assert page.locator('#brandLogo').evaluate('(e)=>e.naturalWidth')>0
 nav=page.locator('#sidebar').bounding_box();mount=page.locator('#fullscreenTrendMount').bounding_box()
 print('BOUNDS',nav,mount,flush=True)
 assert mount['x']>=nav['x']+nav['width']-3,(mount,nav)
 assert mount['width']>1100 and mount['height']>650,mount
 page.screenshot(path='/mnt/data/PROGNODE_RC6_4_5_HF2_CORE_SHELL_PREVIEW.png')
 page.locator('#sidebarToggle').click()
 assert page.locator('.app-shell').evaluate('(e)=>e.classList.contains("sidebar-collapsed")')
 assert page.locator('#brandLogo').is_visible()
 after=page.locator('#sidebar').bounding_box();assert after['width']<nav['width']/2,(after,nav)
 page.screenshot(path='/mnt/data/PROGNODE_RC6_4_5_HF2_COLLAPSED_PREVIEW.png')
 page.evaluate("document.documentElement.dataset.theme='light';document.documentElement.lang='tr'")
 page.wait_for_timeout(160)
 assert frame.locator('#addSignal').inner_text().find('ekle')!=-1
 assert frame.locator('.toolbar').evaluate('(e)=>getComputedStyle(e).backgroundColor')=='rgb(255, 255, 255)'
 page.screenshot(path='/mnt/data/PROGNODE_RC6_4_5_HF2_CORE_LIGHT_PREVIEW.png')
 print('PASS: official B1 asset, original native sidebar full + collapsed, fullscreen trend constrained to original Core content, EN/TR and parent light theme synchronization',flush=True)
 assert not errs,errs
 b.close()
