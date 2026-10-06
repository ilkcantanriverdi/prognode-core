# PROGNODE RC6.4.7 HF6 — müşteri arayüzü / Windows 11 kabul adayı

**Tür:** .NET 10 TAM KAYNAK PAKETİ; **EXE / imzalı ticari kurulum değildir**. Windows derlemesi ve gerçek saha kabulü bekleniyor.

## İçerik ve sınır

- Onaylanan yeni PROGNODE arayüzü **gerçek Core web kabuğuna** bağlandı: sol menü, kompakt durum/oturum alanı, temiz Overview, Settings kartları ve gerçek HF3+ Historian Trend Studio.
- Logo `https://prognode.io/` adresini yeni sekmede açar; yönlendirme ok simgesi kaldırıldı.
- Overview cihaz/tag/alarm/historian ve lisans durumunu **gerçek Core state** üzerinden görüntüler; örnek tesis verisi üretim ekranına gömülmedi. Lisans geçersizse hızlı başlangıçta tamamlanmış gösterilmez.
- Settings: Sunucu & QR, Mobil LAN, Backup, Remote kartlarından **orijinal çalışan ayarlar** açılır. Tanılama ayrı görünür.
- Trend: grafikler arka planda gereksiz sorgu üretmez, tab yeniden görünür olduğunda yenilenir; HF3+ kalite boşlukları ve bağımsız zoom korunur. Gerçek PLC akışıyla 30dk+/8 panel uzun kullanım ayrıca test edilmeli.
- QR, Windows UAC LAN yardımcısı, sertifika pini, v2 ACK, lisans/backup ve API sözleşmeleri korunmuştur. **Sertifikayı yenilemeyin, eski eşleşmeleri silmeyin.**

## Windows 11'de güvenli test

1. Çalışan eski klasörü ve `Prognode__DataRoot` değerini kaydedin. Backup Center ile doğrulanmış, şifreli bir yedek alın. Mümkünse **ayrı test PC ve veri kökü** kullanın. Gerçek üretim verisinin üzerine geri yükleme yapmayın.
2. Yeni ZIP'i farklı bir klasöre açın. Eski tray Agent'ı **Exit PROGNODE Agent** ile kapatın. Var olan Core port 5080 çakışması yaratıyorsa yalnız PROGNODE sürecini kontrollü sonlandırın.
3. Test kaynağı için `Prognode__DataRoot` ortam değişkenini veri klasörüne ayarlayın. Mevcut veri klasörünü veya `server-access.json` dosyasını silmeyin; HTTPS sertifikasını değiştirmeyin.
4. `TEST_HF6_WINDOWS_SOURCE.ps1` çalıştırın. Bu betik syntax/build kontrolü yapar, eski servisi değiştirmez.
5. `START_PROGNODE_RC6_4_7_HF6.cmd` çalıştırın. Başlatıcı bir güvenlik yedeği gerektiriyorsa şifreyi yerel ortamda isteyecektir; geçemezse eski Core'u durdurmadan önce hata verir.
6. Tarayıcıda `http://127.0.0.1:5080` adresini açın, Ctrl+F5 yapın. Önce boş, ardından (ayrı test verisiyle) dolu Overview; Settings'in 4 kartı; TR/EN; açık/koyu tema; 1366 ve 1920 px; Trend 1/2/4/8 panel ve kalite boşluklarını deneyin.
7. Var olan Samsung'u yeniden eşleştirmeden QR bağlantısı, mobil alarm ve **occurrence-specific ACK** kontrolü yapın. LAN HTTPS için `TLS_OK` ve gerçek telefondan erişimi doğrulayın. Backup Restore'u üretim verisine değil kopya klasöre uygulayın.

## Kabul kapıları

- Burada JS syntax, mevcut Node testleri ve simüle API ile tarayıcı arayüz smoke testi yapılır. **Bu testler gerçek Windows .NET build, UAC, PLC/Historian yükü veya mobil uçtan uca testinin yerine geçmez.**
- Yeni paket production-ready ilan edilmeden Windows 11 .NET 10 derlemesi, temiz kurulum, geçmiş DB migration, en az 30dk gerçek Trend yükü, QR/ACK ve Backup geri yükleme geçmelidir. Sonuçları `HF6_WINDOWS_ACCEPTANCE_REPORT.md` şablonuna kaydedin.
