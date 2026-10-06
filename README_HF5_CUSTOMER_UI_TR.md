# PROGNODE RC6.4.7 HF5 — müşteri arayüzü hazırlığı

**Kaynak:** HF4.1.2.1 (QR / LAN TLS / Backup Center / HF3+). Bu paket bir **tam kaynak kodu** paketidir; Windows'ta derlenmiş veya müşteri için imzalı installer değildir.

## Arayüz değişiklikleri

- Üst sabit başlık kaldırıldı. Sayfa başlığı, yerel durum, saat, dil, tema ve oturum kontrolleri sol menüye taşındı. Sol menü daraltılabilir; küçük ekranlarda mobil menü korunur.
- Sol üst PROGNODE logosu yeni sekmede pazarlama web sitesi için `https://prognode.tech/` adresine gider. Bu alan adı henüz yayında değilse `src/Prognode.Host/wwwroot/index.html` içindeki `brandWebsite` bağlantısını gerçek yayındaki adresle değiştirin; yerel Core gezinmesi değişmez.
- Ayarlar dört sekme: Sunucu & QR, Mobil LAN, Uzak erişim ve Yedek & kayıtlar. Mevcut form/kaydet/onay API'leri silinmedi. Backup verisi yalnız Backup sekmesine girildiğinde otomatik yüklenir.
- HF3+ Trend Studio: gerçek Historian/API sözleşmesi korunur; aynı karede SVG çizimleri birleştirilir, tarayıcı/Trend sekmesi görünmezken sorgu durur, kısa aralıklar 10 saniye, gün/ay aralıkları 30 saniye, haftalık/yıllık aralıklar 60 saniye periyotta sorgulanır. Çizilecek nokta sayısı aralığa göre sınırlandırılır; tam ham veri gerektiğinde Historian export kullanılmalıdır. BAD/STALE kalitesi ve bağımsız zoom korunur. Kaydedilen grafiğin en son değeri ve NORMAL/YÜKSEK/DÜŞÜK durumu yeni seri geldikçe başlıkta güncellenir.
- API kimlik doğrulama, HTTPS/QR sertifika pini, yedek verisi, lisans ve mobil istemci anlaşmaları değiştirilmedi.

## Windows 11 geliştirme testi

1. Kurulu PROGNODE veri klasörünü ve lisansını **yedekleyin**. HTTPS sertifikasını silmeyin, eski mobil eşleştirmeleri sıfırlamayın. Yeni kaynak klasörü, mevcut `Prognode__DataRoot` ile başlatın; yanlışlıkla boş veri kökü oluşturmayın.
2. Eski Core ve Agent işlemlerini kontrollü kapatın. ZIP'i **yeni** bir klasöre açın. `START_PROGNODE_RC6_4_7_HF4_1_2.cmd` dosyasıyla bu kaynak sürümünü başlatın. Gerçek `data/` klasörü hiçbir ZIP'e dahil edilmemiştir.
3. Tarayıcıda `Ctrl+F5`; Genel Bakış/Cihazlar/Taglar/Alarmlar/Historian/Bildirimler/Lisans sayfaları arasında gezinerek geçiş, sol menü, dil ve açık/koyu temayı kontrol edin.
4. Ayarlar'da dört sekmeyi açın. Mevcut başarılı **LAN HTTPS/QR** ayarını yeniden kurmayın; sadece durumunu doğrulayın. Yedek geri yükleme testini üretim verisi yerine kopya veri kökünde yapın.
5. Historian etkin Tag'lerle Trend Studio açın. 1, 2, 4, 8 grafik; 15m/1h/24h/7d/30d/1y; per-panel LIVE/PAUSE; zoom; veri kalite boşlukları; karşılaştırma ve düzen kaydetmeyi deneyin. En az 30 dakika açık tutun; tarayıcı görev yöneticisinden bellek/CPU'yu izleyin. 1 yıllık sorgu gerçek veri bulunmadığında **simüle edilmez**.
6. Android QR bağlantısının ve Windows 11 Client pilotunun hâlâ çalıştığını ayrı kabul testiyle doğrulayın.

**Güvenlik/ürün sınırı:** Bu kaynak paketini Windows .NET 10 gerçek derleme, 30 dakikalık gerçek Historian yükü, sahadaki QR/ACK regresyonu ve temiz Windows 11 kurulum testi tamamlanmadan imzalı müşteri kurulum programı diye dağıtmayın. Bu ortamda Windows servisi, UAC veya gerçek PLC ile uçtan uca test yapılmamıştır.
