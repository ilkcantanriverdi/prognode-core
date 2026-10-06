# PROGNODE RC6.4.7 HF4.1 — Schannel 36870 / TLS özel anahtar onarımı

**Kaynak/DEV sürümü.** HF4 üzerine mobil ekibin 27.09.2026 P0 incelemesi uygulanmıştır.
HF3+ Trend Studio, QR v1 eşleştirme, mobil v2 ACK ve HF3 Backup Center korunmuştur.
Mevcut eşleşmiş telefonlar, imzalı lisans, data/ ve `server-access.json` ASLA sıfırlanmaz.

## Neden HF4.1?

`HasPrivateKey=True` ve TCP 5443 LISTEN, servis hesabının CNG/CSP özel anahtarına
ulaşabildiğini veya TLS el sıkışmasının tamamlandığını kanıtlamaz. Önceki saha
vakası Windows Schannel **36870 / 0x8009030D** bu durumdur. HF4 yalnızca pin +
port üzerinden `httpsReady` gösterebiliyordu.

## Bu sürümdeki değişiklikler

- HF4 UAC yardımcısı yalnız yerel, gerçek Core işleminin sahibi SID'ini tespit
  eder. Hizmet açıksa `PROGNODECore` PID'sini kullanır; geliştirme modunda
  doğrulanmış yerel 5080 dinleyicisinin `Prognode.Host` işlem kimliğini alır.
  Kimlik çözümlenemezse **hiçbir key ACL değiştirilmez**.
- Mevcut LocalMachine sertifikasının CNG veya RSA legacy CSP *machine-key* dosyası
  saptanır ve **yalnız tespit edilen SID'e Read ACE** eklenir. `Everyone`, tüm
  kullanıcılar veya sertifika dışa aktarma kullanılmaz. PFX kullanılıyorsa PFX
  anahtarının ACL'si otomatik değiştirilmez; ayrı ve açık IT onarımı gerekir.
- Mevcut sertifika DER SHA-256 parmak izi kurulum öncesi/sonrası korunur;
  yalnız hiç sertifika olmayan ilk kurulumda dışa aktarılamayan yeni sertifika
  oluşturulur. Kayıtlı eski telefonların pini değiştirilmez.
- Çalışan Windows servisi UAC onayı sonrasında yeniden başlatılır ve HTTPS
  `127.0.0.1:5443/api/server/identity` gerçek **pinned TLS + HTTP 200** ile
  sınanır. Geliştirme Core'u varsa kullanıcı manuel yeniden başlatır.
- Core'un kendi proses kimliği altında **özel anahtarla imza atma + pinned
  self-signed TLS handshake + doğru serverId** kontrolü olmadan
  `httpsReady=true` dönmez. Hata kodları: `TLS_PRIVATE_KEY_ACCESS_DENIED`,
  `TLS_HANDSHAKE_FAILED`, `TLS_PIN_MISMATCH` vb. Sağlıksız HTTPS ile QR üretimi ve
  telefon probu engellenir.
- 5 dakikalık telefon testinin başarı sayılması için gerçek uzaktaki IP'nin
  seçilmiş NIC/IP üzerindeki **PROGNODE tarafından yönetilen sağlıklı firewall
  kuralının izinli IP/subnet kapsamına girmesi** şarttır. Localhost testi tek
  başına mobil erişim doğrulaması değildir.

## GÜVENLİ deneme sırası

1. Son çalışan HF3/HF4 Core'dan `.pgnbackup` ve mevcut canlı data/ yedeği alın.
   Aynı `Prognode__DataRoot` yolunu koruyun. Eski `server-access.json`, lisans ve
   LocalMachine HTTPS sertifikasını silmeyin. Yeni kaynak dizini eski boş
   veritabanıyla başlatmayın.
2. `.NET 10 SDK` olan Windows PC'de ayrı kaynak dizininde
   `START_PROGNODE_RC6_4_7_HF4_1_TLS_REPAIR.cmd` çalıştırın. Core'un gerçekten
   `rc6.4.7-hf4.1-lan-tls-repair` sürümünü yayınladığını `/api/health` üzerinden
   kontrol edin. Mevcut servisi kaynak başlatıcısı değil, kurumun servis dağıtım
   süreci güncellemelidir.
3. Yalnız Core bilgisayarında ADMIN girişinden **Ayarlar → Mobil Erişim** içinde
   Public Wi-Fi gibi doğru LAN NIC'ini, doğru IP'yi ve telefon(lar)ın IP'sini
   veya tesis subnet'ini seçin. Public için ayrıca açık onay verin.
4. Hazırla → yerel Windows Agent tepsisinde `Complete LAN Access Setup (UAC)` →
   fiziksel UAC onayı. Mevcut sertifikanın pin'i korunur. Sertifika ACL'leri
   yalnız bu yerel yükseltilmiş yardımcının çalışmasıyla değişir.
5. Yeniden başlatma sonrası Core **Doğrula** ekranında `TLS_OK`, `HTTPS: TLS
   VERIFIED`, `firewallRuleHealth=HEALTHY` durumunu görün. Değilse QR
   üretimine devam etmeyin. `Get-WinEvent` ile Schannel 36870 tanısı alın.
6. Telefondan aynı izinli LAN üstünde 5 dakikalık prob URL'sini açın; ardından
   mobil uygulamanın gerçek QR sertifika pin doğrulamasıyla eşleşin. Bir alarmı
   açıp yeni occurrence-specific ACK test edin. Telefonla HTTPS bağlantısı ve
   Core'un sunucu pini **ayrı ayrı doğrulanmalıdır**.

### Test ve kabul

`TEST_PROGNODE_LAN_WIZARD.ps1` ilk çalıştırmada anonim API güvenliğini ve doğru
sürümü test eder. Aynı oturumda `PROGNODE_TEST_SESSION` değişkenine mevcut
geçerli yerel OWNER token'ı verildiyse oturumlu TLS tanısını da kontrol eder;
**token hiçbir rapora konulmamalıdır**. Eski fingerprint'i önceden kaydettiyseniz
`-ExpectedPin '64_HEX'` ile karşılaştırın. Tam saha kabulü için, gerçek telefon
probe başarıyla tamamlandıktan sonra `-StrictAcceptance` çalıştırın.

Windows test VM'sinde ayrıca servis özel anahtarını kasıtlı olarak erişilemez
hale getirerek 36870 regresyonunu **ayrı, kontrollü test sertifikasıyla** tekrar
edin; canlı tesiste private key ACL'sini test için bozmayın. İzinli/izinsiz
telefon IP'si, farklı VLAN/SSID, Public/Private, Tailscale eşzamanlı ve yeniden
başlatma sonrası TLS/QR/ACK kontrol edilmeli.

### Ticari sürüm engeli ve çevre sınırı

Kaynak ZIP'teki `.ps1` **imzasızdır**. `Program Files` içine kurulduğunda
kullanım öncesi geçerli Authenticode imzası ve yazma korumalı ACL zorunludur;
üretim imzalama/kurulum paketlemesi bu kaynak tesliminde yapılmamıştır.
Windows .NET 10 derlemesi, UAC, gerçek Schannel / PFX, gerçek Samsung ve
mobil occurrence ACK **burada çalıştırılmamıştır**. Bu paket üretim onayı değildir.
Eski `PROGNODE HTTPS Samsung Test` firewall kuralı varsa kontrollü bakım
penceresi dışında silmeyin; temiz kabulde manuel kuralın sonucu maskelemesine
izin vermeyin. Ağ profili değişikliği, WAN açılması veya Tailscale adaptörünün
otomatik seçilmesi yapılmaz.
