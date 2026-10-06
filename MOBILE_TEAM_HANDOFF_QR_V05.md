# PROGNODE Mobile V0.5 — QR ile Güvenli LAN Eşleştirme: Core RC6.4.7 sözleşmesi

Referans Core: `0.7.2-rc6.4.7-qr-pairing`. Mobile: mevcut tek Flutter Android/iOS deposu, V0.4.1 akışı korunur. **Core tarafında kaynak uygulandı; Flutter QR okuyucu bu Core ZIP'inde çalışır/derlenmiş APK olarak teslim edilmedi.** Android 16 / Samsung One UI 8.5 cihaz testi mobil ekipte.

## Kullanıcı akışı

Core bilgisayarında yetkili OWNER/ORGANIZATION_ADMIN, Ayarlar > İstemci Erişimi > QR ile Cihaz Ekle. Aktif LAN HTTPS 5443 yoksa QR oluşturulmaz. Çoklu ağ arayüzünde erişilebilir LAN IPv4 kullanıcı tarafından seçilir. Mobilde QR okutulur, sertifika pinlenir, ticket harcanır, mevcut `clientId` / `accessToken` / `server` JSON yanıtı güvenli depoya kaydedilir. Sonraki açılışlarda tekrar QR gerekmez. LAN ücretsiz, REMOTE yıllık 5/10/25 ve ayrı kayıt.

## QR taşıma biçimi (Core tarafından üretiliyor)

`POST http://127.0.0.1:5080/api/client/pairing-qr` + `X-PROGNODE-Session` (sadece Core bilgisayarında yönetici oturumu), istek `{"selectedHost":"192.168.1.10"}`. Yanıt:

```json
{"schemaVersion":1,"pairingMethod":"QR_TICKET_V1","qrPayload":"<base64url(UTF-8 JSON)>","expiresAtUtc":"2026-09-27T13:02:00Z","expiresInSeconds":120,"selectedHost":"192.168.1.10","displayName":"PROGNODE Core"}
```

QR tarandığında `qrPayload` açılarak (RFC 4648 base64url, padding yok) bu JSON elde edilir:

```json
{"v":1,"type":"PROGNODE_LAN_PAIRING","serverId":"<Core GUID>","displayName":"<Core adı>","lanHosts":["192.168.1.10"],"httpsPort":5443,"certSha256":"<64 BÜYÜK HEX>","pairingTicket":"<32 random byte base64url>","expiresAtUtc":"<ISO8601 UTC>"}
```

Telefon `type`, `v=1`, GUID, TTL, 64 haneli hex fingerprint, tek kullanımlık ticket biçimini doğrular; bilinmeyen sürümü reddeder. **QR yalnızca fiziksel yetkili Core ekranından tarandıysa fingerprint güven başlangıcı olabilir**; e-posta/mesajla gelen QR otomatik güvenilir değildir. Sertifika pin'i (`SHA256(X509Certificate2.RawData)` eşdeğeri) HTTPS handshake sırasında, ticket/şifre/transmit öncesinde kontrol edilir. HTTPS sunucusunun `serverId` değeri QR ile uyuşmalı. Pin uyuşmazlığında asla HTTP/UDP fallback yoktur. Core sertifikası yenilenirse yeni QR ve açık kullanıcı doğrulaması gerekir.

## Eşleştirme isteği ve yanıtı

`POST https://<selected-host>:5443/api/client/pair-qr`, JSON:

```json
{"serverId":"<Core GUID>","pairingTicket":"<QR ticket>","clientName":"Galaxy S25","platform":"ANDROID","devicePublicKey":"<base64 public key>"}
```

Başarılı 200: mevcut `/api/client/pair` ile aynı alanlar: `clientId`, `accessToken`, `server`. `accessToken`, cihaz özel anahtarı ve sertifika pini platform secure storage içinde saklanır, loglanmaz. Bilet tek kullanımlıktır; parallel isteklerde yalnızca ilk kalıcı istemci oluşturma başarılı olur. Bildirimler `/api/mobile/v2/notifications?cursor=...`, ACK `POST /api/mobile/v2/alarms/{occurrenceId}/ack` (geçerli Bearer + oturum) — **yeni QR bu API'leri değiştirmez**.

Kararlı hata kodları: `HTTPS_REQUIRED`(426), `SERVER_MISMATCH`(400), `INVALID_TICKET`(400), `EXPIRED`(410), `REVOKED`(410), `ALREADY_USED`(409), `RATE_LIMITED`(429), `ALARM_LICENSE_REQUIRED`(403). Global düz HTTP LAN dinleyicisi yoktur; yalnız localhost 5080 HTTP, LAN 5443 HTTPS.

## Geriye uyumluluk ve test

Eski 6 haneli `GET /api/client/pairing-code` + `POST /api/client/pair` devam ediyor, ancak manuel yolu da sertifika fingerprint'i önceden doğrulayacak şekilde kullanın. Yeni V0.5 Flutter `QR ile Eşleştir` kamera izni isteyecek; reddedilirse manuel yöntem kalacak. `serverId`, doğrulanmış fingerprint ve secure token APK üzerine güncellemede korunacak. IP değişikliği durumda aynı sertifika/serverId ile güvenli keşif yapılabilir.

Kabul testleri: Android 16 / One UI 8.5 aynı Wi-Fi'da tek QR; tamamen internetsiz LAN'da alarm/ACK; paralel bilet yarışı; kullanılmış/süresi dolmuş/iptal ticket; sahte sertifika/sahte UDP; admin oturumsuz localhost ve LAN'dan QR üretimi reddi; ağ/uygulama yeniden başlatmada pairing korunur. Android arka plan/FGS, FCM/APNs remote push, APK imzalama ve bildirim üzerinden ACK ayrı açık işlerdir; QR bunları tamamlanmış yapmaz.
