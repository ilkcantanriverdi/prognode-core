# PROGNODE RC6.4.7 HF4 — Mobil LAN Erişim Asistanı

**Temel:** RC6.4.7 HF3 Backup Center (QR + HF3+ Trend Studio + şifreli proje yedeği). Bu kaynak ZIP, mevcut uygulamanın içine LAN Access Wizard'ı ekler; ayrı APK gerektirmez.

## Çözülen saha sorunu

Örnek: Windows Wi-Fi `192.168.1.52` = `Public`, Tailscale = `Private`. Önceki `PROGNODE LAN HTTPS` kuralı yalnız Private açtığı için Samsung `192.168.1.61` aynı Wi-Fi'da olsa bile HTTPS TCP 5443'e erişemiyordu. HTTPS sertifikası veya QR'ı yeniden üretmek tek başına bunu çözmez.

## Kullanım

1. Eski proje `data/`, lisans ve HTTPS sertifika ayarlarını yedekleyin. ZIP'i yeni klasöre açıyorsanız **eski `data/` klasörünü yenisine güvenle kopyalayın** veya `Prognode__DataRoot` ortam değişkenini eski veri klasörüne ayarlayın. Yeni klasörü boş `data/` ile başlatırsanız önceki Tag/Alarm/Historian kayıtları görünmez. `.pgnbackup` ve canlı veri klasörünü asla silmeyin. Kurulu Windows servisinin veri yolu farklıysa eski servisle aynı yolu açıkça koruyun.
2. Windows 10/11 + .NET 10 SDK kurulu bilgisayarda `START_PROGNODE_RC6_4_7_HF4_LAN_WIZARD.cmd` çalıştırın. Eski Core durdurulmadan önce mevcut HF3 pre-upgrade yedek akışı uygulanır. Başarılı derlemeden sonra Windows Agent sistem tepsisinde başlar.
3. Yalnız **Core bilgisayarında** `http://127.0.0.1:5080` üzerinden OWNER/ORGANIZATION_ADMIN oturumuyla **Ayarlar → Mobil Erişim → LAN Erişimini Etkinleştir** bölümüne girin. LAN Wi-Fi/Ethernet arayüzünü elle seçin. Tailscale, VPN, belirsiz profiller otomatik seçilemez.
4. Telefon IP'lerini belirleyin (`192.168.1.61` vb.; en fazla 8) **veya** onaylı tesis alt ağını seçin. Public arayüz için ayrı güvenlik onayını işaretleyin; Windows ağ profilini Private'a çevirmeyin.
5. **LAN erişimini hazırla** ile seçilen NIC, profil, tek yerel IP, kaynak aralığı ve TCP 5443 önizlemesini oluşturun. 3 dakika içinde **Core PC'nin Windows Agent tepsi ikonuna sağ tıklayıp** `Complete LAN Access Setup (UAC)` menüsünü açın. Yerel özet penceresinde onay verin, Windows yönetici/UAC onayını geçin. Bu adım uzaktan web isteğiyle veya yalnızca tarayıcıyla yükseltilemez.
6. Yardımcı, sadece ilgili `PROGNODE-MOBILE-LAN-<NIC index>` isimli firewall kuralını üretir/onarır. Varsa HTTPS sertifikasını aynen kullanır; ilk defaysa LocalMachine'e dışa aktarılamayan sertifika oluşturur. **Yeni sertifika/HTTPS ilk defa etkinleştirildiyse Core'u bir defa yeniden başlatın.** Mevcut thumbprint/pin sessizce değiştirilmez.
7. **Doğrula / yenile** ile kural ve yerel HTTPS durumunu kontrol edin. **Telefondan test et** seçeneği 5 dakikalık test URL'si verir. Telefonu aynı izinli Wi-Fi/LAN'a bağlayıp kendi tarayıcısından bu URL'yi açın. Core, gerçek telefon IP'sinden gelen TLS isteğini ayrı doğrular. Sertifika Chrome tarafından güvenilir değilse tarayıcı uyarısı olabilir; gerçek mobil uygulama QR SHA-256 pin doğrulamasını ayrıca yapar. Yerel portun çalışması telefon erişiminin kanıtı değildir.
8. **QR eşleştirmeye geç** seçeneğinde gerçek LAN IP'sini kullanın. QR 120 saniye, tek kullanımlıktır; UDP 5081 keşfi gerekmez. LAN eşleştirmesi ücretli Remote koltuğu tüketmez. Mobile v2 ACK değişmez.

## Güvenlik ve sınırlamalar

- **Dışa açılan port yalnız TCP 5443**; tek NIC adı + IP + mevcut Public/Private profil + onaylı kaynak IP/subnet; edge traversal ve WAN port forwarding kapalı. UDP 5081 bu sürümde açılmaz.
- UAC aracı yalnız Windows Agent menüsünden **fiziksel kullanıcı onayı** sonrası çalışır. Kaynak paketindeki `.ps1` imzasızdır; üretim dağıtımında Authenticode ile imzalanmalı ve uygulama dosyaları yetkisiz kişilerin değiştiremeyeceği şekilde kurulmalıdır. Bu kaynak/DEV dağıtımının imzalı üretim kurulumuyla karıştırılmaması gerekir.
- Önceki `PROGNODE LAN HTTPS` **Private** kuralı olası diğer Ethernet istemcilerini kesmemek için otomatik silinmez. Kapsamını IT ile gözden geçirin; yeni sihirbaz eski Private kuralını genişletmez. Yeni uygulama kuralı ayrıdır.
- Telefon başka SSID/VLAN'daysa ya da AP Client Isolation/GPO izin vermiyorsa uygulama bu korumaları aşmaz. Sihirbazdan NIC, kaynak IP/subnet ve TCP 5443 bilgilerini IT'ye iletin.
- Network IP/profil veya sertifika değişirse yeniden onay/onarıma gerek olabilir. `Disable LAN Access` yalnız uygulamanın oluşturduğu kuralı siler, diğer kuralları korur. Uninstall uygulama-sahipli kuralları temizler.
- Project Backup Center, Trend Studio, Historian, alarm/notification v2, lisans ve mevcut eşleşmiş istemci kaydı değiştirilmedi; firewall durumu **makineye özgüdür**, başka bilgisayara `.pgnbackup` yüklenince yerel yöneticinin yeniden LAN yetkisi vermesi gerekir.

## Kabul testleri

`TEST_PROGNODE_LAN_WIZARD.ps1` yerel güvenlik sözleşmesini yoklar. Ayrıca Windows'ta Public Wi-Fi, Private Ethernet, eşzamanlı Tailscale, gerçek Samsung/Android Wi-Fi erişimi, gerçek QR eşleşme, ACK, GPO yetkisiz senaryosu, uninstall cleanup ve mevcut eski kuralların korunması elle doğrulanmalıdır.

**Doğrulama durumu:** Bu ortamda .NET 10 SDK/Windows Firewall/UAC/Android fiziksel erişimi olmadığından Windows .NET derlemesi ve uçtan uca saha kabulü yapılmadı. JS sözdizimi ve statik entegrasyon kontrolleri yürütüldü. Saha devreye almadan önce Windows derleme ve telefon testi gereklidir.
