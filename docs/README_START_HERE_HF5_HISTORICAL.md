# PROGNODE RC6.4.7 HF4.1 — QR + Trend HF3+ + Backup + LAN TLS onarımı

**Önce `HF4_1_LAN_TLS_REPAIR_README_TR.md` dosyasını okuyun.** HF4 taban notları `LAN_ACCESS_WIZARD_README_TR.md` içindedir. HF3'ün tam kaynak paketine mobil ekibin Public/Private LAN erişim sihirbazı eklendi.

- Windows PC'de `.NET 10 SDK` ve Windows Agent ile `START_PROGNODE_RC6_4_7_HF4_LAN_WIZARD.cmd` çalıştırın.
- **Ayarlar → Mobil Erişim** içinde NIC/IP ve telefon IP'leri veya onaylı tesis CIDR'ını seçin. Public arayüzü ayrıca onaylayın.
- **LAN erişimini hazırla**, sonra yalnız Core PC sistem tepsisindeki PROGNODE Agent menüsünden **Complete LAN Access Setup (UAC)**; sonra HTTPS durumunu doğrulayın, gerçek telefonla test edin ve QR eşleştirin.
- Herhangi bir Windows ağ profilini veya router/WAN portunu değiştirmeyin. Üretimde yerel yükseltilmiş betik imzalı ve admin korumalı dosyalardan çalışmalıdır.
- Mevcut `data/`, lisans, `server-access.json` ve HTTPS sertifikasını KORUYUN. ZIP'i yeni klasöre açtıysanız eski `data/` klasörünü yeni köke kopyalayın **veya** `Prognode__DataRoot` ile eski tam yolu belirtin. Aksi halde boş kurulum açılır. Başlangıç HF4 öncesi şifreli yedeği alır. Backup Center ve Trend HF3+ pakettedir; önceki yedek parolanızı güvenle saklayın.
- **Windows/.NET gerçek derleme ve fiziksel telefon kabul testleri henüz yapılmadı.** Kaynak/DEV paketidir; tesis verisinde test etmeden devreye almayın.

Orijinal HF3 sürüm açıklamaları `BACKUP_CENTER_README_TR.md` içindedir.

## HF4.1 P0 (27.09.2026)

**Yeni sürüm:** `START_PROGNODE_RC6_4_7_HF4_1_TLS_REPAIR.cmd`

Önce `HF4_1_LAN_TLS_REPAIR_README_TR.md` dosyasını okuyun. HF4'ün Schannel
36870 özel anahtar erişimi ve yanlış `httpsReady` raporlaması giderildi.
Doğrulama: `TEST_PROGNODE_LAN_WIZARD.ps1`. Kurulu eski `data/`, HTTPS sertifikası,
`server-access.json` ve eşleşmiş cihazları koruyun; üretimde imzalı helper ve
Windows + gerçek mobil kabul testleri olmadan devreye almayın.


## HF4.1.1 derleme düzeltmesi (27.09.2026)

Visual Studio ekranındaki BackupArchive.cs ve LanAccessWizardService.cs hataları için `HF4_1_1_BUILD_HOTFIX_README_TR.md` belgesini okuyun. Önce `TEST_HF4_1_1_BUILD_HOTFIX.ps1` çalıştırın. Eski `data/` veya TLS sertifikası silinmeyecek.

## HF4.1.2 LAN Agent JSON hotfix (27.09.2026)

See `HF4_1_2_AGENT_JSON_FIX_README_TR.md` for the fix for empty JSON responses
when a prepared LAN UAC request has expired or the Windows tray Agent is
connected to an older/other Core instance. Preserve the existing data root,
license, HTTPS certificate and paired clients. No firewall/TLS trust bypass.
