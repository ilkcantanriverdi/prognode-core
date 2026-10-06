/* HF6.4 mobile authority: never send a device ACK grant before explicit admin Save. */
(function(){
  const $ = id=>document.getElementById(id);
  const txt=(en,tr)=>document.documentElement.lang==='tr'?tr:en;
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
    list.replaceChildren(create('p','hf64-msg',txt('Loading paired devices...','Eşleştirilmiş cihazlar yükleniyor...')));
    let items;try{items=(await req('/api/mobile/v2/admin/paired-devices')).items||[]}
    catch(e){list.replaceChildren(create('p','hf64-msg',e.message));return;}
    if(!items.length){list.replaceChildren(create('p','hf64-msg',txt('No paired devices yet.','Henüz eşleştirilmiş cihaz yok.')));return;}
    list.replaceChildren();
    items.forEach(d=>{
      const card=create('div','hf64-device');
      const info=create('div');
      const name=create('input','hf64-device-name'); name.value=d.name||'';
      name.maxLength=128; name.setAttribute('aria-label',txt('Display name','Cihaz adı'));
      const desc=create('small','',`${d.platform||'Mobile'} · ${d.clientId} · Last seen: ${d.lastSeenAtUtc||'—'}`);
      const pub=create('small','',d.devicePublicKey?txt('QR device public key registered','QR cihaz açık anahtarı kayıtlı'):txt('No device key · re-pair for device ACK','Cihaz anahtarı yok · Cihaz ACK için yeniden eşleştirin'));
      info.append(name,desc,pub);
      const fields=create('div','hf64-access-fields');
      const makeCheck=(label,checked)=>{let w=create('label');let input=create('input');
        input.type='checkbox';input.checked=checked;w.append(input,document.createTextNode(' '+label));
        fields.append(w);return input;};
      const view=makeCheck(txt('View alarms','Alarmları görüntüle'),d.canViewAlarms!==false);
      const ack=makeCheck(txt('Allow ACK from this device','Bu cihazdan ACK yetkisi ver'),!!d.canAcknowledge);
      const mode=create('select');
      for(const val of ['USER_SESSION','DEVICE']){let o=create('option','',val==='DEVICE'?txt('Device-only ACK','Oturumsuz cihaz ACK'):txt('User session','Kullanıcı oturumu'));o.value=val;mode.append(o)}
      mode.value=d.ackAuthMode==='DEVICE'?'DEVICE':'USER_SESSION';
      fields.append(mode);
      const save=create('button','secondary',txt('Save access','Yetkiyi kaydet'));save.type='button';
      const msg=create('p','hf64-msg','');
      save.addEventListener('click',async()=>{
        if(ack.checked && !view.checked){msg.textContent=txt('View alarms required for ACK','ACK için alarm görüntüleme yetkisi gerekir');return;}
        if(ack.checked && mode.value==='DEVICE' && !d.devicePublicKey){msg.textContent=txt('Re-pair this phone with a device key.','Bu telefonu cihaz anahtarıyla yeniden eşleştirin.');return;}
        if(!window.confirm(txt('Apply mobile device permissions for ','Mobil cihaz yetkileri uygulansın mı: ')+name.value+'?'))return;
        save.disabled=true;
        try{await req('/api/mobile/v2/admin/paired-devices/'+encodeURIComponent(d.clientId)+'/access',{
            method:'PUT',body:JSON.stringify({displayName:name.value,canViewAlarms:view.checked,
               canAcknowledge:ack.checked,ackAuthMode:mode.value})});
          msg.textContent=txt('Saved. Device can retrieve updated access without re-pairing.','Kaydedildi. Yeniden eşleştirme gerekmeden cihaz güncel yetkileri alabilir.');
        }catch(e){msg.textContent=e.message}finally{save.disabled=false}
      });
      const revoke=create('button','ghost-button',txt('Revoke device','Cihaz eşleştirmesini iptal et'));revoke.type='button';
      revoke.addEventListener('click',async()=>{
        if(!window.confirm(txt('Revoke this paired device? It will need a new QR.','Bu cihazı iptal etmek istiyor musunuz? Yeni QR gerekecektir.')))return;
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
