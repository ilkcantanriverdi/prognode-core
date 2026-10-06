# PROGNODE HF6.4 — Core / Mobile V0.5.7 P0 entegrasyonu

**Temel:** HF6.3 test edilen QR / Trend Studio / Backup Center kaynağı. Ayrı klasöre açın; aynı veri klasörünü ve HTTPS sertifikasını koruyun. Bu bir KAYNAK ve WINDOWS KABUL ADAYI; bu ortamda .NET SDK bulunmadığından Windows derleme ve fiziksel telefon regresyonu tamamlanmış değildir.

## Yeni Core özellikleri
- Admin (OWNER/ORGANIZATION_ADMIN) yalnız yerel Core ekranından **Ayarlar → Sunucu ve QR → Bağlı mobil cihazlar** kısmında her telefona ayrı Alarm görüntüleme/ACK yetkisi verebilir. QR tek başına ACK vermez. Önceden eşleşmiş telefonlar USER_SESSION modunda kalır. DEVICE ACK için cihazın eşleşmiş public key'i mevcut olmalıdır.
- `GET /api/mobile/v2/device/access`: eşleştirilmiş bearer üzerinden cihazın mevcut izni.
- `GET /api/mobile/v2/alerts/pending`: yalnız RepeatUntilAcknowledged seçilmiş, ACK bekleyen olaylar; erişim ACK oturumu olmadan okunabilir; `404` eski Core'da destek yok anlamına gelir.
- `POST /api/mobile/v2/alarms/{occurrenceId}/ack`: yetkili DEVICE + geçerli bearer ile veya mevcut USER_SESSION ile; ACK sonucuna göre 200/401/403/409/404. Device-only ACK her çağrıda Core tarafında kontrol edilir. Audit SQLite'da `mobile_ack_audit` tablosundadır. Aktif alarm koşuluna PLC yazılmaz.
- `alarm_runtime_snapshots` tablosunda aktif ve CLEAR sonrası ACK bekleyen occurrence kimlikleri korunur. AlarmEngine açılışta durumu devralır. Yeni alarm oluşumu eski occurrence'ı geçersiz kılar. Bu mekanizma PLC ile fiziksel kabul testine tabidir.

## Bilerek devreye alınmayanlar
- Opsiyonel PIN/Biometric: ayrı Core-verified PIN tasarımı/testi olmadan etkinleştirilmedi.
- CRITICAL_ACK profili: Kritik öncelik tek başına tekrarlayan ses açmamalıdır. Şimdilik açıkça `RepeatUntilAcknowledged` seçilmiş alarmlar `ACK_REMINDER` profilinde; profil genişletmesi sonraki sözleşmeye bırakıldı.
- GSM arama, DND geçersiz kılma, iOS offline arka plan garantisi, kesintisiz siren veya bulut teslim garantisi yoktur.

## Kurulum ve test
1. Mevcut `data`, `server-access.json`, `prognode.db`, lisans ve sertifika yapılandırmasını yedekleyin. Yeni ZIP'i ayrı klasöre açın. `Prognode__DataRoot` değerini önceki gerçek veri klasörünüzle eşleyin.
2. Windows .NET 10 ile `dotnet build .\PROGNODE.sln -c Debug` yapın. Mevcut QR ve mobil alarm geçmişi kaybolmamalı.
3. `dotnet run --project .\tests\MobileDeviceAckContract\MobileDeviceAckContract.csproj` ile mevcut token, explicit grant, revoke, occurrence snapshot geri yükleme sözleşmelerini test edin.
4. Eski telefonu hiçbir izin değişikliği yapmadan açın: USER_SESSION gerekliliği devam etmeli. Yeni QR ile eklenmiş test telefona OWNER yetkisiyle DEVICE ACK verin. Eşleşmiş cihaz adı kimlik değildir; immutable ID doğrulanır.
5. İnternet tamamen kesikken `device/access`, `alerts/pending` ve occurrence ACK uçlarını test edin. Kayıp/çalıntı telefonu revoke edin; eski bearer artık çalışmamalı.
6. Test alarmlarını simülatörle tetikleyin: ACTIVE/REMINDER aynı occurrence, ACK farklı telefondan tekrarları durdursun; CLEAR sonrası yeniden aktivasyon eski ID için 409 vermeli. Core yeniden başlatıldıktan sonra pending korunmalı.
7. Android 16 gerçek cihazda ekran açık/kilitli, Doze, Wi-Fi kes/reconnect, 7.2 s ses kanalını fiziksel olarak test edin. Bildirim gelmediğinde teslim edilmiş görünmemeli.
8. Bu testler, Windows servis/modu ve Backup/Restore testleri tamamlanmadan müşteri dağıtımı yapılmaz.

## Mobil ekibe API notu
- `DEVICE` izni yalnızca admin açıkça verdiğinde `canAcknowledge=true` olur. Mobile yalnız `canAcknowledge` true ve `ackAuthMode=DEVICE` ise oturumsuz ACK çağrısı yapmalı. `USER_SESSION` durumunda Core-local login kullanılmaya devam eder.
- `alerts/pending` yanıtında `profile=ACK_REMINDER`, `schemaVersion=1`; `CRITICAL_ACK` henüz aktif değil. Android OS sesini, foreground service ve bildirim iznini Core garanti edemez.

**Korunan tekrar sırası:** Core yeniden başladığında aynı occurrence için son journal REMINDER numarası ve zamanı geri alınır; bildirimler yeniden 1 ile başlamaz. Fiziksel yeniden başlatma/regresyon testi yine zorunludur.
