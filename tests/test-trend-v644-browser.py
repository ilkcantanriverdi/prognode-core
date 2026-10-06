from pathlib import Path
from playwright.sync_api import sync_playwright
html=(Path(__file__).resolve().parents[1]/'TREND_STUDIO_RC6_4_4_INTERACTIVE_DEMO.html').read_text(encoding='utf-8')
with sync_playwright() as p:
    browser=p.chromium.launch(executable_path='/usr/bin/chromium', headless=True, args=['--no-sandbox','--disable-dev-shm-usage'])
    page=browser.new_page(viewport={'width':1600,'height':1000})
    errors=[];page.on('pageerror',lambda e:errors.append(str(e)))
    page.set_content(html,wait_until='load');page.wait_for_timeout(900)
    assert page.locator('canvas[data-studio-lane]').count()==2, 'individual graphs must always render'
    assert page.locator('#navTrendCount').inner_text()=='2', 'active chart count wrong'
    assert page.locator('#studioCompareCheck').is_checked() is False, 'comparison must be opt-in'
    assert page.locator('#studioChartShell').is_visible() is False, 'overlay should start hidden'
    assert page.locator('#studioSmoothToggle').count()==0, 'smoothing must not require a toggle'
    assert page.locator('.studio-main-head').count()==0, 'bulky header still present'
    assert page.locator('.studio-tag-row').count()==2, 'unenrolled Tag must not be in explorer'
    names=page.locator('.studio-tag-meta strong').all_inner_texts()
    assert all('Mikser PLC — ' in n for n in names), names
    assert all('4000' not in n and 'Float32' not in n for n in names), names
    assert page.locator('#studioSavedDetails').is_open() is False if hasattr(page.locator('#studioSavedDetails'),'is_open') else not page.locator('#studioSavedDetails').evaluate('(x)=>x.open'), 'saved views should be tucked away'
    print('PASS: separate-by-default, manual Historian membership, clean device/tag labels, compact layout')
    page.locator('#studioCompareCheck').check();page.wait_for_timeout(300)
    assert page.locator('canvas[data-studio-lane]').count()==2
    assert page.locator('#studioChartShell').is_visible(), 'comparison must add a chart, not replace others'
    page.locator('#studioCompareCheck').uncheck();page.wait_for_timeout(180)
    assert page.locator('#studioChartShell').is_visible() is False
    assert page.locator('canvas[data-studio-lane]').count()==2
    print('PASS: compare checkmark adds/removes overlay without removing separate lanes')
    page.locator('#studioCursorA').click()
    assert page.evaluate('PrognodeStudio.live') is False, 'A must pause live'
    t=page.locator('canvas[data-studio-lane="temp"]');rect=t.bounding_box()
    t.click(position={'x':rect['width']*.38,'y':rect['height']*.48})
    page.locator('#studioCursorB').click();t.click(position={'x':rect['width']*.7,'y':rect['height']*.48})
    assert 'Δt' in page.locator('#studioCompareBar').inner_text()
    print('PASS: A/B retains cursor comparison in separate charts')
    page.locator('#studioLive').click()
    assert page.evaluate('PrognodeStudio.live') is True
    for minutes in (5,15,60,480,1440,10080):
        page.locator(f'[data-range-value="{minutes}"]').click();page.wait_for_timeout(70)
        r=page.evaluate('PrognodeStudio.range')
        assert abs((r['to']-r['from'])/60000-minutes)<.05, (minutes,r)
    print('PASS: six quick windows in rolling LIVE')
    page.locator('#demoTheme').click();page.wait_for_timeout(150)
    background=page.locator('#page-trends .studio-main').evaluate('(x)=>getComputedStyle(x).backgroundColor')
    assert background=='rgb(255, 255, 255)', background
    page.locator('#demoLang').click();page.wait_for_timeout(180)
    assert page.locator('.studio-primary-side-head strong').inner_text()=='Signals'
    assert page.locator('#studioCompareCheck').is_checked() is False
    page.locator('#demoLang').click();page.wait_for_timeout(120)
    assert page.locator('.studio-primary-side-head strong').inner_text()=='Sinyaller'
    print('PASS: high-contrast light mode + TR/EN reactive Studio translation')
    assert not errors,errors
    print('PASS: no JavaScript runtime errors')
    browser.close()
