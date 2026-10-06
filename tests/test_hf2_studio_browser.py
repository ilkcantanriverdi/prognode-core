"""HF2 mocked HTTP-browser acceptance; no PLC or private license file required."""
from playwright.sync_api import sync_playwright
from datetime import datetime, timezone, timedelta
from urllib.parse import urlparse, parse_qs
import json
BASE='http://127.0.0.1:5088'
TAGS=[{'id':f'00000000-0000-4000-8000-00000000000{i}','deviceId':'a0000000-0000-4000-8000-000000000001','name':name,'unit':'°C' if i==1 else 'bar','dataType':'Float32','decimalPlaces':1} for i,name in [(1,'Temperature'),(2,'Pressure')]]
DEV=[{'id':'a0000000-0000-4000-8000-000000000001','name':'PLC1'}]
HIST=[{'configuration':{'tagId':t['id'],'enabled':True,'sampleIntervalSeconds':10 if i==0 else 30}} for i,t in enumerate(TAGS)]
ALARM=[{'id':'b0000000-0000-4000-8000-000000000001','tagId':TAGS[0]['id'],'enabled':True,'condition':'GreaterThanOrEqual','threshold':80}]
NOW=datetime.now(timezone.utc)

def respond(route):
    path=urlparse(route.request.url).path
    data={'/api/tags':TAGS,'/api/devices':DEV,'/api/historian/configurations':HIST,'/api/alarms/definitions':ALARM,'/api/alarms/history':[], '/api/batches':[], '/api/health':{'version':'0.7.2-rc6.4.5-hf2-core-native'}}
    if path=='/api/trend-studio/series':
        q=parse_qs(urlparse(route.request.url).query)
        begin=datetime.fromisoformat(q['from'][0].replace('Z','+00:00'))
        end=datetime.fromisoformat(q['to'][0].replace('Z','+00:00'))
        length=(end-begin).total_seconds()
        out=[]
        for tag_id in q['tagIds'][0].split(','):
            seconds=10 if tag_id==TAGS[0]['id'] else 30
            # Fixed actual recording times, no fabricated sub-interval samples.
            vals=[]
            x=begin.replace(microsecond=0)
            for n in range(min(2000,int(length//seconds)+1)):
                ts=x+timedelta(seconds=n*seconds)
                if ts>end: break
                vals.append({'timestamp':ts.isoformat(),'value':float(30+n%30),'quality':'GOOD'})
            out.append({'tagId':tag_id,'points':vals,'sampleCount':len(vals)})
        data={'series':out}
    else:
        data=data.get(path,[])
    route.fulfill(status=200,content_type='application/json',body=json.dumps(data))

with sync_playwright() as p:
    browser=p.chromium.launch(executable_path='/usr/bin/chromium',headless=True,args=['--no-sandbox','--disable-dev-shm-usage'])
    ctx=browser.new_context(viewport={'width':1600,'height':900})
    ctx.add_init_script("localStorage.setItem('prognode.language','en');localStorage.setItem('prognode.theme','dark')")
    ctx.route('**/api/**',respond)
    page=ctx.new_page();errors=[];page.on('pageerror',lambda e:errors.append(str(e)))
    page.goto(BASE+'/trend-studio-fullscreen.html',wait_until='domcontentloaded')
    page.wait_for_timeout(450)
    assert page.locator('.rail').count()==0, 'rogue sidebar remains'
    assert page.locator('#addSignal').inner_text().strip()=='＋ Add from Historian',page.locator('#addSignal').inner_text()
    page.locator('#addSignal').click();page.wait_for_timeout(100)
    assert page.locator('.signal-item').count()==2
    assert 'Recording interval' in page.locator('.signal-item').first.inner_text()
    page.locator('.signal-item').first.click();page.wait_for_timeout(550)
    assert page.locator('.chart-card').count()==1
    assert 'PLC1' in page.locator('.chart-name').first.inner_text()
    assert page.locator('.chart-name').first.inner_text().lower().endswith('temperature')
    assert 'sample' in page.locator('.chart-meta').first.inner_text().lower()
    assert page.evaluate("PrognodeFullscreen.series.get('00000000-0000-4000-8000-000000000001').points.length")>10
    page.screenshot(path='/mnt/data/PROGNODE_RC6_4_5_HF2_DARK_PREVIEW.png',full_page=True)
    plot=page.locator('canvas[data-canvas]').first;bounds=plot.bounding_box()
    assert bounds['width']>500 and bounds['height']>250,bounds
    # Chart-selection drag should select a real time range and pause LIVE.
    page.mouse.move(bounds['x']+bounds['width']*.30,bounds['y']+bounds['height']*.58)
    page.mouse.down();page.mouse.move(bounds['x']+bounds['width']*.75,bounds['y']+bounds['height']*.58,steps=12)
    page.mouse.up();page.wait_for_timeout(450)
    status=page.evaluate('({live:PrognodeFullscreen.live,custom:PrognodeFullscreen.custom,range:PrognodeFullscreen.range})')
    assert not status['live'] and status['custom'],status
    assert status['range']['to']-status['range']['from']<15*60*1000,status
    assert page.locator('.chart-card').count()==1,'drag must not accidentally enlarge or close'
    page.locator('#resetButton').click();page.wait_for_timeout(200)
    assert page.evaluate('PrognodeFullscreen.live') is True
    # Display raw historian samples, compare mode opt-in, genuine English/parent-mode dark and light.
    assert page.locator('#pointButton').evaluate('(e)=>e.classList.contains("active")')
    page.locator('#pointButton').click();assert not page.locator('#pointButton').evaluate('(e)=>e.classList.contains("active")')
    assert page.locator('#compareBar').is_visible() is False
    page.locator('#compareButton').click();assert page.locator('#compareBar').is_visible()
    # English theme via parent (test direct URL reload with localStorage).
    page.evaluate("localStorage.setItem('prognode.theme','light');localStorage.setItem('prognode.language','tr')")
    page.reload();page.wait_for_timeout(350)
    assert page.locator('#addSignal').inner_text().strip()=='＋ Historian’dan ekle'
    light_bg=page.locator('.toolbar').evaluate('(e)=>getComputedStyle(e).backgroundColor')
    assert light_bg=='rgb(255, 255, 255)',light_bg
    page.screenshot(path='/mnt/data/PROGNODE_RC6_4_5_HF2_LIGHT_PREVIEW.png',full_page=True)
    assert not errors,errors
    print('PASS: native iframe-only Studio (no duplicate sidebar), EN/TR, live Historian sample interval, zoom drag, reset, compare, light/dark; no JS errors')
    # Outer shell must retain B1 logo and support collapse across pages; iframe must stay within Core main area.
    shell=ctx.new_page();shell_errors=[];shell.on('pageerror',lambda e:shell_errors.append(str(e)))
    shell.goto(BASE+'/',wait_until='domcontentloaded');shell.wait_for_timeout(400)
    assert shell.locator('#brandLogo').get_attribute('src') in ['/assets/prognode-light.png','/assets/prognode.png']
    shell.locator('#sidebarToggle').click();assert shell.locator('.app-shell').evaluate('(e)=>e.classList.contains("sidebar-collapsed")')
    shell.locator('[data-page="trends"]').click();shell.wait_for_timeout(350)
    assert shell.locator('#page-trends.active').count()==1
    frame=shell.frame_locator('#fullscreenTrendFrame');assert frame.locator('.rail').count()==0
    assert shell.locator('#brandLogo').is_visible()
    mount=shell.locator('#fullscreenTrendMount').bounding_box();main=shell.locator('.main').bounding_box();side=shell.locator('#sidebar').bounding_box()
    assert mount['x']>=side['x']+side['width']-3,(mount,side)
    assert mount['width']<=main['width']+3,(mount,main)
    shell.screenshot(path='/mnt/data/PROGNODE_RC6_4_5_HF2_CORE_SHELL_PREVIEW.png',full_page=True)
    print('PASS: original PROGNODE B1 image + native sidebar collapse persisted; Trend Studio mounted inside Core shell')
    browser.close()
