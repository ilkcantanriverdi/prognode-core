/* Customer V2: only client-side shell & view composition.  The existing app.js
   remains the sole owner of data, sessions, QR, alarm ACK and backup operations. */
(()=>{'use strict';
const byId=id=>document.getElementById(id);
const lang=()=>'en';
const words={
  en:{overview:'Overview',devices:'Devices',tags:'Tags',alarms:'Alarms',trends:'Trend Studio',historian:'Historian',notifications:'Notifications',license:'License',settings:'Settings',diagnostics:'Diagnostics',running:'Local Core online',offline:'Core unavailable',awaiting:'Checking Core',done:'Done',pending:'Pending',configured:'Recording enabled',unconfigured:'Set up local signal recording',live:'Local monitoring'}
};
function locale(){return words[lang()];}
function navigateTo(page){const nav=document.querySelector('.nav-item[data-page="'+page+'"]');if(nav)nav.click();else if(typeof navigate==='function')navigate(page);}
function updateLocale(){const l=lang();document.querySelectorAll('[data-customer-en]').forEach(el=>{const v=el.getAttribute('data-customer-'+l);if(v!==null)el.textContent=v;});const pg=document.querySelector('.nav-item.active')?.dataset.page||'overview';byId('customerPageName').textContent=locale()[pg]||'PROGNODE';}
function settingsHome(){byId('customerSettingsLanding').hidden=false;byId('customerSettingsDetail').hidden=true;}
function settingsOpen(name){byId('customerSettingsLanding').hidden=true;byId('customerSettingsDetail').hidden=false;const tab=document.querySelector('[data-settings-tab="'+name+'"]');if(tab)tab.click();const h=byId('customerSettingsDetail');if(h && h.getBoundingClientRect().top<0)h.scrollIntoView({block:'start',behavior:'auto'});}
for(const btn of document.querySelectorAll('[data-customer-page]')) btn.addEventListener('click',()=>navigateTo(btn.dataset.customerPage));
for(const btn of document.querySelectorAll('[data-open-settings]')) btn.addEventListener('click',()=>settingsOpen(btn.dataset.openSettings));
byId('customerSettingsBack').addEventListener('click',settingsHome);
byId('customerDiagnostics').addEventListener('click',()=>navigateTo('diagnostics'));
byId('customerNotification').addEventListener('click',()=>navigateTo('notifications'));
byId('customerOpenTrends').addEventListener('click',()=>navigateTo('trends'));
byId('customerAddFirstDevice').addEventListener('click',()=>{const licenseReady=!!(window.__customerV2State?.licensed);if(!licenseReady) navigateTo('license'); else byId('opAddDevice')?.click();});
byId('customerGuidedSetup').addEventListener('click',()=>{if(typeof openFirstRunQuickStart==='function')openFirstRunQuickStart(true);});
window.customerV2Refresh=function(s){if(!s)return;window.__customerV2State=s;
  const pg=byId('page-overview');pg.classList.toggle('customer-has-devices',s.devices>0);
  byId('customerIntroEmpty').hidden=s.devices>0;byId('customerIntroReady').hidden=s.devices===0||s.recording>0;byId('customerEmptyBottom').hidden=s.devices>0;
  byId('customerDeviceCount').textContent=s.devices.toLocaleString('en-US');
  byId('customerAlarmCount').textContent=s.alarms.toLocaleString('en-US');
  byId('customerAlarmCount').closest('.customer-kpi')?.classList.toggle('has-alerts',s.alarms>0);
  byId('customerRecordingCount').textContent=s.recording?s.recording.toLocaleString('en-US'):'—';
  byId('customerHistorianHelp').textContent=s.recording?locale().configured:locale().unconfigured;
  const mark=(id,on)=>{const el=byId(id);el.textContent=on?locale().done:locale().pending;el.classList.toggle('done',!!on)};
  mark('customerStepLicense',s.licensed);mark('customerStepDevice',s.devices>0);mark('customerStepSignals',s.tags>0);
  byId('customerCoreText').textContent=s.coreHealthy?locale().running:locale().offline;
  byId('customerLiveStatus').textContent=s.coreHealthy?locale().live:locale().offline;
  byId('customerCoreDot').classList.toggle('ok',!!s.coreHealthy);
  byId('customerCoreDot').classList.toggle('offline',!s.coreHealthy);
};
// The existing app.js publishes page changes and language changes; do not patch it a second time.
window.addEventListener('prognode:pagechange',e=>{const pg=e.detail?.page||'overview';byId('customerPageName').textContent=locale()[pg]||'PROGNODE';if(pg==='settings')settingsHome();});
window.addEventListener('prognode:languagechange',()=>{updateLocale();if(window.__customerV2State)window.customerV2Refresh(window.__customerV2State);});
updateLocale();
})();
