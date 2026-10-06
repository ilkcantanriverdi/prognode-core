from pathlib import Path
from playwright.sync_api import sync_playwright
html=(Path(__file__).resolve().parents[1]/'TREND_STUDIO_RC6_4_3_INTERACTIVE_DEMO.html').read_text(encoding='utf-8')
with sync_playwright() as p:
    browser=p.chromium.launch(executable_path='/usr/bin/chromium',headless=True,args=['--no-sandbox','--disable-dev-shm-usage'])
    page=browser.new_page(viewport={'width':1600,'height':1000})
    errors=[];page.on('pageerror',lambda e:errors.append(str(e)))
    page.set_content(html,wait_until='load');page.wait_for_timeout(300)
    assert page.locator('canvas[data-studio-lane]').count()==2, 'separate lanes missing'
    assert page.locator('#navTrendCount').inner_text()=='2', 'unsaved active charts not counted'
    page.locator('[data-studio-tag="flow"]').dispatch_event('click')
    assert page.evaluate('PrognodeStudio.selected.length')==2, 'unenrolled tag incorrectly selected'
    assert 'Historian' in page.locator('#demoToast').inner_text(), 'disabled tag guidance missing'
    print('PASS historian membership gating and active chart counter')
    page.locator('#studioCursorA').click()
    assert not page.evaluate('PrognodeStudio.live'), 'cursor should pause live'
    temp=page.locator('canvas[data-studio-lane="temp"]')
    bbox=temp.bounding_box();temp.click(position={'x':bbox['width']*.38,'y':bbox['height']*.5})
    page.locator('#studioCursorB').click()
    bbox=temp.bounding_box();temp.click(position={'x':bbox['width']*.68,'y':bbox['height']*.5})
    assert 'Δt' in page.locator('#studioCompareBar').inner_text()
    print('PASS A/B pause and comparison')
    page.locator('button.studio-lane-event').first.click()
    assert page.locator('#studioEventDetail').is_visible(), 'alarm detail missing'
    page.locator('#studioFlagToggle').uncheck()
    assert page.locator('#studioEventDetail').is_visible(), 'flag toggle erased selected alarm detail'
    print('PASS independent alarm flags and retained event detail')
    page.locator('#studioModeCompare').click()
    assert not page.locator('#studioChartShell').evaluate('(e)=>e.classList.contains("hidden")'), 'compare main chart hidden'
    assert page.locator('#studioViewHint').inner_text().find('karşılaştır')>=0
    page.locator('#studioModeSeparate').click()
    assert page.locator('#studioChartShell').evaluate('(e)=>e.classList.contains("hidden")')
    print('PASS separate/compare display mode')
    page.locator('#studioLive').click()  # resume
    assert page.evaluate('PrognodeStudio.live')
    for preset in [5,15,60,480,1440,10080]:
        page.locator(f'[data-range-value="{preset}"]').click()
        val=page.evaluate('({v:document.querySelector("#trendRange").value,range:PrognodeStudio.range})')
        assert val['v']==str(preset) and abs((val['range']['to']-val['range']['from'])/60000-preset)<.05,(preset,val)
    page.locator('#studioLive').click() # pause
    oldto=page.evaluate('PrognodeStudio.range.to')
    page.locator('[data-range-value="15"]').click()
    fixed=page.evaluate('PrognodeStudio.range')
    assert fixed['to']==oldto and abs((fixed['to']-fixed['from'])/60000-15)<.05
    print('PASS 5m/15m/1h/8h/24h/7d live and fixed presets')
    page.locator('#demoTheme').click()
    assert page.evaluate('document.documentElement.dataset.theme')=='light'
    bg=page.locator('#page-trends .studio-main').evaluate('(e)=>getComputedStyle(e).backgroundImage')
    assert 'rgb(255, 255, 255)' in bg,(bg,'light stylesheet ignored')
    print('PASS light theme with white studio surface')
    assert not errors, errors
    print('PASS browser Javascript: no runtime errors')
    browser.close()
