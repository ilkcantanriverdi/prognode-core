# PROGNODE RC6.4.5 — Trend Studio Fullscreen / REAL DATA CANDIDATE

Temel sürüm: RC6.4.4. Yalnız Trend Studio arayüzü tam ekran/çoklu grafik olarak güncellendi. SQLite veritabanı ve alarm/historian sözleşmesi değiştirilmedi.

## Çalıştırma

1. Mevcut PROGNODE Core servisini durdurun (5080 portunun boş olduğuna emin olun). Mevcut veri klasörünü YEDEKLEYİN.
2. `BUILD_PROGNODE_CORE.cmd` ile kendi Windows PC'nizde build alın.
3. `START_PROGNODE_RC6_4_5.cmd` ile yeni Core'u başlatın.
4. Tarayıcıyı Ctrl+F5 ile yenileyin ve Trend Studio ekranına gidin.
5. Önce Historian sayfasından PLC sinyalini manuel olarak kayıt için etkinleştirin. Trend Studio içinde `Historian’dan ekle` ile bir tıklamada her sinyal için ayrı grafik açılır.
6. LIVE akışında durdurana kadar son 5dk/15dk/1sa vb. kayar; karşılaştır düğmesi tıklanınca checkbox'lar görünür. Seçili grafiğe tıklayınca büyür.

Yeni dosyalar: `trend-studio-fullscreen.html`, `trend-studio-fullscreen.js`, `trend-studio-mount.js`. Eski Trend DOM, daha eski app.js işleyicilerinin hatasız başlatılması için gizli tutulmuştur. Üretim dağıtımı öncesi temiz mimariye taşınmalıdır.

## Doğrulama kapsamı ve dürüst sınırlamalar

JS syntax ve test verisiyle tarayıcı etkileşimleri kontrol edildi; ZIP CRC kontrol edildi. Bu ortamda Windows .NET SDK ve gerçek PLC mevcut değil. Gerçek derleme, 5m/15m penceresi, 1-8 grafikte gerçek akış, BAD/STALE kırılması ve uzun süreli performans Windows/Developer Test Bench üzerinde doğrulanmalıdır. Bu paket *field-test candidate* olup ticari genel sürüm değildir.

Bu paket Core'un mevcut cloud lisans aktivasyon host/SSL yapılandırmasını DEĞİŞTİRMEZ; Control Center Installations=0 sorununun ayrıca çözülmesi gerekir.
