# PROGNODE RC6.4.5 HF1 — Fullscreen Trend Studio (tam kaynak ZIP)

Bu paket RC6.4.4 tabanındaki **tüm Core/Server/Agent kaynaklarını** ve yeni, tam ekran Trend Studio HTML/JS kodunu içerir. Tek başına kurulum EXE'si değildir.

## Önceki RC6.4.5 ZIP'inde tespit edilen problem

- Core health sürüm numarası yanlışlıkla RC6.4.4 kalmıştı ve UI bu değeri sol alta yazıyordu.
- Eski RC6.4.4 başlatıcısı ZIP kökünde duruyordu.
- Yeni RC6.4.5 başlatıcısı, daha önce TCP 5080'i kullanan eski Core'u kapatmıyordu.

Bu HF1 sürümünde bu üç sorun düzeltildi. Eski başlatıcılar `docs/legacy-launchers` altına taşındı.

## Windows geliştirici testi

1. Eski proje **data klasörünü yedekle**. Mümkünse eski Core'u ve `PROGNODECore` Windows Service'i durdur. Bu script mevcut müşteri verisini **taşımaz, silmez veya temizlemez**.
2. ZIP'i ayrı bir klasöre çıkar. Eski kayıtların bulunduğu klasörü kullanmak için yalnızca **eski Core kapalıyken**, eski `data` klasörünü (veya kendi kullandığın DataRoot'u) yeni proje klasöründe `data` altına kopyala. Üretim kurulumunda ProgramData kullanıyorsan path'i ayrıca `Prognode__DataRoot` ortam değişkeninde belirt.
3. Yeni proje klasöründe **yalnız `START_PROGNODE_RC6_4_5.cmd`** çalıştır. Windows Service'i durdurmak için gerekirse Yönetici olarak aç.
4. Script eski developer Core'u durdurur, tüm çözümü build eder, yenisini başlatır ve `/api/health` ile **gerçek sunulan Trend Studio** dosyasını test eder.
5. Açılan tarayıcıda gerekirse Ctrl+F5 yap. Sol altta `v0.7.2-rc6.4.5-fullscreen-hf1` görünmeli. Tam ekran panelde `Historian'dan ekle` butonu olmalı.

Ayrı teşhis: `powershell -ExecutionPolicy Bypass -File .\CHECK_PROGNODE_RC6_4_5.ps1`

> Bu paket yalnızca geliştirici kaynak ve önizlemedir. Windows `.NET 10` derlemesi ve gerçek PLC/Historian uçtan uca testi kullanıcının Windows PC'sinde tamamlanmalıdır.
