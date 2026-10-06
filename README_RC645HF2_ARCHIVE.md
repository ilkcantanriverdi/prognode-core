# PROGNODE RC6.4.5 HF2 — Core-native Trend Studio

**Kaynak ZIP / Windows geliştirici testi.** Bu paket mevcut RC6.4.5 HF1'in tamamını içerir; sadece görünüm dosyası değildir. Kurulum EXE'si değildir. Bir önceki `data` klasörünü otomatik silmez veya taşımaz.

## Değişiklikler

- Orijinal Core `index.html` sol menüsü, gerçek PROGNODE B1 logosu ve ortak başlığı korunur. Trend Studio artık sayfanın içine gömülüdür; ikinci bir sahte sidebar/logo/header yoktur.
- Orijinal sol menüde **« / »** tüm Core sayfalarında genişlet/daralt; tercih tarayıcıda saklanır.
- TR/EN ve Dark/Light kontrolleri orijinal Core'un üst panelindedir; Trend Studio bunları anında takip eder.
- Historian'da manuel etkinleştirilmiş Tag'ler grafiğe alınır. Her grafikte gerçek `sampleIntervalSeconds` görünür. Kısa aralıkta gerçek örneklerin noktaları işaretlidir; uzun aralığın özetlenmiş verileri açıkça belirtilir. Ham kayıtların tümü için Historian CSV kullanılır.
- Grafik üzerine farenin sol tuşuyla **sürükleyerek zaman aralığına zoom**; bu sırada LIVE durur ve Core'dan seçilen zaman aralığı yeniden sorgulanır. ↻ ile LIVE'a dön. Fare tekerleği zoom yapmaz.
- Analog smooth çizgiler görseldir; BAD/STALE ve kayıt kesintileri üzerinden çizgi çekilmez. Alarm eşiklerini içeren otomatik Y ölçeklendirme korunur.
- Tags sayfası dışında kullanıcıya teknik adres ve veri tipi gösterimi kaldırıldı: Alarm/Historian Tag seçimi, Historian tablosu ve Trend başlıkları sade `PLC1 · Sıcaklık` biçimindedir.
- Açık mod kontrastı, çizgiler, seçim kontrolleri ve bilgi etiketleri güçlendirildi. Tek grafik ve çoklu grafik çalışma alanı korunur.

## Windows'ta gerçek Core testi

1. Eski kurulumun `data` klasörünü yedekle. Çalışan eski Core'u durdur; üretim servisinde yetki gerekebilir.
2. ZIP'i **yeni ayrı bir klasöre** aç. Önceki veriyle test edeceksen eski Core tamamen kapalıyken eski `data` klasörünü yeni proje köküne kopyala. Varsayılan dışı veri klasörünü `Prognode__DataRoot` ile belirt.
3. Yeni klasörde **yalnız `START_PROGNODE_RC6_4_5.cmd`** çalıştır. Script önce 5080 portunu temizler, sonra `dotnet build` çalıştırır, yeni Core'u başlatır ve sürümü denetler.
4. `CHECK_PROGNODE_RC6_4_5.ps1` ile `0.7.2-rc6.4.5-hf2-core-native` doğrula. Tarayıcı Ctrl+F5 ile yenilenmeli.
5. Historian'da bir Tag'i 10/30/60 sn örnekleme ile manuel etkinleştir; grafiğe ekle. Noktaları ve kayıt aralığını kontrol et. Fareni basılı sürükleyerek zoom yap, orijinal örnek zamanlarını incele. Birden fazla grafiği ve karşılaştırmayı dene. Orijinal Core menüsünü « » ile daralt/genişlet, TR/EN ve açık/koyu modu dene.

## Doğrulama / sınırlama

`node --check` ve Chromium ile **mock gerçek API yanıtlarını kullanan** ön yüz testleri yapılmıştır: iki dil, iki tema, drag zoom, kayıt noktaları, LIVE dönüşü, orijinal menü/logo ve aynı Core içi mount. Bunlar gerçek saha doğrulaması değildir. Bu çalışma ortamında `.NET 10 SDK` olmadığı için `dotnet build` burada çalıştırılamadı; ayrıca gerçek PLC/Historian soak testi henüz yapılmadı. Windows build ve senin gerçek PLC verin RC6.4.5 HF2'nin sahada kabul aşamasıdır.

**Web/Control Center lisans aktivasyon host/SSL sorunu bu görsel paketin kapsamında değiştirilmedi.**
