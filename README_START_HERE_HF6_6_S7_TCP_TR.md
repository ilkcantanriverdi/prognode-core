# PROGNODE RC6.4.7 HF6.6 — Siemens S7 TCP saha testi

**Durum:** Tam kaynak / Windows 11 saha-test adayı. Bu ortamda .NET 10 SDK veya gerçek PLC bulunmadığı için Windows derlemesi ve PLC okuma henüz doğrulanmadı. Mevcut HF6.5 Core verisini, ProgramData HTTPS sertifikasını, eşleşmiş telefonları ve lisans dosyasını SİLMEYİN. Ayrı klasöre açın.

## Sürücü kapsamı
- ISO-on-TCP (RFC1006, TCP 102), COTP, S7comm Setup Communication, **yalnızca Read Var**. Write Var, PLC kontrolü, TSEND/TRCV ve PROFINET IO mevcut değildir.
- S7-1200 / S7-1500 için *optimize edilmemiş* global DB ve PLC'de PUT/GET erişimi gerekir. Sadece güvendiğiniz test PLC'sinde bu erişimi açın. Kurumsal PLC güvenlik ayarını BT/otomasyon onayı olmadan değiştirmeyin.
- S7-1200/1500 için başlangıç Rack=0 Slot=1; farklı CPU'da gerçek slotu girin. TCP port 102.
- BOOL `DB1.DBX0.0`; WORD / INT / UINT16 `DB1.DBW2`; REAL / DINT / DWORD `DB1.DBD4`. Byte dizilişi Siemens big-endian.
- Devices > Add Device > Siemens S7 TCP > IP/Rack/Slot > **Test Siemens S7**. Başarılı handshake **DB okuma izni kanıtı değildir**: test DB adresiyle gerçek Tag oluşturup GOOD değerini kontrol edin.
- Tags ekranından ilgili cihaza tag ekleyin. S7 adresi ve veri tipi eşleşmelidir. Alarm ve Historian mevcut sistem üzerinden bağlanır.

## İlk saha testi
1. Windows PowerShell'de bu klasörde `./TEST_HF66_WINDOWS_SOURCE.ps1`; .NET 10 SDK gerektirir. Sonuç başarısızsa mevcut çalışan HF6.5'i değiştirmeyin.
2. Test PLC'de DB1 oluşturun: `DBX0.0` değişken BOOL; `DBW2` INT; `DBD4` REAL. DB ayarında **Optimized block access** kapalı. TIA'da PUT/GET açık; PLC port 102'ye yalnızca izinli PROGNODE PC ulaşabilsin.
3. `START_PROGNODE.cmd` ile aynı **orijinal `Prognode__DataRoot`** kullanarak geliştirme Core'unu çalıştırın. Aynı anda eski Core servisini başlatmayın; port/SQLite çatışması oluşur. Kurulu servis geçişi ayrıca test edilmelidir.
4. İki PLC testi: PLC-1 Modbus TCP 100 alarm +20 analog, PLC-2 S7 TCP 100 alarm +20 analog. 30 sn historian. PLC bağlantı kopması/tag kalite BAD ve yeniden bağlanma, alarmlar, trend veri sürekliliği, servis yeniden başlatma ve QR/regresyon testlerini yapın.
5. En az 1 saat, daha sonra 8 saat ve 48 saatlik soak testler. İlk testte 120 S7 Tag için CPU/latency ve poll süresini ölçün: mevcut sürücü tag başına ReadVar gönderir; batch read performans geliştirmesi ayrı kabul maddesidir.

## Veri koruma
Mevcut `C:\ProgramData\PROGNODE` klasörünü ve Core DataRoot'u koruyun. Çalışan HTTPS sertifikasını hiçbir koşulda yeniden üretmeyin. Eski Core ve yeni Core aynı SQLite veritabanına **eşzamanlı** bağlanmasın. Yeni sürüme geçmeden şifreli `.pgnbackup` dışa aktarın.

**Saha kabulü:** Gerçek PLC'de COTP+S7comm, gerçek DB değerleri, kesinti dönüşü, 30 sn historian, iki cihazlı 240-tag test geçmeden `production ready` demeyin.
