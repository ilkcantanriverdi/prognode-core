# PROGNODE RC6.4.7 HF4 — Mobil LAN Erişim Sihirbazı, P0 inceleme

**İncelenen paket:** `PROGNODE_RC6_4_7_HF4_LAN_WIZARD_FULL.zip` (27.09.2026)

**İncelemenin kapsamı:** ZIP içindeki kaynak kod ve kurulum betiklerinin statik incelemesi. Windows üzerinde .NET derlemesi, UAC, gerçek TLS el sıkışması, gerçek Samsung/QR/ACK testi bu incelemede yapılmadı.

## Gerçekleşen iyileştirmeler

- Core arayüzünde ağ kartı seçimi, Public/Private ağ profili onayı, seçilmiş telefon IP'leri veya fabrika alt ağı, önizleme ve telefon bağlantı testi için altyapı bulunuyor.
- Ağ kuralını HTTP isteği değil, Windows Agent sistem tepsisinden fiziki onayla başlatılan UAC yardımcısı oluşturuyor.
- Kural tek ağ arayüzü, yerel IP, Public/Private profil, onaylı kaynak adresler ve TCP 5443 ile sınırlandırılıyor.
- Var olan HTTPS sertifikası/parmak izi sessizce değiştirilmiyor; QR ve v2 alarm ACK sözleşmeleri korunuyor.

## P0: Önceki TLS hatası henüz otomatik onarılmıyor

**Önceki gerçek saha hatası:** Windows Schannel 36870, `0x8009030D`, `dotnet` işlemi mevcut `LocalMachine\\My` HTTPS sertifikasının özel anahtarını TLS sırasında kullanamıyor. Sertifika `HasPrivateKey=True` ve 5443 TCP LISTEN olsa da `curl -vk https://127.0.0.1:5443/api/server/identity` TLS el sıkışmasında başarısızdı.

HF4'ün `LAN_ACCESS_ELEVATED.ps1` dosyası var olan sertifika için `HasPrivateKey` ve son kullanma tarihini denetliyor; Windows Core/servis hesabının o özel anahtarı gerçekten okuyabildiğini kontrol etmiyor, o hesaba dar kapsamlı okuma yetkisi vermiyor. `LanAccessWizardService.Status()` içindeki `httpsReady`, sertifika parmak izi ve dinleme portuna dayanıyor; gerçek TLS el sıkışması yapmıyor. Bu nedenle HF4, önceki 36870 hatası devam ederken yanlışlıkla 'HTTPS hazır' gösterebilir; telefon bağlantı testi yine başarısız olabilir.

**Core ekibinden istenen HF4.1 düzeltme:**

1. Core'u çalıştıran gerçek Windows kimliğini (Windows Service ise servis kimliğini/SID; geliştirici modunda süreci çalıştıran kullanıcıyı) belirleyin.
2. Mevcut sertifikanın CNG veya legacy CSP özel anahtar depolamasını belirleyin. Sertifikanın özel anahtarını döndürmeden ilgili mevcut anahtar dosyasına sadece bu kimlik için gerekli **Read** iznini verin. Geniş `Everyone` / tüm kullanıcılar izni veya dışa aktarılabilir yeni sertifika kullanmayın. ACL düzenlemesini yalnız yerel UAC-onaylı, imzalı, korumalı kurulum yardımcısı yapmalı.
3. Sertifika kullanım izni ve TLS el sıkışmasını **aynı Core servis kimliği** altında doğrulayın. Mevcut sertifika SHA-256 pin'ini koruyun; hata durumunda açık tanı kodu gösterin ve QR'ı kullanıma hazır olarak işaretlemeyin.
4. `httpsReady` ve başarılı kurulum ölçütünü gerçek `https://127.0.0.1:5443/api/server/identity` TLS cevabıyla doğrulayın; yalnız TCP bağlantısı ve `HasPrivateKey` yeterli değildir. Self-signed sertifika için test istemcisi, sertifikayı atlamak yerine *beklenen SHA-256 pin'ini* doğrulamalı.
5. Yerel test başarılı olduktan sonra **gerçek telefon IP'sinden** alınan 5 dakikalık HTTPS probe ile LAN firewall/SSID/VLAN durumunu ayrı doğrulayın. Gerçek mobil QR eşleştirmesi ve occurrence-specific ACK ile tamamlayın.
6. `TEST_PROGNODE_LAN_WIZARD.ps1` ve doğrulama çıktısına Schannel 36870 regresyonunu ekleyin: sertifikası var ama özel anahtara erişemeyen servis, izin onarımı, değişmeyen fingerprint, yeniden başlatmadan sonra başarılı TLS, Public Wi-Fi, izin verilmeyen IP.

## Üretime geçiş için ilave koşullar

- `LAN_ACCESS_ELEVATED.ps1` şu an imzasız kaynak betiği; ticari dağıtımda Authenticode ve yöneticiye ait kurulum ACL'si gerekiyor.
- ZIP yeni klasöre açılıyorsa önceki `data/` klasörünü ve `Prognode__DataRoot` değerini korumadan başlatmayın; aksi hâlde mevcut tag, alarm ve historian verileri yeni boş veri tabanında görünmez.
- Test PC'sindeki manuel `PROGNODE HTTPS Samsung Test` kuralı sihirbazdan bağımsızdır; temiz kabul testinde bu kuralın varlığı sonucu maskeleyebilir. Önce çalışan Core'u ve telefonu kesintisiz tutacak geri dönüş planıyla bu test kuralı yönetici tarafından ayrıca ele alınmalı.
- Mevcut eşleştirilmiş telefonu ve HTTPS sertifikasını yeni paketi test etmek amacıyla silmeyin.

## Karar

HF4'te gerçek LAN erişim sihirbazı kaynak düzeyinde bulunuyor. Ancak kullanıcının önceden doğrulanmış Schannel 36870 vakasını otomatik onardığı henüz gösterilmiyor. **HF4.1 TLS özel anahtar onarımı ve gerçek TLS sağlık testi tamamlanmadan sıfır müdahaleli müşteri kurulumu kabulü verilmemeli.**

### İncelenen kaynaklar

- `LAN_ACCESS_WIZARD_README_TR.md`
- `LAN_ACCESS_ELEVATED.ps1`
- `src/Prognode.Web/LanAccessWizardService.cs`
- `src/Prognode.Host/Program.cs`
- `src/Prognode.Agent.Windows/LanAccessSetup.cs`
- `TEST_PROGNODE_LAN_WIZARD.ps1`
- `VALIDATION_HF4_LAN_WIZARD.txt`
