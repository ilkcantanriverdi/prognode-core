# PROGNODE RC6.4.7 HF4.1.2.1 — LAN UAC PowerShell sözdizimi düzeltmesi

**Temel:** `PROGNODE_RC6_4_7_HF4_1_2_AGENT_JSON_FIX_FULL.zip`. Bu sürümde Core/Agent derleme sürümü HF4.1.2 olarak kalır; HF4.1.2.1, sadece *paket/yardımcı betik düzeltme* sürümüdür.

## Sorun ve düzeltme

`LAN_ACCESS_ELEVATED.ps1` dosyasının `Resolve-CoreProcessSid` işlevinde `NT SERVICE\PROGNODECore` hesabının SID dönüşümü sırasında fazladan bir `)` vardı. Windows PowerShell `Missing closing '}'`, `Try missing Catch`, `Unexpected token ')'` gibi birden fazla zincirleme **ParserError** üretiyordu. Bu, HTTPS sertifika hatasının tekrar oluştuğunu göstermez: yükseltilmiş onarım yardımcısı daha hiçbir işlem yapmadan duruyordu.

**Yapılan değişiklikler:** Yanlış SID dönüşüm satırı güvenli ve açık `try/catch` bloğuyla değiştirildi. Normal Core başlangıcına, yedekleme veya eski Core'u durdurma işleminden **önce**, `Parser.ParseFile` ile LAN yardımcısının sözdizimini doğrulayan koruma eklendi. Bağımsız `TEST_LAN_HELPER_PARSER.ps1` testi eklendi. QR, HF3+ Trend Studio, Backup Center ve diğer Core/Agent kaynakları değiştirilmedi.

## Windows test / başlangıç

1. Sistem tepsisindeki eski PROGNODE Agent'ı **Exit** ile kapat. Mevcut `data` ve lisans klasörlerini koru; yeni ZIP'i ayrı klasöre aç; mevcut HTTPS sertifikasını **silme/yeniden oluşturma**.
2. PowerShell'i yeni ZIP'in kökünde aç ve **yönetici yetkisi gerektirmeden** `powershell.exe -NoProfile -File .\TEST_LAN_HELPER_PARSER.ps1` komutunu çalıştır. `PASS` beklenir; bu test sertifika onarımı yapmaz.
3. Önceki gerçek verilerinin bulunduğu `Prognode__DataRoot` ortam değişkenini aynı klasöre işaret ettir ve `START_PROGNODE_RC6_4_7_HF4_1_2.cmd` ile Core/Agent'ı **yeni klasörden** derleyip başlat. Eski Agent'ın çalışan bir kopyası kalmamalı. Başlatma, aynı sözdizimi testini tekrar yapar; Agent derlemesi kökteki güncel PS1'i bin klasörüne kopyalar.
4. Ayarlar → Mobile Access → Public Wi-Fi / izin verilen telefon IP → **Prepare LAN access**; 3 dakika içinde Agent tepsisinde **Complete LAN Access Setup (UAC)** işlemini tamamla.
5. Ardından **Verify / refresh** ile `TLS_OK`, gerçek localhost HTTPS el sıkışması ve gerçek telefon IP'sinden bağlantı testini ayrı ayrı doğrula. Bu paket **yalnızca ParserError sorununu** düzeltir; özel anahtar ACL, Schannel 36870 veya firewall sorunlarının sahada çözüldüğü iddiası değildir.

**Hızlı düzeltme (mevcut Debug klasörünü yeniden derlemeden):** Eski Agent'ı kapattıktan sonra bu paketteki `LAN_ACCESS_ELEVATED.ps1` dosyasını kendi proje köküne ve `src\Prognode.Agent.Windows\bin\Debug\net10.0-windows10.0.19041.0\LAN_ACCESS_ELEVATED.ps1` konumuna kopyalayabilirsin. Sonra PowerShell parser testini çalıştır ve Agent'ı yeniden başlat. Yalnız geliştirme ortamında uygulanmalı; imzalı ticari kuruluma düz PS1 kopyalanmaz.

**Koruma:** Asla `Everyone` için özel anahtar izni verme, daha önce eşleşmiş telefona ait HTTPS sertifikasını sebepsiz yere döndürme ve firewall'u bütün Public arayüzlere açma. Kaynak PS1 geliştirme kopyası imzasızdır; ticari dağıtımdan önce Authenticode imzası ve yöneticiye ait kurulum klasörü ACL'si gerekir.
