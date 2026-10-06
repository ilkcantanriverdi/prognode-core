# PROGNODE RC6.4.7 — QR ile Güvenli LAN Eşleştirme

**Temel:** RC6.4.6 Mobile Foundation. Önceden eşlenmiş telefonlar ve 6 haneli manuel eşleştirme korunur. Lisans modeli **yıllık 5/10/25 remote cihaz**; LAN eşleştirmesi remote koltuğu tüketmez. Cloud relay/FCM/APNs için ek bir koşul yoktur.

## Kullanım

1. Mevcut PROGNODE veri klasörünüzü yedekleyin; yeni paket ayrı proje klasörüne açılabilir. **Mevcut `data` veya `server-access.json` dosyasını silmeyin.** Ürün anahtarı/şifre hiçbir handoff'a eklenmemiştir.
2. İlk defa LAN HTTPS kullanıyorsanız **Core bilgisayarında yönetici PowerShell** ile `ENABLE_PROGNODE_LAN_HTTPS.ps1` çalıştırın. Bu işlem LocalMachine sertifika deposunda özel anahtarı dışa aktarılamayan HTTPS sertifikası oluşturur; `appsettings.json` içinde `Prognode:LanHttps` ayarlarını değiştirir ve Private profilde TCP 5443'ü açar. Önceden etkin HTTPS'i varsa bu işlemi tekrarlamayın; sertifika değişimi eski eşleşmelerde yeniden doğrulama gerektirir.
3. `START_PROGNODE_RC6_4_7.cmd` çalıştırın; Windows'ta `dotnet build` başlar, eski geliştirme Core'u durdurur, doğru `/api/health` sürümü ve arayüzü doğrular. Servis kurulumunu otomatik değiştirmez. Core'u **kendi bilgisayarınızda** `http://127.0.0.1:5080` üzerinden açın, geçerli Alarm lisansınıza OWNER/ORGANIZATION_ADMIN olarak giriş yapın.
4. **Ayarlar → İstemci Erişimi → QR ile Cihaz Ekle.** Birden fazla ağ kartı varsa telefonun eriştiği Ethernet/Wi-Fi adresini seçin. QR **120 saniye** ve **bir defalık** geçerli; Yenile eskisini iptal eder; İptal derhal geçersiz kılar. Kod fiziksel Core ekranı dışında paylaşılmamalı.
5. Mobil takımın V0.5 Flutter QR okuyucusu hazır olduğunda uygulama QR'daki `certSha256` değerini **ticket ve kimlik bilgisi göndermeden önce** çalışan Core'un HTTPS sertifikasının DER SHA-256 değeriyle eşleştirir. Ardından yalnız `https://<QR lanHost>:5443/api/client/pair-qr` üzerinden biletle eşleşir. **Eski APK'da QR okuyucu olmayabilir; onun için manuel 6 haneli kod + önceden doğrulanmış fingerprint yolu aynen çalışır.**
6. Core'da `TEST_PROGNODE_QR_V05.ps1` çalıştırın. İlk negatif güvenlik testleri kimlik bilgisi istemez; `-RunAuthorizedTest` parametresi OWNER login ile kısa bir QR oluşturup iptal eder. Derleme/gerçek telefon testi Windows/Android üzerinde yapılmalıdır.

## Yeni uç noktalar

- `GET /api/client/pairing-qr/network-options` — yalnız localhost ve yetkili yönetici oturumu, LAN IPv4 adaptörleri.
- `POST /api/client/pairing-qr` — yalnız localhost + yönetici oturumu + aktif Alarm lisansı, `{ "selectedHost":"192.168.1.10" }`.
- `GET /api/client/pairing-qr/status` — yönetici oturumunda `PENDING`, `PAIRED`, `EXPIRED` veya `REVOKED`; bilet/erişim token'ı dönmez.
- `DELETE /api/client/pairing-qr` — yalnız aynı yönetici oturumu, bilet iptali.
- `POST /api/client/pair-qr` — yalnız **HTTPS TCP 5443**, tek kullanımlık bilet, SSL pin doğrulaması **mobil istemcinin sorumluluğunda**.

Yeni uç noktalar için `schemaVersion=1`, `pairingMethod=QR_TICKET_V1` ve QR payload şeması `MOBILE_TEAM_HANDOFF_QR_V05.md` dosyasında belgelenmiştir. Yanlış bilet, tekrar kullanım, eski oturum ve HTTPS olmayan bağlantı kararlı JSON hata kodu döndürür.

## Teslim kapsamı

- Core C# bilet, güvenlik ve normal client yaratma servisleri; mevcut API ile geriye uyumlu HTTP uç noktaları.
- Orijinal PROGNODE Core Ayarlar ekranına eklenmiş yerel QR üretimi; internet/CDN gerektirmeyen ve lisans bildirimi içermeyen QR renderer.
- Tek kullanımlık biletin yalnız hash'inin tutulması, atomik tek istemci yaratma, 120 sn TTL, rate-limit, mevcut istemcileri bozmama.
- Kaynağa eklenen `tests/QrPairingCoreContract` deterministik .NET test projesi ve PowerShell canlı endpoint testleri.
- Referans olarak mevcut onaylı **HF3+ Trend Studio interaktif HTML önizlemesi** `previews/` altında. **HF3+ prototipinin gerçek Core Historian entegrasyonu bu QR paketinde tamamlandı olarak sunulmuyor;** RC6.4.6'nın mevcut çalışan Trend Studio kodu korunmuştur.

**Doğrulama notu:** Bu ortamda .NET 10 SDK bulunmadığından C# derlemesi ve Windows/Samsung fiziksel cihaz testi yapılamamıştır. JS sözdizimi ve tarayıcı içindeki QR ekranı, yerel offline QR SVG kodlanması/çözülmesi, statik güvenlik kaynak kontrolleri yapılmıştır. `dotnet run --project tests/QrPairingCoreContract/QrPairingCoreContract.csproj` komutunu Windows derlemesinden sonra çalıştırın ve sonucu paylaşın.
