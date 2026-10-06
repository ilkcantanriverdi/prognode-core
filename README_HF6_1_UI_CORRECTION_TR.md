# PROGNODE RC6.4.7 HF6.1 — Gerçek Core UI Düzeltmesi

Bu paket, HF6 kaynak projesi üzerine uygulanmış **tam kaynak ZIP**'tir; bağımsız görsel prototip değildir. Test edilmemiş yazılıma "müşteriye teslim" etiketi verilmemiştir.

## Değişen kısımlar

- Sol üst marka alanı artık yalnız **orijinal PROGNODE logosu + PROGNODE + Industrial Monitoring** içerir. Parlama/gölge efekti, görünür `prognode.io` rozeti ve dış bağlantı oku kaldırıldı. Logo alanına tıklamak **https://prognode.io/** adresini yeni sekmede açar.
- Eski HF6 grafik demonstrasyonu gerçek Core'a gömülmedi. Bunun yerine **mevcut gerçek `/trend-hf3plus.html` ve `/trend-hf3plus.js` Historian tabanlı ekranı** yeniden düzenlendi. Sol sinyal gezgini, arama ve seçim, varsayılan alt alta geniş grafikler, iki sütun seçeneği, grafik başına 15m/1h/24h/7d ve Core geçmişinden Proses Olayları tablosu eklendi.
- Mevcut API'ler, gerçek zaman damgaları, veri kalitesi boşlukları, per-panel zoom, LIVE/PAUSE, karşılaştırma, imleç, uzun dönem historian sorgusu ve kaydedilmiş düzen davranışları kullanılmaya devam eder. Gerçekte bulunmayan PLC örnekleri uydurulmaz; Historian yapılandırılmadıysa boş durum gösterilir.
- Bu UI değişiklikleri dışında lisans, HTTPS sertifikası, QR, LAN UAC helper, alarm v2 ACK, sqlite, Remote Access ve Backup Center uygulama kodu HF6 ile **birebir aynı** tutuldu.

## Doğrulama / sınırlar

- Chromium tarayıcıyla HF6.1 Overview / Settings ekranları, 390px mobil yatay taşma, 1366px ve 1920px masaüstü test edildi.
- Trend Studio mock Core API kullanılarak 2 ve 8 etkin Historian tag'iyle test edildi; JavaScript konsolunda çalışma hatası görülmedi. Grafik zaman penceresi, alt alta / iki sütun, sinyal arama ve olay bölümü etkileşimleri kontrol edildi. **Bu testte örnek API verileri sadece tarayıcı test ortamına enjekte edildi; kurulan ürün içinde simülasyon verisi yoktur.**
- Bu Linux ortamında Windows .NET 10 derlemesi, gerçek Siemens PLC/SQLite Historian performansı, mobil QR/ACK ve yedek geri yükleme **test edilmedi**. Müşteriye teslimden önce Windows kabulü zorunludur.

## Nasıl denemeli?

1. Mevcut çalışan PROGNODE Core'u ve `data/`, `server-access.json` ile mevcut HTTPS sertifikasını yedekleyin. Sertifikayı yeniden oluşturmayın ve üretimdeki veritabanını silmeyin.
2. ZIP'i **yeni bir klasöre** açın. Bir süredir kullandığınız Core'u çalışırken üstüne yazmayın.
3. Mevcut kullanıcı/veri ortamını kullanacaksanız eski `Prognode__DataRoot` yapılandırmasını koruyun. Tercihen ikinci Win11'de **kopya test verisiyle** başlayın.
4. Windows PowerShell'de proje kökündeyken `./TEST_HF6_WINDOWS_SOURCE.ps1` ve `dotnet build ./src/Prognode.Host/Prognode.Host.csproj -c Debug` komutlarını çalıştırın. Mevcut `START_PROGNODE_RC6_4_7_HF6.cmd` HF6 tabanındaki geliştirme başlatıcısıdır.
5. Tarayıcıda eski önbelleğin yüklenmesini önlemek için Ctrl+F5 yapın. Ana sayfa, Ayarlar ve 2/8 gerçek Historian tag'iyle Trend Studio'yu gözden geçirin.
6. İkinci Windows 11 bilgisayarı, telefon ve QR/ACK/Backup Center testleri tamamlanmadan üretim sürümü olarak dağıtmayın.

## Değişen dosyalar

- `src/Prognode.Host/wwwroot/index.html`
- `src/Prognode.Host/wwwroot/trend-hf3plus.html`
- `src/Prognode.Host/wwwroot/trend-hf3plus.js`
- **Yeni:** `src/Prognode.Host/wwwroot/customer-hf61.css`
- **Yeni:** `src/Prognode.Host/wwwroot/trend-hf61.css`

Not: Alttaki eski HF6 README ve önceki sürüm dosya adları tarihsel kaynakları anlatır; HF6.1 değişiklikleri için bu belge geçerlidir.
