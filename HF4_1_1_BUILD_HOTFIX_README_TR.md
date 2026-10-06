# PROGNODE RC6.4.7 HF4.1.1 — Visual Studio derleme düzeltmesi

Bu paket, HF4.1 TLS onarımı + QR + HF3+ Trend Studio + Backup Center + LAN Wizard kaynaklarının **tamamını** içerir. HF4.1 paketindeki Visual Studio ekran görüntüsünde görülen derleme hataları hedeflenmiştir.

## Düzeltilenler

- `src/Prognode.Backup/BackupArchive.cs`: Var olmayan `Directory.SetUnixFileMode` (iki nokta) yerine dizin yolunu da destekleyen `File.SetUnixFileMode` kullanıldı. Yalnız Unix'te, oluşturulan geçici klasörlere `0700` uygulanır; Windows kod yolu değişmez.
- `src/Prognode.Web/LanAccessWizardService.cs`: `ToArray()` sonucu olan `clients` dizisinin `Count` metot grubu yerine `Length` özelliği kullanıldı.
- `BackupArchive.cs`: Eski PBKDF2 yapıcısı yerine aynı parola/salt/310000 iterasyon/SHA-256/32 bayt parametreleriyle statik `Rfc2898DeriveBytes.Pbkdf2` kullanıldı; var olan `.pgnbackup` formatı korunur. `stackalloc` uzunluk tamponu döngü dışına alındı.

## Kontrol

Önce kaynak dosyalarını eski klasörünüzün ÜZERİNE karıştırmak yerine ZIP'i yeni bir klasöre açın. Daha sonra yeni kökte Visual Studio ile `PROGNODE.sln` dosyasını açın. Paket içindeki `TEST_HF4_1_1_BUILD_HOTFIX.ps1` betiğini PowerShell'de çalıştırabilir veya `dotnet build .\PROGNODE.sln -c Debug` komutunu kullanabilirsiniz. Backup davranışları için `dotnet run --project .\tests\BackupContract\BackupContract.csproj` komutu eklenmiştir.

**Önemli:** Bu teslimde Windows üzerinde .NET 10 SDK / Visual Studio derleme testi ve gerçek Windows TLS/telefon testleri yapılamadı. Kod kaynak düzeyinde değiştirildi; test çıktısını paylaşarak kalan problemleri netleştirebiliriz. Güncellemeden önce `data/`, imzalı lisans, `server-access.json`, mevcut HTTPS sertifikası ve mobil eşleşmelerini koruyun. Bu hotfix'in uygulanması bunların silinmesini gerektirmez. Kurulu Core servisini ve üretim verisini doğrudan test için değiştirmeyin. Ayrıntılı TLS kabul adımları önceki `HF4_1_LAN_TLS_REPAIR_README_TR.md` belgesinde.
