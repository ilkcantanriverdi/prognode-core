# ProgNode Core çalışma sınırları

- Kaynak temeli PROGNODE_HF6_7.zip. `mobile/Prognode.Mobile` eski referanstır; güncel mobil repo `C:\prognode\prognode-mobile`.
- Saha PLC, alarm, historian ve SQLite Core'a aittir. Bulut web/account/control bu repoya taşınmamalı.
- SDK `global.json` ile seçilir. Windows üzerinde `pwsh -File scripts/Validate-Baseline.ps1` çözümü derler ve altı console contract projesini ayrı çalıştırır. `dotnet test` tek başına bu console testlerini çalıştırmaz.
- İlave bağımlılıksız kontroller: `node tests/test-trend-logic.cjs`, `python tests/test_hf65_manual_pairing_static.py`.
- NuGet `packages.lock.json` dosyalarını koru. Baseline kontrolü locked restore kullanır; bilinçli bağımlılık güncellemesinde `-UpdateLockFile` ver ve diff'i incele.
- Gerçek servis/PLC/veri dizini yerine izole test verisi kullan. Servis kurulumu, firewall/TLS kurulumu ve backup restore normal unit test adımı değildir.
- QR/TLS pinning, occurrence bazlı ACK, Ed25519 imza doğrulama ve ticari lisans davranışını koru.
- Ücretsiz deneme (TRIAL): 14 gün, 1 PLC cihazı, 20 tag, Alarm+Historian, Remote Access 2 cihaz, ek süre yok. Lisans Core içinden "Start free trial / Connect account" (CoreLinkService, /api/license/link/*) ile alınır; Cloud tarafı ve uyumluluk testi prognode-cloud `scripts/test-license-core-interop.cjs`.
- Build/console testleri geçse bile fiziksel cihaz, installer, saha performansı ve production onayı ayrı raporlanır.
- MQTT ve OPC UA field-test v1 sınırları `docs/PROTOCOL_CONNECTORS_V1.md` içindedir. MQTT yalnız topic abonesidir; OPC UA yalnız güvenilir SignAndEncrypt NodeId okumasıdır. Gerçek broker/OPC sunucusu kabulü ayrı yapılmalıdır.
