# PROGNODE RC6.4.7 HF6.2 — Trend düzeni ve kalıcı QR/LAN HTTPS

**Sürümün niteliği:** Gerçek PROGNODE Core kaynak kodunda HF6.1 üzerine uygulanan HF6.2 geliştirme ve Windows kabul test paketi. Henüz imzalı müşteri kurulum EXE'si veya üretime kabul edilmiş sürüm değildir. `README_START_HERE.md` eski sürümü işaret ediyorsa bu dosyayı esas alın.

## Ne değişti?

1. **Sol üst marka hizası:** Logonun alt çizgisi, uygulamanın üst araç çubuğunun alt çizgisiyle aynı 74px hattına getirildi. Logoda görünen domain rozeti/dış bağlantı oku yoktur; markaya tıklamak https://prognode.io/ açar.
2. **Trend Studio gelişmiş araçları:** Açılır menüde sıkışıp kırpılmak yerine `Diğer araçlar` tıklanınca araç çubuğunun altında tam genişlikte ek bir satır açılır. Grafik seçimi, zaman kısayolları, CSV, düzeni kaydet, varsayılan ve zoom sıfırlama korunur.
3. **Otomatik ve elle yerleşim:** Varsayılan `Otomatik` (ekran ve grafik sayısına göre); `Alt alta`, `2 sütun`, `3 sütun` elle seçilebilir. Sinyal gezgininden grafiği boş hücreye sürükleyip bırakabilir veya boş hücreye tıklayıp sinyal seçebilirsiniz. Grafik başlığındaki taşıma tutamacı ile dolu/boş hücrelere yeniden yerleştirebilirsiniz. Düzen kaydedilir; eski HF6.1 satır/sütun kayıtları yüklenebilir. Dar ekranda tek sütun zorlanır.
4. **Sürümden bağımsız HTTPS kimliği:** İlk kurulumda veya yerel UAC-onaylı LAN sihirbazı tamamlandığında seçilen HTTPS sertifikası `%ProgramData%\PROGNODE\config\lan-https.json` içinde yerel yönetici kontrolünde kalıcı tutulur. Sonraki ZIP'lerdeki boş appsettings.json bu kaydı sıfırlamaz. Core başlangıçta sertifika uyuşmazlığını reddeder; otomatik parmak izi değiştirmez. QR UI eski `HF2.cmd` gibi sürüme bağlı talimatlar vermez, Ayarlar → Mobile LAN → Verify/Repair akışına yönlendirir.
5. **Geliştirici veri klasörünün korunması:** Sürümden bağımsız `START_PROGNODE.cmd` ve tek seferlik `SET_PROGNODE_DATA_ROOT.ps1` eklendi. Yeni klasörde eski database bilinmiyorsa **eski Core'u durdurmadan hata verir**, asla sessiz boş veritabanı açmaz. Geliştirici hesabının seçtiği veri klasörü `%LOCALAPPDATA%\PROGNODE\dev-data-root.txt` içinde saklanır. Gerçek Windows Service ve kurulum sihirbazı yapılandırmaları yine ayrıca test edilmelidir.

## Windows 11 — Mevcut tesisi koruyarak HF6.2 testi

- **Önce yedek alın:** Eski çalışan Core'un `data` dizini (`prognode.db` dahil), lisans ve eşleşme kayıtları (`server-access.json` dahil) ayrı güvenli konumda bulunsun. Mevcut Windows HTTPS sertifikasını, private key ACL'sini ve firewall izinlerini değiştirmeyin/silmeyin.
- ZIP'i yeni bir klasöre açın. Eski Core çalışmaya devam ederken yeni kopyada yalnızca test ve kontrol yapabilirsiniz; birden fazla Core'u aynı 5080/5443 portunda **eşzamanlı çalıştırmayın**.
- Proje kökünde PowerShell ile `./TEST_HF6_2_WINDOWS_SOURCE.ps1` çalıştırın; değiştirilen PowerShell betiklerini Windows 5.1 parser ile denetler ve `.NET 10` kaynaklarını derler. Bu, gerçek telefon/PLC testi yerine geçmez.
- **Eski veriyle başlatmadan önce** proje kökünde `./SET_PROGNODE_DATA_ROOT.ps1 -Path 'C:\prognode\ESKI_CORE\data'` komutunu çalıştırın (`prognode.db` dosyanızın GERÇEK yolu ile değiştirin). Ardından `START_PROGNODE.cmd` kullanın. Proje klasörünün içindeki varsayılan `data` dizinini yaratıp eski verinin yerine geçmeyin.
- **İkinci, tamamen boş Win11 test bilgisayarında:** `./SET_PROGNODE_DATA_ROOT.ps1 -CreateNew` komutuyla yeni boş *pilot* veri klasörüne açıkça izin verin. Sonrasında `START_PROGNODE.cmd` ile derleyip açın. Bu komutu eski müşterinin bilgisayarında kullanmayın.
- Yeni kaynağın `appsettings.json` dosyası kasıtlı olarak LAN HTTPS `Enabled=false` gelebilir. Önceki kurulumun sertifikası ve parmak izi **tekil ve kesin şekilde** belirlenmiş değilse QR'ı etkin göstermeyin. `%ProgramData%\PROGNODE\config\lan-https.json` zaten kurulmuşsa aynı sertifika otomatik yüklenir. **İlk geçişte henüz bu kalıcı dosya yoksa**, eski sürümdeki onaylı `Prognode:LanHttps:Thumbprint` değerini yeni projeye *aynen* taşıyın veya `SETUP_PROGNODE_QR.cmd` yardımcısını çalıştırırken mevcut sertifikanın tekil olduğundan ve gerçek eski pin ile eşleştiğinden emin olun. Hiç sertifika bulunamadığında, betik yalnız temiz ilk kurulumda `CREATE` yazarak yeni sertifika üretir; mevcut eşleşmeli PC'de bunu **yapmayın**. PFX kullanan kurulumlar açık yönetici migrasyonu gerektirir.
- Core açıldığında `Settings → Mobile LAN → Verify / refresh` ile gerçek lokal TLS (`TLS_OK`) ve ardından telefondan erişimi test edin. QR'ın açılması için gerçek TLS ve telefonun onaylı kaynak IP aralığı gerekir; yalnız TCP dinlemek yeterli değildir. En son mevcut telefon QR ve `occurrenceId` temelli ACK; ardından Backup Restore kopya verisiyle test edilmeli.

## Kontrol listesi

- [ ] Windows `TEST_HF6_2_WINDOWS_SOURCE.ps1` + `dotnet build` başarılı
- [ ] 1366×768 / 1920×1080 ve TR/EN açık/koyu görünümde logo çizgisi/üst bant hizası
- [ ] Trend 2/8 gerçek historian tag'i: otomatik/1/2/3 kolon, sürükle-bırak, hücre swap, ayrı zaman, pause/live, BAD/STALE boşluğu
- [ ] Yeni ZIP'de eski veri, lisans ve eşleştirmeler korunmuş
- [ ] ProgramData pin eşitliği, Windows private-key ACL, yerel **pinned TLS** ve gerçek telefon 5443 testi
- [ ] Önceden eşleşmiş cihazın tekrar QR gerektirmeden açılması, yeni QR ile yeni cihaz ve occurrence ACK
- [ ] SQLite Backup Center yedekleme + izole kopyada geri yükleme ve alarm regresyonu

**Şimdiki doğrulama:** Chromium'da mock Core API ile Trend 3 grafik / boş hedef hücre, panel taşıma ve gelişmiş araç satırı testi geçti; ayrıca 620px mobil yerleşim ve Overview üst çizgi hizası test edildi. Bu Linux ortamında Windows PowerShell 5.1, .NET 10, UAC, gerçek PLC ve gerçek telefon testleri çalıştırılamadı. Bu paketi müşteri üretimine henüz dağıtmayın.

**Ek önizlemeler:** `docs/HF6_2_PREVIEW/HF62_TREND_LAYOUT_TEST.png` ve `HF62_LOGO_ALIGNMENT.png`. Trend önizlemesindeki PLC değerleri yalnız tarayıcı testi için MOCK verisidir; gerçek Core ürününde simülasyon verisi eklenmedi.
