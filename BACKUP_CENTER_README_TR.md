# PROGNODE RC6.4.7 HF3 — Backup Center

Temel HF2 QR + HF3+ Trend Studio korunur; Backup Center eklenmiştir.

## Kapsam

- SQLite WAL veritabanından **online tutarlı snapshot**, cihaz/Tag/alarm/Batch/Historian/Trend kayıtları ve bildirim günlüğü.
- `data/` altında sunucu kimliği, mevcut imzalı lisans, cloud/remote state ve `audit/changes.jsonl`; TLS özel anahtarları ve PFX kesinlikle **dahil değildir**.
- Ayarlar → Backup Center: şifreli tek dosyalık `.pgnbackup` oluşturma, indirme, içe alma/doğrulama ve işlem günlüğü.
- AES-256-GCM parça doğrulama + PBKDF2-SHA256. Parola **unutulursa geri yüklenemez**, buluta/parola kasasına gönderilmez.
- **İlk HF3 başlatma:** yeni başlatıcı eski `data/` içinde DB bulursa, eski Core'u durdurmadan önce `PREUPGRADE-HF3-*` şifreli ve doğrulanan bir güvenlik kopyası üretir. Parola ortam değişkeninde yoksa başlatıcı parolayı gizli olarak sorar; yedek başarısızsa eski Core'a dokunmaz.
- Otomatik günlük backup: yalnız sistem ortam değişkeni `PROGNODE_BACKUP_PASSWORD` (en az 12 karakter) tanımlıysa; 02:00 yerel saatten sonraki ilk kontrol, 30 günlük otomatik yedek saklama. `appsettings.json` → `Prognode.Backup.DailyHourLocal/RetentionDays` değiştirilebilir. Otomatik snapshotlar Core bilgisayarındaki kardeş `backups/` klasörüne yazılır; güvenli bir harici disk/NAS'e ayrıca kopyalanmalıdır.
- HF3+ Düzeni Kaydet ile UI düzeni artık Core `data/ui/trend-hf3plus-layout.json` içine de kaydedilir; farklı tarayıcıda yerel düzen yoksa Core kaydı kullanılır.

## Geri yükleme

**Çalışan Core'a veritabanını kopyalama.** Ayarlar'da dosyayı yükleyip parolayla doğrulayın; ekranda doğrulanan dosyanın adı gösterilir. Core PC üzerinde `RESTORE_PROGNODE_BACKUP.cmd` çalıştırıp dosya yolunu ve **gerçek mevcut** `data` klasörünü girin; betik çalışan servisi durdurur ve 5080 portunu kontrol eder, bütün dosyaları/değerleri doğrular. İstenen `RESTORE` onayından sonra mevcut `data` kardeş `.before-restore-*` klasörüne taşınır, doğrulanmış yedek yerine alınır. `RESTORE` kesinlikle canlı API üzerinden çalışmaz.

**Sistem taşıma:** imzalı `.pgnlicense` doğrulanması yeni Core'da tekrar yapılır; lisans makineye veya siteye bağlıysa portal üzerinden yeniden tanımlanmalıdır. HTTPS sertifika özel anahtarı taşınmadığından eski mobil eşleşmeler yeni PC'de otomatik güvenilmez; yeni sertifika ile QR üzerinden tekrar eşleştirilmelidir. Yedekteki `appsettings.json` yalnız **inceleme kopyası** olarak dışarı alınır; yeni PC'nin sertifika thumbprint'i, servis ve URL ayarları otomatik üzerine yazılmaz.

**Geliştirme ortamı:** .NET 10 SDK gerekli. Linux ortamında .NET bulunmazsa testler Windows bilgisayarında çalıştırılmalı. Manuel yedeği indirdikten sonra doğrulama/geri yükleme için `dotnet run --project tools/Prognode.Backup.Restore -- verify <FILE> --data <DATA>` (parola `PROGNODE_BACKUP_PASSWORD` ortam değişkeninde).

**Sınırlamalar:** Yedek **Core işletme verisi/yapılandırmasıdır**, Core kaynak kodu/EXE/PLC programı değildir. Tarayıcıda kaydedilmemiş kişisel ayarlar otomatik yedeğe girmez. Otomatik çalışma için Windows servis hesabına backup dizininde yazma izni gerekir.
