# PROGNODE RC6.4.7 HF6.3 — LAN otomatik yeniden başlatma / Historian canlı katalog / Trend erişim kontrolü

**Paket türü:** Tam .NET 10 kaynak pilotu; imzalı ticari kurulum EXE'si değildir. Bütün önceki HF6.2 Core, QR, LAN sihirbazı, Backup Center ve Trend kaynakları korunmuştur. HF6.3 için eklenen üç hata düzeltmesi aşağıda açıklanır.

## 1. Müşteriye elle Core kapat-aç yaptırmayan LAN kurulum akışı

- **Kurulu Windows Service:** Var olan LAN sihirbazı, yalnız Core bilgisayarındaki Agent üzerinden ve UAC onayı sonrasında HTTPS sertifikasının özel anahtarına sınırlı okuma izni verir. Aynı UAC yardımcısı `PROGNODECore` servisini zaten yeniden başlatır; yeni sürümde bu davranış korunur. Yeniden açılan servis kalıcı ProgramData sertifikasını kullanır. Kurulum aracı ve Agent ticari dağıtımdan önce imzalanmalıdır.
- **HF6.3 geliştirici kaynak pilotu:** Core'u `START_PROGNODE.cmd` ile açın. Bu başlatıcı artık `START_PROGNODE_RUNTIME.ps1` gözetmenini devreye alır. Yerel Agent-UAC onarımı bittiğinde yöneticiye ait `%ProgramData%\PROGNODE\config\lan-restart-request.json` içine sadece çalışan Core PID'sine yönelik 3 dakikalık yeniden başlatma isteği kaydeder. Core istek/başlangıç zamanını doğrulayarak kendini düzgün kapatır. Gözetmen **aynı Core DLL, veri klasörü ve oturum hesabı** ile otomatik yeniden başlatır; sertifika değiştirilmez. Kullanıcı yeni ZIP, Setup komutu veya PowerShell adımı çalıştırmaz.
- **VS Code/Visual Studio doğrudan debugger:** Özel IDE başlatması `START_PROGNODE.cmd` gözetmenini kullanmıyorsa IDE debug oturumunu kendiliğinden yeniden başlatmak güvenli değildir; geliştirici bir kez yeniden başlatır. Bu müşteri dağıtım şekli değildir.
- **İlk ağ izni:** Fabrika LAN arayüzünü ve izinli IP/aralığını ilk defa seçmek için yerel yönetici UAC onayı gerekir; izinler zaten kayıtlıysa sürüm güncellemelerinde UAC/PowerShell tekrarlanmaz. Bilinmeyen Public NIC/Tailscale veya her ağ kartı otomatik açılmaz. Değişen sertifika pin'i sessizce kabul edilmez.

## 2. Historian sinyalleri, Core'u kapatmadan Trend'de görünür

- Historian ekleme/güncellemeden sonra ana sayfa iframe'e `pgn:historian-changed` bildirir. Trend Studio her ziyaret veya sekmeye dönüşte `/api/historian/configurations` + `/api/tags` kataloglarını `no-store` yeniden çeker. Birbirinin üstüne binen istekler tekilleştirilir; eklenen veya kaldırılan kayıtlar yeni grafik ekleme listesini anında günceller.
- Sekme uzun süre açık kaldığında katalog 15 saniyede bir kontrol edilir; navigasyonda anında yenilenir. Historian API boş dönüyorsa hata açıkça gösterilir; yokmuş gibi örnek veri üretilmez.

## 3. Trend Studio'da girişsiz salt görüntüleme

- Üstte **Salt görüntüleme** bildirimi görünür. Giriş yapılmamış / oturumu süresi dolmuş kullanıcı sinyal, panel yerleşimi, sürükle-bırak ve panel ayarlarını değiştiremez; API'ye yeni layout yazılmaz ve localStorage kaydı yapılmaz. Yetkili oturum `/api/access/status` ve imzalı lisans durumundan doğrulanır. Giriş/çıkış ve periyodik oturum kontrollerinde UI anında güncellenir.
- Mevcut **gerçek veri görüntüleme**, zaman aralığında gezinme, zoom ve alarm inceleme okuma amaçlı kullanılabilir. Her mutating API zaten Core oturumu ve lisansı zorunlu kılar; Backup Layout ayrıca OWNER/ORGANIZATION_ADMIN ve localhost gerektirir.
- ÖNEMLİ: İmzalı lisans içeriğindeki Industrial Operator/Engineer rolleri ayrı yetkilendirme projesidir; bu HF6.3 sürümü kullanıcı rolü bazlı yeni haklar vaat etmez. Kayıtlı düzen kaydetme/sıfırlama şu an Core yönetici hesabı gerektirir.

## Kurulum ve doğrulama

1. Eski `data` klasörünü (lisans, `server-access.json`, `prognode.db`) ve mevcut HTTPS sertifikasını/ProgramData ayarlarını yedekleyin. Eski Core ile aynı anda aynı portları dinlemeyin.
2. Yeni ZIP'i ayrı klasöre açıp `TEST_HF6_3_WINDOWS_SOURCE.ps1` çalıştırın: PowerShell 5.1 sözdizimi + .NET 10 derleme. Bu adım Windows'ta yapılmalıdır.
3. Daha önce kaydettiğiniz kalıcı veri yolu `%LOCALAPPDATA%\PROGNODE\dev-data-root.txt` içindeyse aynen kullanılır. İlk geçişte `SET_PROGNODE_DATA_ROOT.ps1 -Path 'C:\eski\prognode\data'` ile gerçek yolu seçin. Temiz test PC'sinde açıkça `-CreateNew` onayı verin.
4. `START_PROGNODE.cmd` ile pilotu çalıştırın. Settings > Mobile LAN > Verify sonucu `TLS_OK` olmadan QR üretmeyin. Herhangi bir sertifikayı sırf test amacıyla silmeyin/yenilemeyin.
5. İlk ağ onayından sonraki Core yeniden başlatmasını gözetmen yapacak. Core yeniden başladıktan sonra önceki **yerel oturum güvenlik gereği sona erebilir**; bu durumda yalnızca hesaba tekrar giriş yapın. HTTPS kurulumunu/UAC onarımını tekrarlamayın. QR ile telefonun bağlandığını ve occurrence ACK'in çalıştığını gerçek Samsung'da test edin.
6. Historian'a yeni Tag ekleyin; mevcut Trend sekmesine dönüp **tarayıcı/servis yeniden başlatmadan** göründüğünü doğrulayın. Giriş yapmadan Add/Save/Drag devre dışı; giriş yaptıktan sonra etkin; çıkışta anında salt görüntüleme olmalı.

Linux Chromium testleri `python tests/test_hf63_trend_auth_catalog.py` ile (sahte API fixture) yapıldı. **Windows .NET 10 derlemesi, gerçek UAC ve Core servis/PID otomatik yeniden başlama, fiziksel PLC/telefon kabul testi henüz burada yapılamadı.** Bunlar başarıyla tamamlanana kadar müşteri üretimine dağıtmayın.
