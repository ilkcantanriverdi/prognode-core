# PROGNODE RC6.4.7 HF6.5 — Manuel SAS + Backup İnceleme

**Temel:** HF6.4 / Mobile V0.5.7. Müşteri dağıtımı DEĞİL; Win11 / gerçek telefon kabul adayı.

## 1. Önce mevcut kurulumunuzu koruyun

`data` klasörü, gerçek `Prognode__DataRoot`, `.pgnlicense`, `%ProgramData%\PROGNODE\config\lan-https.json`, Windows LocalMachine HTTPS sertifikası ve var olan mobil eşleşmeler korunmalı. Yeni ZIP ayrı klasöre açılmalı. **Sertifika oluşturmayın veya Windows güvenlik duvarını genişletmeyin.** HF6.4 üzerinde çalışan QR, Alarm/ACK, Trend Studio ve Backup Center dosyaları temel alınmıştır.

## 2. Yeni manuel eşleştirme

Core yönetici oturumu → Ayarlar → Sunucu ve QR → Kod ile eşleştir. Yalnızca gerçek TLS_OK olduğunda 120 saniyelik altı haneli tek kullanımlık OTP ve bağımsız 12 karakterli kontrol kodu görünür. Telefonun kendi TLS sertifikasından hesapladığı SAS, **fiziksel PC ekranındaki kodla birebir karşılaştırılmalı**. Eşleşmezse cihaz kimliği ya da OTP ile eşleşmeye devam etmeyin. Bilgisayardan yeni kod üretildiğinde eskisi geçersizdir. Tam 64 hex SHA-256 yalnızca Gelişmiş ekranındadır. QR ile eşleştirme korunur.

Yeni API: `GET /api/client/manual-pairing`, `GET /api/client/manual-pairing/certificate` ve `POST /api/client/manual-pairing/cancel` (yalnız fiziksel localhost + imzalı lisansın yetkili yönetici oturumu). `POST /api/client/pair` eski sözleşmeyi korur ancak tek kullanımlık 120 sn kod ve beş hatalı deneme sınırı kullanır.

## 3. Ekrandaki Backup Center mesajı

`Doğrulandı` sonucu, yüklenen `.pgnbackup` dosyasının parolasının, AES-GCM bütünlüğünün ve manifest SHA-256 kayıtlarının doğru olduğunu, henüz canlı verilerin üzerine yazılmadığını gösterir. Başka Server ID mesajı bir **bozulma veya başarısız yedek hatası değil**, kimlik geçişi uyarısıdır. ZIP içindeki `RESTORE_PROGNODE_BACKUP.cmd` yalnızca Core durdurulduktan sonra çalıştırılır. Farklı Server ID'de kullanıcının `MIGRATE`, tam kaynak ID ve `RESTORE` girmesi gerekir; önceki veri klasörü rollback için saklanır. Donanıma bağlı HTTPS özel anahtarı ve uzak lisans/remote binding yeni bilgisayara otomatik taşınmaz, açık doğrulama gerektirir. Müşteri veri klasörünü rastgele değiştirip sahte yeni Server ID oluşturmamalıdır.

### 0.0 MiB boyut yazısı

Önceki arayüz 50 KiB altındaki dosyaları 0.0 MiB olarak yuvarlıyordu. Yeni arayüz küçük dosyaları KiB olarak gösteriyor. Dosyanın indirilebilir gerçek büyüklüğünü ve içeriğini ayrıca doğrulayın.

## 4. Windows kabul testleri

```powershell
# Yeni kaynak klasöründe (.NET 10 SDK yüklü)
dotnet build .\PROGNODE.sln -c Debug
dotnet run --project .\tests\ManualPairingContract\ManualPairingContract.csproj
dotnet run --project .\tests\QrPairingCoreContract\QrPairingCoreContract.csproj
dotnet run --project .\tests\BackupContract\BackupContract.csproj
```

Samsung testleri: gerçek QR korunuyor mu, kamera olmadan SAS doğrulama/yanlış sertifika, ikinci defa OTP reddi, 5 hatalı deneme limiti, mevcut telefon ACK grant/revoke ve occurrence ACK korunuyor mu? Backup farklı Server ID uyarısı yedek üzerinde yeni canlı data değişikliği yapmadan gösteriliyor mu? Test tamamlanmadan ticari dağıtıma geçmeyin.

**Bilinen sınırlar:** Bu ortamda .NET SDK yok; Core native Windows build, Flutter/APK, gerçek MITM ve end-to-end restore testi henüz yapılmadı. `tests/ManualPairingContract` ve `mobile/MANUAL_PAIRING_V058_GOLDEN_VECTOR.json` Flutter ekibine verilecek ortak sözleşmedir.
