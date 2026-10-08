/* HF6.4 mobile authority: never send a device ACK grant before explicit admin Save. */
(function(){
  const $ = id=>document.getElementById(id);
  const txt=(en,tr)=>en;
  const list=$('hf64PairedDevices');
  if(!list) return;
  const create=(name,cl,text)=>{let e=document.createElement(name);if(cl)e.className=cl;
    if(text!==undefined)e.textContent=text;return e;};
  async function req(url,opts={}){
    const session=sessionStorage.getItem('prognode.accessSession') || '';
    const headers={'Content-Type':'application/json',...opts.headers};
    if(session)headers['X-PROGNODE-Session']=session;
    const r=await fetch(url,{...opts,headers,cache:'no-store'});
    if(!r.ok){let b={};try{b=await r.json()}catch{};
      throw new Error(b.message||b.code||('HTTP '+r.status));}
    return r.status===204?null:r.json();
  }
  async function load(){
    list.replaceChildren(create('p','hf64-msg','Loading paired devices...'));
    let items;try{items=(await req('/api/mobile/v2/admin/paired-devices')).items||[]}
    catch(e){list.replaceChildren(create('p','hf64-msg',e.message));return;}
    if(!items.length){list.replaceChildren(create('p','hf64-msg','No paired devices yet.'));return;}
    list.replaceChildren();
    items.forEach(d=>{
      const card=create('div','hf64-device');
      const info=create('div');
      const name=create('input','hf64-device-name'); name.value=d.name||'';
      name.maxLength=128; name.setAttribute('aria-label','Display name');
      const desc=create('small','',`${d.platform||'Mobile'} · ${d.clientId} · Last seen: ${d.lastSeenAtUtc||'—'}`);
      const pub=create('small','',d.devicePublicKey?'QR device public key registered':'No device key · re-pair for device ACK');
      info.append(name,desc,pub);
      const fields=create('div','hf64-access-fields');
      const makeCheck=(label,checked)=>{let w=create('label');let input=create('input');
        input.type='checkbox';input.checked=checked;w.append(input,document.createTextNode(' '+label));
        fields.append(w);return input;};
      const view=makeCheck('View alarms',d.canViewAlarms!==false);
      const ack=makeCheck('Allow ACK from this device',!!d.canAcknowledge);
      const mode=create('select');
      for(const val of ['USER_SESSION','DEVICE']){let o=create('option','',val==='DEVICE'?'Device-only ACK':'User session');o.value=val;mode.append(o)}
      mode.value=d.ackAuthMode==='DEVICE'?'DEVICE':'USER_SESSION';
      fields.append(mode);
      const save=create('button','secondary','Save access');save.type='button';
      const msg=create('p','hf64-msg','');
      save.addEventListener('click',async()=>{
        if(ack.checked && !view.checked){msg.textContent='View alarms required for ACK';return;}
        if(ack.checked && mode.value==='DEVICE' && !d.devicePublicKey){msg.textContent='Re-pair this phone with a device key.';return;}
        if(!window.confirm('Apply mobile device permissions for '+name.value+'?'))return;
        save.disabled=true;
        try{await req('/api/mobile/v2/admin/paired-devices/'+encodeURIComponent(d.clientId)+'/access',{
            method:'PUT',body:JSON.stringify({displayName:name.value,canViewAlarms:view.checked,
               canAcknowledge:ack.checked,ackAuthMode:mode.value})});
          msg.textContent='Saved. Device can retrieve updated access without re-pairing.';
        }catch(e){msg.textContent=e.message}finally{save.disabled=false}
      });
      const revoke=create('button','ghost-button','Revoke device');revoke.type='button';
      revoke.addEventListener('click',async()=>{
        if(!window.confirm('Revoke this paired device? It will need a new QR.'))return;
        revoke.disabled=true;
        try{await req('/api/client/paired/'+encodeURIComponent(d.clientId),{method:'DELETE'});
          await load();
        }catch(e){msg.textContent=e.message;revoke.disabled=false;}
      });
      fields.append(save,revoke,msg);card.append(info,fields);list.append(card);
    });
  }
  window.hf64LoadDevices=load;
  $('hf64RefreshDevices')?.addEventListener('click',load);
  new MutationObserver(()=>{if(list.offsetParent!==null)void load()})
    .observe(document.documentElement,{attributes:true,attributeFilter:['lang']});
  document.querySelectorAll('[data-open-settings="general"],[data-settings-tab="general"]')
    .forEach(el=>el.addEventListener('click',()=>void load()));
})();
