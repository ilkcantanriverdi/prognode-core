/* Core V0.5.8: manual pairing is an explicit admin-only operation. No secrets persist
   in localStorage and none are ever sent to the mobile device before visual SAS check. */
(()=>{'use strict';
const byId=id=>document.getElementById(id);
const manual=byId('pairManualPanel'),qr=byId('pairQrPanel');if(!manual||!qr)return;
let expireAt=0,active=false,requesting=false;
const tr=()=>document.querySelector('#tr')?.classList.contains('selected');
const text=(a,b)=>b;
function clear(){expireAt=0;byId('serverPairingCode').textContent='------';byId('manualSasCode').textContent='---- — ---- — ----';byId('serverPairingExpiry').textContent='—';byId('manualAdvanced').classList.add('hidden');byId('manualAdvancedToggle').setAttribute('aria-expanded','false');byId('manualFullPin').textContent='Not requested';}
function tab(which){active=which==='manual';manual.classList.toggle('hidden',!active);qr.classList.toggle('hidden',active);
  for (const [id,on] of [['pairModeQr',!active],['pairModeManual',active]]) {const el=byId(id);el.classList.toggle('active',on);el.setAttribute('aria-selected',String(on));}}
function token(){return sessionStorage.getItem('prognode.accessSession')||'';}
async function request(path,method='GET'){
 const res=await fetch(path,{method,credentials:'same-origin',cache:'no-store',headers:{'X-PROGNODE-Session':token()}});
 const body=await res.json().catch(()=>({}));if(!res.ok)throw Error(body.code||body.message||`HTTP ${res.status}`);return body;
}
async function cancel(){clear();try{await request('/api/client/manual-pairing/cancel','POST');}catch{ /* no secrets cached */ }}
async function generate(){if(requesting)return;requesting=true;const renewing=expireAt>0;clear();byId('manualRefresh').disabled=true;
 byId('manualStatus').textContent='Checking actual Core TLS and administrator session…';
 try{if(renewing)await request('/api/client/manual-pairing/cancel','POST');const b=await request('/api/client/manual-pairing');
  byId('serverPairingCode').textContent=b.pairingCode;byId('manualSasCode').textContent=b.certificateCheckCode;expireAt=Date.now()+1000*(b.expiresInSeconds||120);
  byId('manualStatus').textContent='Compare the PHONE-computed 12 characters with this trusted PC screen; cancel on any mismatch.';
 }catch(e){byId('manualStatus').textContent=e.message||String(e);}
 finally{requesting=false;byId('manualRefresh').disabled=false;}}
byId('pairModeQr').addEventListener('click',()=>{if(active)void cancel();tab('qr');});
byId('pairModeManual').addEventListener('click',()=>{tab('manual');if(!expireAt)void generate();});
byId('manualRefresh').addEventListener('click',generate);
byId('manualCancel').addEventListener('click',()=>{void cancel();byId('manualStatus').textContent='Pairing code cancelled.';});
byId('manualAdvancedToggle').addEventListener('click',async()=>{
 const box=byId('manualAdvanced'),open=box.classList.contains('hidden');box.classList.toggle('hidden',!open);byId('manualAdvancedToggle').setAttribute('aria-expanded',String(open));
 if(open){try{const b=await request('/api/client/manual-pairing/certificate');byId('manualFullPin').textContent=b.certificateSha256;}catch(e){byId('manualFullPin').textContent=e.message||String(e);}}});
setInterval(()=>{if(!expireAt)return;const left=Math.max(0,Math.ceil((expireAt-Date.now())/1000));byId('serverPairingExpiry').textContent=left?`${left}s remaining · single use`:'Expired · Refresh';if(!left){clear();byId('manualStatus').textContent='Code expired. Create a new one.';}},1000);
const signout=byId('accountSignOut');signout?.addEventListener('click',()=>{clear();tab('qr');});
tab('qr');
})();
