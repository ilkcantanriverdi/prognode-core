# PROGNODE RC6.4.7 HF2 — QR + HF3+ LIVE Trend Studio

**Bu ZIP test amaçlı TAM Core kaynak paketidir; önizleme HTML'si değildir.**

1. Mevcut çalışan Core'u durdurun. Özellikle `data/`, `server-access.json` ve lisans dosyasını yedekleyin. Var olan klasörü silmeyin. Yeni ZIP'i **ayrı klasöre** çıkarın.
**Kritik yükseltme notu:** Yeni klasördeki `data/` boşsa yeni sunucu kimliği ve eksik lisansla açılabilir. Mevcut `data/` klasörünüzü yedekten kopyalayın **veya** eski gerçek veri klasörünüzü aynı PowerShell oturumunda `$env:Prognode__DataRoot = 'C:\ESKI_PROGNODE\data'` ile gösterin. Bu yolda bulunan `server-access.json`, `.db` ve `.pgnlicense` dosyalarını silmeyin. Mevcut HTTPS sertifikasının Thumbprint değerini de koruyun.

2. İlk kez QR kullanıyorsanız Core PC'de `SETUP_PROGNODE_QR.cmd` dosyasını çift tıklayın ve istenen yönetici iznini onaylayın. **Mevcut geçerli HTTPS sertifikasını değiştirmeyin.** Betik var olan sertifikayı tekrar kullanır.
3. Windows'ta .NET 10 SDK yüklüyken `START_PROGNODE_RC6_4_7_HF2.cmd` ile yeni Core'u derleyip başlatın. Betik `/api/health` sürümünü, Trend Studio bağlantısını ve QR arayüzünü kontrol eder.
4. `http://127.0.0.1:5080` üzerinden giriş yapın. Tarayıcı eski görünüyorsa `Ctrl+F5`. Sağ üstteki sürüm `0.7.2-rc6.4.7-hf2-qr-hf3plus` olmalı.
5. **Trends:** önce Historian'da gerçek Tag kaydını elle etkinleştirin; sonra Trends → Historian'dan ekle. Varsayılan sahte grafik yoktur.
6. **QR:** Ayarlar → İstemci Erişimi → QR ile Cihaz Ekle. Telefonun görebildiği Ethernet/Wi-Fi arayüzünü seçin. QR yalnızca **çalışan HTTPS** ve yetkili yerel kullanıcıyla üretilir; 120 saniye ve tek kullanımlıktır. Yenile/İptal var olan bileti geçersiz kılar.
7. İleri doğrulama: `TEST_PROGNODE_RC647_HF2_MERGE.ps1` ve `TEST_PROGNODE_QR_V05.ps1`. Gerçek Android/iOS eşleştirme için mobil V0.5 QR tarayıcısı gerekir; eski APK manuel 6 haneli kod ile devam eder.

**QR neden boştu?** Gönderdiğiniz önceki kaynak pakette `src/Prognode.Host/appsettings.json` içindeki `LanHttps.Enabled` varsayılan olarak false. QR, sertifikalı HTTPS kurulmadan güvenli şekilde çizilemez. Bu pakette önkoşul ekranı, tek seferlik UAC kurulumu ve yeniden kontrol düğmesi var. Başlatıcı HTTPS hazır değilse uyarı verir. Bu bir "örnek QR" değildir; sertifika olmadan bilet üretilmez.

**HF3+** gerçek iframe `/trend-hf3plus.html`, gerçek `/api/historian/configurations` ve `/api/trend-studio/series` ile çalışır. İsteğe bağlı ortak imleç, grafik başına zaman ve LIVE/PAUSE, 1–8 panel, bağımsız zoom, düzen kaydı, görsel kalite boşlukları korunur. HF2'de uzun dönem SQLite sorgusunda önceki AVG-only özet yerine **gerçek kaydedilmiş min/max/ilk/son noktalar + kalite işaretleri** korunur. Uzun dönem görsel özet eksiksiz ham CSV değildir; yakınlaşınca gerçek örnekler tekrar sorgulanır.

**Doğrulama sınırı:** Bu ortamda JS sözdizimi, mock Historian ile Chromium etkileşimleri, QR renderer/HTTPS UI akışı, SQLite özet SQL testi ve mevcut Node testleri geçti. .NET 10 ve Windows yönetici/servis/gerçek PLC testleri burada çalıştırılamadı; müşteri/üretim ortamına taşımadan önce Windows derlemesi ve sahada test gereklidir.
