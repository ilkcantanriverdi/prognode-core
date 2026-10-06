# PROGNODE Trend Studio — HF3+ mühendislik önizlemesi

**Durum:** Bağımsız, çevrimdışı çalışan HTML arayüz prototipi. Mevcut Core kaynak koduna dokunulmadı; henüz Core ZIP'i, gerçek Historian API bağlantısı veya üretim sürümü değildir. Gerçek PROGNODE logo görseli HTML içine gömülüdür. Test verileri açıkça temsili/simülasyon verisidir.

## Yeni özellikler ve deneme akışları

1. **Her grafiğe ayrı zaman:** Her panelin başlığındaki `15m` seçicisini değiştir. Diğer panellerin aralığı değişmemeli. Üstteki hızlı zaman düğmeleri yalnızca başlığı tıklanarak aktif edilen paneli değiştirir. Global LIVE düğmesi ana takip akışını, paneldeki `PAUSE/LIVE` yalnızca o paneli kontrol eder.
2. **Ortak imleç, isteğe bağlı:** `Ortak imleç` kutusunu işaretle. Bir grafiğin üstünde fareyi oynatınca diğer panellerde aynı zamana en yakın **gerçek kaydedilmiş** örnek noktası işaretlenir. BAD/STALE aralığında uydurma değer gösterilmez. Grafik zoom ve zaman aralıkları birbirinden bağımsız kalır.
3. **Veri kalitesi:** Sıcaklık grafiğinin son 15 dakikasında `BAD`, basınç grafiğinde `STALE` örnek aralıkları çizgiyi koparır. Kesinti taranmış alanda görünür; her panelin altında ilgili kalite sayacı bulunur.
4. **Panel sırası, boyut, kayıt:** Panel başlığındaki `⠿` tutamacını diğer grafiğe sürükle. Soldaki sütunun genişliğini kaydırıcıyla %30–70 değiştir. Grafiğin `⚙` menüsünde **İki sütun genişliği** ayarını aç. `Düzeni kaydet` tarayıcıda yalnızca panel sırasını, zaman pencerelerini, çizim ayarlarını, bağlantılı imleci ve sütun genişliğini saklar. `Varsayılan` ile eski dört panele dön.
5. **Uzun dönem görselleştirme:** `7d`, `30d`, `1y` aralıklarını dene. Geniş görünümde zaman kovası başına ilk/son/min/max gerçek örnekleri koruyarak çizim için nokta azaltılır. Soldan dar aralık seçip zoom yaptığında orijinal simülasyon örnekleri ayrıntısıyla kullanılır. İlgili panel altında ham nokta → çizilen nokta sayısı görünür.

### Önemli üretim sınırı

Önizlemenin 1 yıllık *sentetik* arşivi, performans gösterebilmek için karışık örnekleme kullanır: en yeni 8 gün 30 saniye; önceki 22 gün 2 dakika; daha eski yaklaşık 11 ay 1 saat. Bunlar gerçek Core kayıtları veya bir üretim veri saklama politikası değildir. Gerçek Core entegrasyonunda tüm saklanan örneklerin kalite/zaman damgaları korunarak sunucuda zaman-kovası min/max, alarm-sınırı olayı koruma, cursor ve zoom'da gerçek veriyi tekrar sorgulama uygulanacaktır.

### Korunan mevcut kullanım davranışları

- PROGNODE Core'un orijinal logosu, daraltılabilir sol menü, TR/EN ve açık/koyu tema.
- Sol tuş + sürükle: aktif grafiğe bağımsız zoom; büyük grafik büyük kalır.
- Sağ tık: aynı grafiğin önceki zoom'una dön.
- Historian'dan tekli/çoklu Tag ekleme ve sürükle-bırak.
- Karşılaştırma modu kapalıyken görünmeyen seçim kutuları; açılınca ayrı grafikler korunarak karşılaştırma.
- Her panelde bayrak, eşik, örnek noktaları, Batch/Lot, smooth ve dondurma seçenekleri.

**Sonraki adım:** Önizleme geri bildirimi → Core/Historian API entegrasyonu → Windows .NET derlemesi → gerçek PLC ile kabul testleri → tam kaynak ZIP.
