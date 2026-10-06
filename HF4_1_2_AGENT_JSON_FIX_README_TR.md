# PROGNODE RC6.4.7 HF4.1.2 — LAN Agent boş JSON/UAC hotfix

**Başlangıç paketi:** HF4.1.1 tam kaynak; QR, HF3+ Trend Studio, Backup Center,
LAN erişim sihirbazı ve HF4.1 TLS özel anahtar onarımı korunur.

## Tespit edilen hata

Windows Agent'ın `GetFromJsonAsync<Pending>` çağrısı, `/api/mobile-access/agent/pending`
boş cevap döndürdüğünde JSON ayrıştırma hatası veriyordu. Özellikle üç dakikalık
*Prepare* isteği bittiğinde `Results.Ok(null)` bazı ASP.NET Core sürümlerinde
HTTP 200 ve sıfır uzunluklu cevap verebilir. Benzer hata, boş `claim` cevabı veya
Agent'ın yanlış/eski yerel Core'a bağlanmasında da görülebilir.

**Düzeltme:** Bekleyen istek yoksa Core artık JSON gövdeli `404 NO_PENDING_LAN_REQUEST`
döndürür. Agent eski Core'un 200/boş cevabını, JSON `null` ve 204'ü de güvenle
yönetir; ham JSON istisnası yerine yeni *Prepare* talimatı gösterir.
`claim` cevabı da boş/geçersiz JSON'a dayanıklı hâle getirildi. Endpoint yalnız
loopback'ten erişilebilir; işlemin fiziksel tray onayı, UAC ve mevcut PIN/TLS
güvenlik sınırları değiştirilmedi.

## Güncelleme / aynı hatadan kaçınma

1. **Önce eski PROGNODE Windows tray Agent'ı kapatın:** görev çubuğu bildirim
   alanında PROGNODE > `Exit PROGNODE Agent`. Aynı bilgisayarda başka eski Agent
   örneği kalmasın. Yeni Core ile eski Agent'ın birlikte çalışması düzeltmeyi
   görünmez kılabilir.
2. Mevcut `data/`, `.pgnlicense`, `server-access.json`, HTTPS sertifikası ve eşlenmiş
   telefonları yedekleyin; silmeyin. Yeni ZIP ayrı klasöre açıldıysa
   `Prognode__DataRoot` değişkenini **mevcut veri klasörüne** işaret edecek şekilde
   ayarlayın. Başlatıcının varsayılanı yeni ZIP klasöründeki `data` dizinidir.
3. Yeni klasörde `START_PROGNODE_RC6_4_7_HF4_1_2.cmd` çalıştırın (Windows .NET 10
   SDK gerekli). Başlatıcı eski Agent açıksa derlemeden önce durur ve onu
   kapatmanızı ister; yanlış sürüm Core'un 5080'de kalmasına izin vermez.
4. `http://127.0.0.1:5080/api/health` içindeki `coreVersion` tam olarak
   `0.7.2-rc6.4.7-hf4.1.2-agent-json-fix` olmalıdır.
5. `TEST_HF4_1_2_AGENT_JSON.ps1` betiğini çalıştırın. Aktif *Prepare* yoksa
   `/api/mobile-access/agent/pending` JSON gövdeli **404** döndürür.
6. Core'da **Settings → Mobile Access**: doğru Wi-Fi/telefon IP adresini seçin;
   Public profil onay kutusunu işaretleyin; **Prepare LAN access** seçin.
   Üç dakika dolmadan **yeni** tepsi Agent'ından `Complete LAN Access Setup (UAC)`
   seçin; Windows UAC'yi fiziksel Core PC'de onaylayın.
7. **Verify / refresh**, gerçek `TLS_OK`, pin'li localhost TLS ve telefondan HTTPS
   probe olmadan QR hazır kabul edilmez. Schannel 36870/CryptographicException
   devam ederse HF4.1 özel anahtar izin onarımı ayrıca doğrulanmalıdır.

## Test sınırı

Bu ortamda Windows / .NET 10 / UAC / gerçek Samsung testi çalıştırılamadı.
Kaynak ve ZIP yapısı kontrol edilmiştir. Ticari dağıtım için yükseltilmiş
PowerShell yardımcısının Authenticode imzası ve yönetici ACL'si gerekir.
