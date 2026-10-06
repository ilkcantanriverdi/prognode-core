# PROGNODE RC6.4.7 HF1 — QR + onaylanan HF3+ Trend Studio (tam Core)

Bu, **RC6.4.7 QR Pairing kaynaklarının tamamı üzerine onaylanan HF3+ Trend Studio'nun gerçek Core ekranına bağlandığı** birleşik test paketidir. Eski yalnızca-QR ZIP'inin yerine bu paketi kullanın.

## Değişmeyenler

- Orijinal PROGNODE logo, native sol menü, Core tema ve TR/EN seçimi.
- Ayarlar içindeki 120 saniyelik tek kullanımlık HTTPS QR eşleştirmesi, manuel pairing fallback, mevcut eşleştirmeler.
- Mevcut Alarm/Historian/Batch/License/Remote API ve veritabanı korunmuştur. Yıllık 5/10/25 Remote modeli ve ücretsiz LAN değişmedi.

## HF3+ gerçekten nerede çalışıyor?

Core'un **Trends** menüsündeki orijinal `#fullscreenTrendFrame` şimdi
`/trend-hf3plus.html` yüklüyor; `trend-hf3plus.js` ve `trend-hf3plus.css` gerçek aktif frontend varlıklarıdır.
Onaylanan bağımsız HTML tasarım örneği `docs/HF3_PLUS_APPROVED_REFERENCE_DEMO.html` içinde yalnızca referans olarak bulunur ve müşteri verisi yerine simülasyon kullanır. Canlı Core UI referans demoyu **çalıştırmaz**.

- Sadece Historian'da elle etkinleştirilen Tag'ler açılabilir; ön yüklü/sahte Trend veya otomatik Historian kaydı üretilmez.
- Chart başına 5m/15m/1h/8h/24h/7d/30d/1y, LIVE/PAUSE, bağımsız zoom. 1–8 grafik, sürükleyip sıralama, büyütülmüş grafikte sürükleyerek zoom ve sağ tıkla geri al.
- İsteğe bağlı ortak imleç gerçek kayıt noktalarına snap olur; BAD/STALE veri araları birleştirilmez. Her Chart için ayrı alarm bayrağı/eşik/nokta/Batch ayarları, karşılaştırma ve düzeni kaydetme.
- Gerçek okuma: `/api/tags`, `/api/devices`, `/api/historian/configurations`, `/api/trend-studio/series`, `/api/alarms/definitions`, `/api/alarms/history` ve `/api/batches`. 3000 nokta üstünde server-side özet verilir; yakınlaştırınca tekrar gerçek kayıt sorgulanır. Özet zaman aralığından eksiksiz ham CSV indirme iddiası yoktur.
- Alttaki Core sürümü ve `/api/health` aynı: `0.7.2-rc6.4.7-hf1-qr-hf3plus`.

## Windows'ta kurulumu (test)

1. **Çalışan Core'u durdur ve `data` klasörünü yedekle.** Önceki lisansı silme. ZIP'i ayrı yeni klasöre aç.
2. .NET 10 SDK kurulu Windows PC'de `START_PROGNODE_RC6_4_7_HF1.cmd` çalıştır. Eski Windows Service'i yalnız gerektiğinde ve yönetici izinleriyle durdurur. Farklı yazılım 5080'i kullanıyorsa durur; zorla kapatmaz.
3. Script solution'ı derler, `/api/health` yeni versiyonu, HF3+ HTML ve orijinal QR ekranını **üç ayrı kontrolle** doğrular. Sonra tarayıcı açılır. `Ctrl+F5` ile eski tarayıcı cache'ini temizle.
4. `Trends` ekranı içinde gerçek Historian verisi görmek için önce `Historian` sayfasında Tag'in kaydını elle etkinleştir; sonra `Historian'dan ekle` seç. QR testi için `Settings → QR ile Cihaz Ekle`.
5. Bağımsız ilave kontrol: `TEST_PROGNODE_RC647_HF1_MERGE.ps1`.

**Doğrulama sınırı:** JS syntax ve mock Core API ile otomatik tarayıcı etkileşim testleri yapıldı; orijinal QR Core kodu checksum ile korundu. Bu çalışma ortamında .NET SDK / Windows PLC olmadığı için gerçek `dotnet build` ve gerçek PLC testi yapılmadı. Windows build geçmeden production'a taşımayın.
