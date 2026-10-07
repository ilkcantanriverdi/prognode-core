// Mirrors Prognode.Contracts.Alarms.AlarmDefaults.RequiresAcknowledgement (Y6).
const ALARM_DEFAULT_REQUIRES_ACK = true;

const state = {
  page: "overview",
  devices: [],
  tags: [],
  devicePage: 1,
  tagPage: 1,
  tagValues: new Map(),
  protocols: [],
  notifications: [],
  license: null,
  licenseUsage: null,
  health: null,
  language: localStorage.getItem("prognode.language") || "en",
  theme: localStorage.getItem("prognode.theme") || "dark",
  selectedProtocol: null,
  modbusTestPassed: false,
  editingTagId: null,
  editingDeviceId: null,
  editingDeviceOriginal: null,
  alarmDefinitions: [],
  activeAlarms: [],
  alarmHistory: [],
  alarmHistoryCount: 0,
  alarmHistoryFilteredCount: 0,
  alarmHistoryRequestId: 0,
  alarmActivePage: 1,
  alarmDefinitionPage: 1,
  alarmHistoryPage: 1,
  editingAlarmId: null,
  trends: [],
  selectedTrendId: null,
  editingTrendId: null,
  trendPayload: null,
  agentStatus: null,
  serverClock: null,
  historianStats: null,
  historianConfigurations: [],
  editingHistorianId: null,
  hiddenTrendTags: new Set(),
  trendChartModel: null,
  trendHover: null,
  serverIdentity: null,
  serverPairing: null,
  remoteAccessStatus: null,
  remoteAccessClients: [],
  accessSession: null,
  importedAccount: null,
  quickStartActive: false
};

const translations = {
  en: {
    industrialMonitoring:"Industrial Monitoring", operations:"OPERATIONS", overview:"Overview", devices:"Devices", tags:"Tags", alarms:"Alarms", trends:"Trends", historian:"Historian", system:"SYSTEM", diagnostics:"Diagnostics", notifications:"Notifications", license:"License", settings:"Settings",
    coreRunning:"Core Running", localSystem:"LOCAL SYSTEM", systemHealthy:"System Healthy", localByDesign:"LOCAL BY DESIGN", knowBefore:"Know before it stops.",
    heroText:"Connect industrial devices, verify live data, create alarms and start recording — without sending process data to the cloud.",
    addFirstDevice:"+ Add your first device", monitoredTags:"Monitored Tags", tagPollingActive:"Live tag polling runtime is active", activeAlarms:"Active Alarms", alarmEngineNext:"Alarm evaluation is active", healthy:"Healthy",
    quickStart:"QUICK START", commissioningPath:"15-minute commissioning path", serviceFoundationRunning:"Service foundation is running", devEntitlementActive:"Local entitlement is active",
    firstRunTitle:"Commission PROGNODE in about 15 minutes", firstRunText:"A guided commissioning path from local access to device, live Tag, alarm and Historian recording.", firstRunDevice:"Connect a device", firstRunTags:"Verify a live Tag", firstRunAlarm:"Create an alarm", firstRunHistorian:"Enable Historian", firstRunStart:"Continue setup", firstRunLater:"Not now", firstRunAccessRequired:"Sign in to configure this PROGNODE Server.", firstRunAccessReady:"Local configuration session is active.", firstRunSignIn:"Sign in to start", setupAssistant:"Setup Assistant", setupComplete:"Setup complete", setupProgress:"Setup progress", commissioningAssistant:"COMMISSIONING ASSISTANT", localFirstFootnote:"PLC polling, alarms and Historian stay on this PROGNODE Server.", operationalStatus:"OPERATIONAL STATUS", accessMode:"Access", connectivity:"Connectivity", alarmPosture:"Alarm posture", recordingState:"Local recording", viewOnly:"View only", viewOnlyHelp:"Live monitoring remains available. Import a valid license and sign in to change configuration.", signedInLocal:"Signed in", attentionRequired:"Attention required", systemStable:"System stable",
    addDevice:"Add a device", modbusAvailable:"Modbus TCP is available", addDeviceArrow:"Add device →", verifyLiveTags:"Verify live tags", addFirstTagHelp:"Add 40001 and watch the value change live", addTagArrow:"Add tag →",
    createFirstAlarm:"Create your first alarm", alarmEngineMilestone:"Alarm monitoring", enableRecording:"Enable recording", historianMilestone:"Historian recording",
    architecture:"ARCHITECTURE", localSystemStatus:"Local system status", running:"Running", available:"Available",
    fieldConnectivity:"FIELD CONNECTIVITY", devicesText:"Add PLCs, meters, drives, RTUs and other industrial data sources.", addDevicePlus:"+ Add Device", noDevicesYet:"No devices yet", noDevicesText:"Connect a PLC, meter, drive, RTU or another supported industrial device.",
    device:"Device", protocol:"Protocol", status:"Status", host:"Host", poll:"Poll", created:"Created", editDevice:"Edit Device",
    tagsText:"Create live Tags from Modbus registers and verify value, datatype, status and engineering display.", addTagPlus:"+ Add Tag", noTagsYet:"No tags yet", noTagsText:"Add a Modbus Tag (for example 40001 or 30001) and PROGNODE will begin polling it immediately.",
    tag:"Tag", address:"Address", datatype:"Datatype", value:"Value", dataStatus:"Status", updated:"Updated", edit:"Edit",
    alarmsPlaceholder:"Alarm definitions will reference existing Tags. BOOL and integer WORD Tags can be selected as digital alarm sources and assigned alarm text.", trendsPlaceholder:"Fast local trends with time-range-aware historian queries.", historianPlaceholder:"Retention, storage health, CSV export and recorded tag configuration.", diagnosticsPlaceholder:"Connection latency, last poll, retry count, communication errors and device health.", modbusDiagnosticsSoon:"Connection health, polling and communication diagnostics.", nextMilestone:"System detail",
    notificationsText:"Browser-independent Windows desktop notifications through the PROGNODE Agent.", sendTestNotification:"Send Windows Test Notification", agentStatus:"AGENT STATUS",
    agentHelp:"Run the Windows Agent project. It stays in the system tray and receives local notification events even when the browser is closed.", coreNotificationApi:"Core Notification API", trayAgent:"Tray Agent", runSeparately:"Run separately", recentEvents:"RECENT EVENTS",
    entitlements:"ENTITLEMENTS", licenseText:"Your signed offline license controls enabled PROGNODE modules while runtime data remains local.", currentLicense:"CURRENT LICENSE", plan:"Plan", alarmDefinitions:"Alarm Definitions", recordedTags:"Recorded Tags", features:"FEATURES", licensedProtocols:"Licensed Protocols",
    settingsPlaceholder:"Users, backup/restore, network, update, security and data-flow settings will live here.",
    commissioning:"COMMISSIONING", configure:"Configuration", chooseProtocolText:"Choose how PROGNODE will communicate with the industrial device.", backProtocols:"← Back to protocols", selectedProtocol:"Selected protocol", deviceName:"PLC / Asset name", deviceNameHelp:"Use a globally clear equipment name, e.g. Boiler PLC, Main Pump PLC or Packaging Line PLC.", mockInfo:"Protocol-specific settings are stored in the local configuration database.",
    hostIp:"Host / IP", port:"Port", unitId:"Unit ID", pollInterval:"Poll interval (ms)", timeout:"Timeout (ms)", modbusTestInfo:"Ping and Modbus tests are optional. The device can be saved while offline.", cancel:"Cancel", ping:"Ping", pingOk:"Ping OK", pingFail:"Ping Failed", testConnection:"Test Modbus", saveDevice:"Save Device",
    addTag:"Add Tag", editTag:"Edit Tag", modbusDeviceOnly:"Configured Modbus TCP, Siemens S7 TCP, MQTT and OPC UA devices are available.", tagName:"Tag / Signal name", unit:"Unit", offset:"Offset",
    decimalPoint:"Decimal point", decimalPointHelp:"Integer example: raw 236 + 1 decimal = 23.6", displayDecimals:"Display decimals", displayDecimalsHelp:"FLOAT32 / REAL keeps its numeric value; this only controls how many decimal digits are shown.",
    tagAddressHelp:"Modbus read areas: 00001 Coil (FC01), 10001 Discrete Input (FC02), 30001 Input Register (FC04), 40001 Holding Register (FC03). Coil/Discrete use BOOL directly; Holding Register BOOL uses bit 0..15. Input Register uses WORD/numeric types. 32-bit values use two registers.",
    saveTag:"Save Tag", noDevicesConfigured:"No devices configured", configuredDevice:"configured device", configuredDevices:"configured devices", delete:"Delete",
    deviceSaved:"Device saved to PROGNODE configuration.", deviceDeleted:"Device deleted.", tagSaved:"Tag saved. Live polling started.", tagUpdated:"Tag updated.", tagDeleted:"Tag deleted.",
    testSuccess:"Modbus protocol test successful", testFailed:"Modbus test failed", testNotificationQueued:"Test notification queued for PROGNODE Agent.", noNotificationEvents:"No notification events yet.",
    alarmRuntimeActive:"Alarm engine and system alarms are active", alarmEngineRunning:"Digital + numeric alarm engine is running", addAlarmArrow:"Add alarm →",
    alarmsText:"Create digital or numeric alarms, acknowledge active alarms and review persistent alarm history.", addAlarmPlus:"+ Add Alarm", live:"LIVE", noActiveAlarms:"No active alarms.", configuredAlarms:"Configured alarms", noAlarmDefinitions:"No alarm definitions yet.", source:"Source", alarmText:"Alarm Text", priority:"Priority", state:"State", activeSince:"Active Since", sourceTag:"Source Tag", condition:"Condition", delay:"Delay", alarmHistory:"ALARM HISTORY", recentAlarmEvents:"Alarm occurrences", time:"Time", event:"Event", acknowledge:"ACK", alarmCame:"Active At", alarmCleared:"Cleared At", duration:"Duration", checking:"Checking...", agentOnline:"Connected", agentOffline:"Not running",
    addAlarm:"Add Alarm", editAlarm:"Edit Alarm", alarmSourceHelp:"BOOL and WORD Tags are available as digital alarm sources.", triggerWhen:"Trigger when", delayOn:"Delay ON (ms)", delayOff:"Delay OFF (ms)", windowsNotification:"Windows Notification", alarmInfo:"Alarm reads the existing Tag value. It never creates a second PLC/Modbus read.", saveAlarm:"Save Alarm", alarmSaved:"Alarm saved.", alarmUpdated:"Alarm updated.", alarmDeleted:"Alarm deleted.", alarmAcknowledged:"Alarm acknowledged.",
    trendsText:"Create saved live and historical trends from Tags, compare signals and work across useful time ranges.", addTrendPlus:"+ Add Trend", savedTrends:"SAVED TRENDS", liveTrend:"LIVE TREND", selectTrend:"Select or create a trend.", addTrend:"Add Trend", editTrend:"Edit Trend", trendName:"Trend name", defaultRange:"Default range", selectTags:"Select Tags (max 8)", trendInfo:"Select up to 8 Tags, choose a time range and inspect values with local trend data.", saveTrend:"Save Trend", trendSaved:"Trend saved.", trendUpdated:"Trend updated.", trendDeleted:"Trend deleted."
  },
  tr: {
    industrialMonitoring:"Endüstriyel İzleme", operations:"OPERASYON", overview:"Genel Bakış", devices:"Cihazlar", tags:"Taglar", alarms:"Alarmlar", trends:"Trendler", historian:"Geçmiş Veri", system:"SİSTEM", diagnostics:"Tanılama", notifications:"Bildirimler", license:"Lisans", settings:"Ayarlar",
    coreRunning:"Core Çalışıyor", localSystem:"YEREL SİSTEM", systemHealthy:"Sistem Sağlıklı", localByDesign:"YEREL TASARIM", knowBefore:"Durmadan önce bil.",
    heroText:"Endüstriyel cihazları bağlayın, canlı veriyi doğrulayın, alarmlar oluşturun ve proses verisini buluta göndermeden kayıt almaya başlayın.",
    addFirstDevice:"+ İlk cihazınızı ekleyin", monitoredTags:"İzlenen Taglar", tagPollingActive:"Canlı tag tarama motoru aktif", activeAlarms:"Aktif Alarmlar", alarmEngineNext:"Alarm değerlendirme motoru aktif", healthy:"Sağlıklı",
    quickStart:"HIZLI BAŞLANGIÇ", commissioningPath:"15 dakikalık devreye alma akışı", serviceFoundationRunning:"Servis altyapısı çalışıyor", devEntitlementActive:"Yerel lisans hakkı aktif",
    firstRunTitle:"PROGNODE’u yaklaşık 15 dakikada devreye alın", firstRunText:"Yerel erişimden cihaz, canlı Tag, alarm ve Historian kaydına uzanan yönlendirmeli devreye alma akışı.", firstRunDevice:"Cihaz bağla", firstRunTags:"Canlı Tag doğrula", firstRunAlarm:"Alarm oluştur", firstRunHistorian:"Historian'ı etkinleştir", firstRunStart:"Kuruluma devam et", firstRunLater:"Şimdi değil", firstRunAccessRequired:"Bu PROGNODE Server'ı yapılandırmak için giriş yapın.", firstRunAccessReady:"Yerel yapılandırma oturumu aktif.", firstRunSignIn:"Başlamak için giriş yap", setupAssistant:"Kurulum Asistanı", setupComplete:"Kurulum tamamlandı", setupProgress:"Kurulum ilerlemesi", commissioningAssistant:"DEVREYE ALMA ASİSTANI", localFirstFootnote:"PLC taraması, alarmlar ve Historian bu PROGNODE Server üzerinde kalır.", operationalStatus:"OPERASYON DURUMU", accessMode:"Erişim", connectivity:"Bağlantı", alarmPosture:"Alarm durumu", recordingState:"Yerel kayıt", viewOnly:"Salt görüntüleme", viewOnlyHelp:"Canlı izleme kullanılabilir. Yapılandırmayı değiştirmek için geçerli lisansı içe aktarın ve giriş yapın.", signedInLocal:"Giriş yapıldı", attentionRequired:"Dikkat gerekiyor", systemStable:"Sistem stabil",
    addDevice:"Cihaz ekle", modbusAvailable:"Modbus TCP kullanılabilir", addDeviceArrow:"Cihaz ekle →", verifyLiveTags:"Canlı tagları doğrula", addFirstTagHelp:"40001 ekleyin ve değeri canlı izleyin", addTagArrow:"Tag ekle →",
    createFirstAlarm:"İlk alarmınızı oluşturun", alarmEngineMilestone:"Alarm izleme", enableRecording:"Kayıt almayı etkinleştir", historianMilestone:"Historian kaydı",
    architecture:"MİMARİ", localSystemStatus:"Yerel sistem durumu", running:"Çalışıyor", available:"Kullanılabilir",
    fieldConnectivity:"SAHA HABERLEŞMESİ", devicesText:"PLC, sayaç, sürücü, RTU ve diğer endüstriyel veri kaynaklarını ekleyin.", addDevicePlus:"+ Cihaz Ekle", noDevicesYet:"Henüz cihaz yok", noDevicesText:"PLC, sayaç, sürücü, RTU veya desteklenen başka bir endüstriyel cihaz bağlayın.",
    device:"Cihaz", protocol:"Protokol", status:"Durum", host:"Host", poll:"Tarama", created:"Oluşturma", editDevice:"Cihaz Düzenle",
    tagsText:"Modbus registerlarından canlı Taglar oluşturun; değer, veri tipi, durum ve mühendislik gösterimini doğrulayın.", addTagPlus:"+ Tag Ekle", noTagsYet:"Henüz tag yok", noTagsText:"Bir Modbus Tagı ekleyin (ör. 40001 veya 30001); PROGNODE hemen taramaya başlayacaktır.",
    tag:"Tag", address:"Adres", datatype:"Veri Tipi", value:"Değer", dataStatus:"Durum", updated:"Güncelleme", edit:"Düzenle",
    alarmsPlaceholder:"Alarm tanımları mevcut Tagları referans alacak. BOOL ve integer WORD Taglar dijital alarm kaynağı seçilip alarm metni atanabilecek.", trendsPlaceholder:"Zaman aralığına göre optimize edilmiş hızlı yerel trendler.", historianPlaceholder:"Retention, disk sağlığı, CSV export ve kayıtlı tag ayarları.", diagnosticsPlaceholder:"Bağlantı gecikmesi, son tarama, retry sayısı, haberleşme hataları ve cihaz sağlığı.", modbusDiagnosticsSoon:"Gelişmiş Modbus tanılama sıradaki geliştirmedir.", nextMilestone:"Sıradaki kilometre taşı",
    notificationsText:"PROGNODE Agent üzerinden browser'dan bağımsız Windows masaüstü bildirimleri.", sendTestNotification:"Windows Test Bildirimi Gönder", agentStatus:"AGENT DURUMU",
    agentHelp:"Windows Agent projesini çalıştırın. Sistem tepsisinde kalır ve browser kapalı olsa bile yerel bildirim olaylarını alır.", coreNotificationApi:"Core Bildirim API", trayAgent:"Tray Agent", runSeparately:"Ayrı çalıştır", recentEvents:"SON OLAYLAR",
    entitlements:"LİSANS HAKLARI", licenseText:"İmzalı offline lisansınız etkin PROGNODE modüllerini belirler; runtime verisi yerelde kalır.", currentLicense:"MEVCUT LİSANS", plan:"Plan", alarmDefinitions:"Alarm Tanımları", recordedTags:"Kayıtlı Taglar", features:"ÖZELLİKLER", licensedProtocols:"Lisanslı Protokoller",
    settingsPlaceholder:"Kullanıcılar, backup/restore, network, update, güvenlik ve veri akışı ayarları burada olacak.",
    commissioning:"DEVREYE ALMA", configure:"Yapılandırma", chooseProtocolText:"PROGNODE'un endüstriyel cihazla nasıl haberleşeceğini seçin.", backProtocols:"← Protokollere dön", selectedProtocol:"Seçili protokol", deviceName:"PLC / Varlık adı", deviceNameHelp:"Global olarak anlaşılır bir ekipman adı kullanın. Örn: Kazan PLC, Ana Pompa PLC, Paketleme Hattı PLC.", mockInfo:"Protokole özel ayarlar yerel konfigürasyon veritabanına kaydedilir.",
    hostIp:"Host / IP", port:"Port", unitId:"Unit ID", pollInterval:"Tarama süresi (ms)", timeout:"Timeout (ms)", modbusTestInfo:"Ping ve Modbus testleri opsiyoneldir. Cihaz haberleşme yokken de kaydedilebilir.", cancel:"İptal", ping:"Ping", pingOk:"Ping Başarılı", pingFail:"Ping Başarısız", testConnection:"Modbus Test", saveDevice:"Cihazı Kaydet",
    addTag:"Tag Ekle", editTag:"Tag Düzenle", modbusDeviceOnly:"Yapılandırılmış Modbus TCP, Siemens S7 TCP, MQTT ve OPC UA cihazları kullanılabilir.", tagName:"Tag / Sinyal adı", unit:"Birim", offset:"Offset",
    decimalPoint:"Ondalık nokta", decimalPointHelp:"Integer örnek: raw 236 + 1 ondalık = 23.6", displayDecimals:"Gösterim basamağı", displayDecimalsHelp:"FLOAT32 / REAL değeri değişmez; yalnız noktadan sonra kaç basamak gösterileceğini belirler.",
    tagAddressHelp:"Modbus okuma alanları: 00001 Coil (FC01), 10001 Discrete Input (FC02), 30001 Input Register (FC04), 40001 Holding Register (FC03). Coil/Discrete doğrudan BOOL kullanır; Holding Register BOOL 0..15 bit kullanır. Input Register WORD/sayısal tip kullanır. 32-bit değerler iki register kullanır.",
    saveTag:"Tagı Kaydet", noDevicesConfigured:"Yapılandırılmış cihaz yok", configuredDevice:"yapılandırılmış cihaz", configuredDevices:"yapılandırılmış cihaz", delete:"Sil",
    deviceSaved:"Cihaz PROGNODE konfigürasyonuna kaydedildi.", deviceDeleted:"Cihaz silindi.", tagSaved:"Tag kaydedildi. Canlı tarama başladı.", tagUpdated:"Tag güncellendi.", tagDeleted:"Tag silindi.",
    testSuccess:"Modbus protokol testi başarılı", testFailed:"Modbus testi başarısız", testNotificationQueued:"Test bildirimi PROGNODE Agent için kuyruğa alındı.", noNotificationEvents:"Henüz bildirim olayı yok.",
    alarmRuntimeActive:"Alarm motoru ve sistem alarmları aktif", alarmEngineRunning:"Dijital + sayısal alarm motoru çalışıyor", addAlarmArrow:"Alarm ekle →",
    alarmsText:"Dijital veya sayısal alarmlar oluşturun, aktif alarmları ACK edin ve kalıcı alarm geçmişini görüntüleyin.", addAlarmPlus:"+ Alarm Ekle", live:"CANLI", noActiveAlarms:"Aktif alarm yok.", configuredAlarms:"Tanımlı alarmlar", noAlarmDefinitions:"Henüz alarm tanımı yok.", source:"Kaynak", alarmText:"Alarm Metni", priority:"Öncelik", state:"Durum", activeSince:"Aktif Zaman", sourceTag:"Kaynak Tag", condition:"Koşul", delay:"Gecikme", alarmHistory:"ALARM GEÇMİŞİ", recentAlarmEvents:"Alarm kayıtları", time:"Zaman", event:"Olay", acknowledge:"ACK", alarmCame:"Geldiği Zaman", alarmCleared:"Gittiği Zaman", duration:"Süre", checking:"Kontrol ediliyor...", agentOnline:"Bağlı", agentOffline:"Çalışmıyor",
    addAlarm:"Alarm Ekle", editAlarm:"Alarm Düzenle", alarmSourceHelp:"BOOL ve WORD Taglar dijital alarm kaynağı olarak kullanılabilir.", triggerWhen:"Şu durumda tetikle", delayOn:"Aktif Gecikme (ms)", delayOff:"Pasif Gecikme (ms)", windowsNotification:"Windows Bildirimi", alarmInfo:"Alarm mevcut Tag değerini kullanır. PLC/Modbus tarafında ikinci bir okuma oluşturmaz.", saveAlarm:"Alarmı Kaydet", alarmSaved:"Alarm kaydedildi.", alarmUpdated:"Alarm güncellendi.", alarmDeleted:"Alarm silindi.", alarmAcknowledged:"Alarm ACK edildi.",
    trendsText:"Taglardan canlı ve geçmiş trendler oluşturun, sinyalleri karşılaştırın ve uygun zaman aralıklarında inceleyin.", addTrendPlus:"+ Trend Ekle", savedTrends:"KAYITLI TRENDLER", liveTrend:"CANLI TREND", selectTrend:"Bir trend seçin veya oluşturun.", addTrend:"Trend Ekle", editTrend:"Trend Düzenle", trendName:"Trend adı", defaultRange:"Varsayılan aralık", selectTags:"Tagları seçin (maks. 8)", trendInfo:"En fazla 8 Tag seçin, zaman aralığını belirleyin ve yerel trend verilerini inceleyin.", saveTrend:"Trendi Kaydet", trendSaved:"Trend kaydedildi.", trendUpdated:"Trend güncellendi.", trendDeleted:"Trend silindi."
  }
};

Object.assign(translations.en, {acknowledgedAt:"Acknowledged At",acknowledgedBy:"Acknowledged By"});
Object.assign(translations.tr, {acknowledgedAt:"Onay Zamanı",acknowledgedBy:"Onaylayan"});

Object.assign(translations.en, {
  signIn:"Sign in",
  signedIn:"Signed in",
  openLicensePage:"Open License Page",
  accountSaved:"Account session saved.",
  accountSignedOut:"Signed out.",
  accountRequired:"Enter full name and email.",
  alarmsText:"Create digital and numeric alarms with device-aware sources, priority, acknowledgement and persistent history.",
  sourceTag:"Source Tag",
  notify:"Notify",
  notifyOnActive:"Notify when ACTIVE",
  notifyOnActiveHelp:"Recommended default",
  notificationMode:"Mobile notification mode", notifyOnce:"Notify once", repeatUntilAck:"Repeat at interval while active", repeatEvery:"Repeat every",
  ackPolicyHelp:"ACK confirms the alarm was seen; it does not clear the process alarm. Interval reminders continue while the alarm remains active, even after ACK.",
  requiresAcknowledgement:"Require operator ACK", requiresAcknowledgementHelp:"Show an ACK action in Core and mobile for this alarm.", ackRequiredShort:"ACK required", ackNotRequiredShort:"No ACK", repeatActiveShort:"Repeat",
  continueAfterClear:"Continue reminders after CLEAR until ACK", continueAfterClearHelp:"Useful for short critical events that must still be seen.",
  notifyOnCleared:"Notify when CLEARED",
  notifyOnClearedHelp:"Optional recovery notification",
  alarmSourceHelp:"BOOL / WORD digital alarms and UInt16 / Int16 / UInt32 / Int32 / Float32 numeric alarms are supported.",
  trendsText:"Persistent SQLite Historian trends with configurable sampling, retention, colors and date-range navigation.",
  historicalTrend:"HISTORICAL TREND",
  sampleEvery:"Sample every",
  retention:"Retention",
  selectTagsColors:"Select Tags + colors (max 8)",
  trendInfo:"Selected Tags are persisted to the local SQLite Historian at the chosen sampling interval.",
  from:"From",
  to:"To",
  apply:"Apply",
  historianText:"Persistent local SQLite/WAL storage. Process history stays on the customer PC.",
  totalSamples:"Total Samples",
  persistentLocalStorage:"Persistent local storage",
  trendProfilesDriveRecording:"Trend profiles drive recording",
  databaseSize:"Database Size",
  timeSpan:"Time Span",
  recordingProfiles:"RECORDING PROFILES",
  historianConfiguration:"Historian configuration",
  trend:"Trend",
  sampleEvery:"Sample Every",
  retention:"Retention",
  tags:"Tags",
  exportReady:"Export ready"
});

Object.assign(translations.tr, {
  signIn:"Giriş yap",
  signedIn:"Giriş yapıldı",
  openLicensePage:"Lisans Sayfasını Aç",
  accountSaved:"Hesap oturumu kaydedildi.",
  accountSignedOut:"Çıkış yapıldı.",
  accountRequired:"Ad soyad ve e-posta girin.",
  alarmsText:"Dijital ve sayısal alarmları cihaz bilgisi, öncelik, ACK ve kalıcı geçmiş ile yönetin.",
  sourceTag:"Kaynak Tag",
  notify:"Bildirim",
  notifyOnActive:"ACTIVE olduğunda bildir",
  notifyOnActiveHelp:"Önerilen varsayılan",
  notificationMode:"Mobil bildirim modu", notifyOnce:"Bir kez bildir", repeatUntilAck:"Aktif kaldıkça aralıkla bildir", repeatEvery:"Tekrar aralığı",
  ackPolicyHelp:"ACK alarmın görüldüğünü doğrular, proses alarmını temizlemez. Alarm aktif kaldığı sürece seçilen aralıkta, ACK sonrasında da bildirim sürer.",
  requiresAcknowledgement:"Operatör ACK'i zorunlu", requiresAcknowledgementHelp:"Core ve mobil uygulamada bu alarm için ACK eylemini göster.", ackRequiredShort:"ACK gerekli", ackNotRequiredShort:"ACK yok", repeatActiveShort:"Tekrar",
  continueAfterClear:"CLEAR sonrası ACK gelene kadar hatırlat", continueAfterClearHelp:"Görülmesi gereken kısa süreli kritik olaylar için kullanılır.",
  notifyOnCleared:"CLEARED olduğunda bildir",
  notifyOnClearedHelp:"İsteğe bağlı düzelme bildirimi",
  alarmSourceHelp:"BOOL / WORD dijital alarmlar ile UInt16 / Int16 / UInt32 / Int32 / Float32 sayısal alarmlar desteklenir.",
  trendsText:"Ayarlanabilir kayıt süresi, retention, renk ve tarih aralığı ile kalıcı SQLite Historian trendleri.",
  historicalTrend:"GEÇMİŞ TREND",
  sampleEvery:"Kayıt aralığı",
  retention:"Saklama süresi",
  selectTagsColors:"Tag + renk seçin (maks. 8)",
  trendInfo:"Seçilen Taglar belirlenen aralıkta yerel SQLite Historian'a kalıcı kaydedilir.",
  from:"Başlangıç",
  to:"Bitiş",
  apply:"Uygula",
  historianText:"Kalıcı yerel SQLite/WAL kayıt. Proses geçmişi müşteri PC'sinde kalır.",
  totalSamples:"Toplam Kayıt",
  persistentLocalStorage:"Kalıcı yerel kayıt",
  trendProfilesDriveRecording:"Kayıt profillerini Trendler belirler",
  databaseSize:"Veritabanı Boyutu",
  timeSpan:"Zaman Aralığı",
  recordingProfiles:"KAYIT PROFİLLERİ",
  historianConfiguration:"Historian konfigürasyonu",
  trend:"Trend",
  sampleEvery:"Kayıt Aralığı",
  retention:"Saklama",
  tags:"Taglar",
  exportReady:"Export hazır"
});


Object.assign(translations.en, {
  signIn:"Sign in",
  signedIn:"Signed in",
  openLicensePage:"Open License Page",
  accountSaved:"Account session saved.",
  accountSignedOut:"Signed out.",
  accountRequired:"Enter full name and email.",
  historianAddTag:"+ Add Tag to Historian", historianRecordedHelp:"Tags explicitly added to Historian",
  historianTags:"HISTORIAN TAGS", recordingSince:"Recording Since", lastSample:"Last Sample", points:"Points",
  historianEmpty:"No Tags are being recorded. Add a Tag to Historian to start.", historianTagHelp:"Adding a Tag here starts persistent local recording.",
  historianModalInfo:"Historian continues recording even if a Trend is deleted or the browser is closed.", save:"Save",
  stopRecording:"Stop", keepHistory:"Stop recording and keep history? Cancel = delete history too.", openTrend:"Open Trend",
  historianText:"Add Tags here to start persistent local recording. Trend definitions no longer control recording.", historianArrow:"Open Historian →"
});
Object.assign(translations.tr, {
  signIn:"Giriş yap",
  signedIn:"Giriş yapıldı",
  openLicensePage:"Lisans Sayfasını Aç",
  accountSaved:"Hesap oturumu kaydedildi.",
  accountSignedOut:"Çıkış yapıldı.",
  accountRequired:"Ad soyad ve e-posta girin.",
  historianAddTag:"+ Historian'a Tag Ekle", historianRecordedHelp:"Historian'a açıkça eklenen Taglar",
  historianTags:"HISTORIAN TAGLARI", recordingSince:"Kayıt Başlangıcı", lastSample:"Son Kayıt", points:"Nokta",
  historianEmpty:"Kayıt edilen Tag yok. Başlatmak için Historian'a Tag ekleyin.", historianTagHelp:"Tagı buraya eklemek kalıcı yerel kaydı başlatır.",
  historianModalInfo:"Trend silinse veya browser kapansa bile Historian kayıt almaya devam eder.", save:"Kaydet",
  stopRecording:"Durdur", keepHistory:"Kaydı durdurup eski veriyi korumak için Tamam. Veriyi de silmek için İptal.", openTrend:"Trend Aç",
  historianText:"Kalıcı kaydı başlatmak için Tagları buraya ekleyin. Trend artık kayıt davranışını kontrol etmez.", historianArrow:"Historian Aç →"
});

const $ = id => document.getElementById(id);
// DEV3 UI vocabulary is kept here so the original translation table stays readable.
Object.assign(translations.en, {
  signIn:"Sign in",
  signedIn:"Signed in",
  openLicensePage:"Open License Page",
  accountSaved:"Account session saved.",
  accountSignedOut:"Signed out.",
  accountRequired:"Enter full name and email.",
  configure:"Configuration", dataExchange:"Import / Export", dataExchangeText:"Bulk commissioning with CSV templates, validation preview and partial import.",
  importCsv:"Import CSV", downloadTemplate:"Download template", previewDryRun:"Preview / dry-run", dataset:"Dataset",
  chooseCsv:"Choose CSV file", csvExcelCompatible:"CSV / Excel compatible", validRows:"valid", errorRows:"errors", totalRows:"total",
  importValidRows:"Import valid rows", preview:"Preview", details:"Details", importEmpty:"Select a dataset and CSV file to validate before import.",
  exports:"EXPORTS", operationalData:"Operational data", exportDevicesHelp:"Device configuration and communication settings",
  exportTagsHelp:"Addresses, datatypes, engineering display and byte order", exportAlarmsHelp:"Alarm rules and notification settings",
  exportAlarmHistoryHelp:"Occurrence history with active and cleared timestamps", exportHistorianHelp:"Use a Trend time range to export historian data",
  templates:"TEMPLATES", commissioningTemplates:"Commissioning templates",
  templateHelp:"Download a clean CSV template, fill it in Excel and return here for validation. Invalid rows do not block valid rows.",
  operations:"OPERATIONS", quickActions:"Quick actions", commissioningComplete:"Commissioning complete", recordingTags:"Recording Tags",
  historianPoints:"Historian Points", recordingHealth:"Recording health", localStorage:"Local storage", notificationCenter:"NOTIFICATION CENTER",
  notificationCenterStored:"Events available in PROGNODE", critical:"Critical", priorityEvents:"Critical / high priority events",
  modbusNowAvailable:"Modbus TCP is available", runtimeSignalNext:"Tag runtime engine is active",
  systemOverview:"System Overview", operational:"OPERATIONAL", openAlarms:"Open Alarms", noActiveAlarmsNow:"No active alarms",
  localRecording:"Persistent local samples", importReady:"Ready to validate CSV", importedRows:"rows imported", checkingAgent:"Checking Windows Agent",
  actionCenter:"Windows Action Center", actionCenterHelp:"Native Windows notifications are registered under the PROGNODE identity.",
  partialImport:"Valid rows are imported independently; invalid rows remain listed for correction."
});

Object.assign(translations.tr, {
  signIn:"Giriş yap",
  signedIn:"Giriş yapıldı",
  openLicensePage:"Lisans Sayfasını Aç",
  accountSaved:"Hesap oturumu kaydedildi.",
  accountSignedOut:"Çıkış yapıldı.",
  accountRequired:"Ad soyad ve e-posta girin.",
  configure:"Yapılandırma", dataExchange:"İçe / Dışa Aktar", dataExchangeText:"CSV şablonları, doğrulama önizlemesi ve kısmi aktarım ile toplu devreye alma.",
  importCsv:"CSV İçe Aktar", downloadTemplate:"Şablon indir", previewDryRun:"Önizleme / dry-run", dataset:"Veri seti",
  chooseCsv:"CSV dosyası seç", csvExcelCompatible:"CSV / Excel uyumlu", validRows:"geçerli", errorRows:"hata", totalRows:"toplam",
  importValidRows:"Geçerli satırları aktar", preview:"Önizleme", details:"Detay", importEmpty:"İçe aktarmadan önce doğrulamak için veri seti ve CSV dosyası seçin.",
  exports:"DIŞA AKTAR", operationalData:"Operasyon verileri", exportDevicesHelp:"Cihaz konfigürasyonu ve haberleşme ayarları",
  exportTagsHelp:"Adresler, veri tipleri, mühendislik gösterimi ve byte order", exportAlarmsHelp:"Alarm kuralları ve bildirim ayarları",
  exportAlarmHistoryHelp:"Aktif ve temizlenme zamanlarıyla alarm occurrence geçmişi", exportHistorianHelp:"Historian verisini dışa aktarmak için Trend zaman aralığını kullanın",
  templates:"ŞABLONLAR", commissioningTemplates:"Devreye alma şablonları",
  templateHelp:"Temiz CSV şablonunu indirin, Excel'de doldurun ve doğrulama için buraya geri yükleyin. Hatalı satırlar geçerli satırları engellemez.",
  operations:"OPERASYON", quickActions:"Hızlı işlemler", commissioningComplete:"Devreye alma tamamlandı", recordingTags:"Kaydedilen Taglar",
  historianPoints:"Historian Noktaları", recordingHealth:"Kayıt sağlığı", localStorage:"Yerel depolama", notificationCenter:"BİLDİRİM MERKEZİ",
  notificationCenterStored:"PROGNODE içindeki son olaylar", critical:"Kritik", priorityEvents:"Kritik / yüksek öncelikli olaylar",
  modbusNowAvailable:"Modbus TCP kullanılabilir", runtimeSignalNext:"Tag runtime motoru aktif",
  systemOverview:"Sistem Genel Bakışı", operational:"OPERASYONEL", openAlarms:"Alarmları Aç", noActiveAlarmsNow:"Aktif alarm yok",
  localRecording:"Kalıcı yerel örnekler", importReady:"CSV doğrulamasına hazır", importedRows:"satır aktarıldı", checkingAgent:"Windows Agent kontrol ediliyor",
  actionCenter:"Windows Bildirim Merkezi", actionCenterHelp:"Native Windows bildirimleri PROGNODE kimliğiyle kaydedilir.",
  partialImport:"Geçerli satırlar bağımsız aktarılır; hatalı satırlar düzeltmek için listede kalır."
});


// HF5 Settings workspace: all existing IDs and APIs remain unchanged.
Object.assign(translations.en, {hf5General:'Server & QR',hf5Lan:'Mobile LAN',hf5Remote:'Remote access',hf5Backup:'Backup & audit',hf5SettingsHelp:'Server, secure mobile access, remote clients and project safety — each in one place.'});
Object.assign(translations.tr, {hf5General:'Sunucu & QR',hf5Lan:'Mobil LAN',hf5Remote:'Uzak erişim',hf5Backup:'Yedek & kayıtlar',hf5SettingsHelp:'Sunucu, güvenli mobil erişim, uzak istemciler ve proje güvenliği — ayrı bölümlerde.'});

// V0.7.1 — B1 brand system + professional operational dashboard
Object.assign(translations.en, {
  signIn:"Sign in",
  signedIn:"Signed in",
  openLicensePage:"Open License Page",
  accountSaved:"Account session saved.",
  accountSignedOut:"Signed out.",
  accountRequired:"Enter full name and email.",
  plantPerformance:"Plant performance at a glance", monitor:"MONITOR", monitorHelp:"Live data from your assets",
  detect:"DETECT", detectHelp:"Alarms and anomalies", analyze:"ANALYZE", analyzeHelp:"Trends and historical data",
  keepRunning:"KEEP RUNNING", keepRunningHelp:"Higher uptime, lower risk", builtCloser:"PROCESS DATA STAYS ON-SITE.", processDataOnSite:"Process data stays on-site", noCloudRequired:"No cloud required", localFirst:"LOCAL-FIRST", sidebarDescriptor:"Industrial monitoring, without the cloud.",
  liveTags:"Live Tags", coreHealth:"Core Health", deviceHealth:"DEVICE HEALTH", viewDevices:"View Devices →",
  online:"Online", degraded:"Degraded", offline:"Offline", maintenance:"Maintenance", viewAlarms:"View Alarms →",
  recentNotifications:"RECENT NOTIFICATIONS", viewNotifications:"View All →", historianRecordingStatus:"HISTORIAN / RECORDING STATUS",
  openHistorian:"Open Historian →", quality:"Quality", pointsByTag:"Points by recording tag",
  criticalEquipment:"See sooner. Act faster. Run longer.", resilientTomorrow:"Local industrial monitoring for alarms, trends and history — without sending process data to the cloud.",
  commissioningProgress:"Commissioning in progress", operationalOverview:"Operational overview",
  prognodeIndustrialMonitoring:"PROGNODE INDUSTRIAL MONITORING", totalDevices:"Total Devices", historianPointsToday:"Historian Points",
  viewAllDevices:"View All Devices →", viewAllAlarms:"View All Alarms →", viewAllNotifications:"View All Notifications →",
  addToHistorian:"Add to Historian", createTrend:"Create Trend",
  subscription:"SUBSCRIPTION", licenseCustomerHelp:"Your PROGNODE license is verified locally. Process data remains on this Server.", buyRenewLicense:"Buy / Renew License ↗", validUntil:"VALID UNTIL", licensedTo:"Licensed to", organization:"Organization", alarmMonitoring:"Alarm Monitoring", moduleIncluded:"Included", offlineLogin:"Offline Login", lanClientAccess:"LAN Client Access", enabled:"Enabled", licenseActions:"LICENSE ACTIONS", manageLicense:"Manage your license", manageLicenseHelp:"Renew or purchase a subscription online, then import the refreshed .pgnlicense file here. Offline runtime continues without a cloud dependency.", openPrognodeStore:"Open PROGNODE ↗", orImportLicense:"OR IMPORT A LICENSE", licenseLocalNote:"Signature, expiry and account credentials are verified locally."
});
Object.assign(translations.tr, {
  signIn:"Giriş yap",
  signedIn:"Giriş yapıldı",
  openLicensePage:"Lisans Sayfasını Aç",
  accountSaved:"Hesap oturumu kaydedildi.",
  accountSignedOut:"Çıkış yapıldı.",
  accountRequired:"Ad soyad ve e-posta girin.",
  plantPerformance:"Tesis performansına genel bakış", monitor:"İZLE", monitorHelp:"Saha varlıklarından canlı veri",
  detect:"TESPİT ET", detectHelp:"Alarmlar ve anormallikler", analyze:"ANALİZ ET", analyzeHelp:"Trend ve geçmiş veriler",
  keepRunning:"ÇALIŞIR TUT", keepRunningHelp:"Daha yüksek süreklilik, daha düşük risk", builtCloser:"PROSES VERİSİ SAHADA KALIR.", processDataOnSite:"Proses verisi sahada kalır", noCloudRequired:"Bulut gerekmez", localFirst:"LOCAL-FIRST", sidebarDescriptor:"Bulutsuz endüstriyel izleme.",
  liveTags:"Canlı Taglar", coreHealth:"Core Sağlığı", deviceHealth:"CİHAZ SAĞLIĞI", viewDevices:"Cihazları Aç →",
  online:"Online", degraded:"Zayıf", offline:"Offline", maintenance:"Bakım", viewAlarms:"Alarmları Aç →",
  recentNotifications:"SON BİLDİRİMLER", viewNotifications:"Tümünü Aç →", historianRecordingStatus:"HISTORIAN / KAYIT DURUMU",
  openHistorian:"Historian Aç →", quality:"Kalite", pointsByTag:"Kaydedilen Tag başına nokta",
  criticalEquipment:"Daha erken görün. Daha hızlı aksiyon alın. Daha uzun çalıştırın.", resilientTomorrow:"Alarm, trend ve geçmiş veri için local endüstriyel izleme — proses verisini buluta göndermeden.",
  commissioningProgress:"Devreye alma devam ediyor", operationalOverview:"Operasyonel genel bakış",
  prognodeIndustrialMonitoring:"PROGNODE ENDÜSTRİYEL İZLEME", totalDevices:"Toplam Cihaz", historianPointsToday:"Historian Noktaları",
  viewAllDevices:"Tüm Cihazlar →", viewAllAlarms:"Tüm Alarmlar →", viewAllNotifications:"Tüm Bildirimler →",
  addToHistorian:"Historian'a Ekle", createTrend:"Trend Oluştur",
  subscription:"ABONELİK", licenseCustomerHelp:"PROGNODE lisansınız yerel olarak doğrulanır. Proses verisi bu Server üzerinde kalır.", buyRenewLicense:"Satın Al / Yenile ↗", validUntil:"GEÇERLİLİK TARİHİ", licensedTo:"Lisans kullanıcısı", organization:"Organizasyon", alarmMonitoring:"Alarm İzleme", moduleIncluded:"Dahil", offlineLogin:"Offline Giriş", lanClientAccess:"LAN İstemci Erişimi", enabled:"Etkin", licenseActions:"LİSANS İŞLEMLERİ", manageLicense:"Lisansınızı yönetin", manageLicenseHelp:"Aboneliğinizi online satın alın veya yenileyin, ardından güncel .pgnlicense dosyasını buraya aktarın. Offline runtime buluta bağlı olmadan çalışmaya devam eder.", openPrognodeStore:"PROGNODE'u Aç ↗", orImportLicense:"VEYA LİSANS İÇE AKTAR", licenseLocalNote:"İmza, süre ve hesap bilgileri yerel olarak doğrulanır."
});
Object.assign(translations.en, {
  localAccess:"LOCAL ACCESS", prognodeAccess:"PROGNODE Access", accessIntro:"Import the offline license once, then sign in with the PROGNODE account linked to that license.",
  offlineLicense:"OFFLINE LICENSE", importLicense:"Import License", remaining:"Remaining", signOut:"Sign out", signInConfig:"Sign in to edit configuration", signInConfigHelp:"Use the email and password linked to this PROGNODE license.", email:"Email", password:"Password", close:"Close",
  offlineActivation:"OFFLINE ACTIVATION", importPrognodeLicense:"Import PROGNODE license", offlineActivationHelp:"Load the .pgnlicense issued by your PROGNODE Account Portal. Account identity and entitlements are applied automatically.",
  assignedUser:"Assigned User", assignedEmail:"Assigned Email", portalRole:"Portal Role", site:"Site", lanAccess:"LAN Access", cloudPush:"Cloud Push", actions:"Actions"
});
Object.assign(translations.tr, {
  localAccess:"YEREL ERİŞİM", prognodeAccess:"PROGNODE Erişimi", accessIntro:"Offline lisansı bir kez içe aktarın, ardından bu lisansa bağlı PROGNODE hesabıyla giriş yapın.",
  offlineLicense:"OFFLINE LİSANS", importLicense:"Lisansı İçe Aktar", remaining:"Kalan Süre", signOut:"Çıkış yap", signInConfig:"Yapılandırmayı düzenlemek için giriş yapın", signInConfigHelp:"Bu PROGNODE lisansına bağlı e-posta ve şifreyi kullanın.", email:"E-posta", password:"Şifre", close:"Kapat",
  offlineActivation:"OFFLINE AKTİVASYON", importPrognodeLicense:"PROGNODE lisansını içe aktar", offlineActivationHelp:"PROGNODE Account Portal tarafından verilen .pgnlicense dosyasını yükleyin. Hesap kimliği ve lisans hakları otomatik uygulanır.",
  assignedUser:"Atanmış Kullanıcı", assignedEmail:"Atanmış E-posta", portalRole:"Portal Rolü", site:"Saha", lanAccess:"LAN Erişimi", cloudPush:"Cloud Push", actions:"İşlemler"
});

Object.assign(translations.en, {
  importLabel:"IMPORT", exportLabel:"EXPORT", templateLabel:"TEMPLATE", historyLabel:"HISTORY", chooseCsvArrow:"Choose CSV →", downloadArrow:"Download ↓", templateArrow:"Template ↓",
  devicesCsv:"Devices CSV", devicesImportHelp:"Bulk device commissioning with validation preview.", devicesTemplateHelp:"Excel-friendly clean commissioning template.",
  tagsCsv:"Tags CSV", tagsImportHelp:"Addresses, datatypes, scaling and byte order.", tagsExportHelp:"Download the current engineered Tag definition set.", tagsTemplateHelp:"Excel-friendly bulk Tag engineering template.",
  alarmsImportHelp:"Bulk alarm rules, priorities, delays and notification flags.", alarmsExportHelp:"Download all configured alarm rules.", alarmHistoryExportHelp:"Active / cleared occurrence history with durations.", alarmsTemplateHelp:"Excel-ready alarm commissioning template.",
  recordingConfig:"Recording Config", historianImportHelp:"Bulk Tag / sample interval / retention configuration.", historianExportConfigHelp:"Download current Historian recording configuration.", historianTemplateHelp:"Device, Tag, sample interval and retention template.",
  all:"All", criticalHigh:"Critical / High", systemCommunication:"System / Communication", custom:"Custom", autoFit:"Auto fit"
});
Object.assign(translations.tr, {
  importLabel:"İÇE AKTAR", exportLabel:"DIŞA AKTAR", templateLabel:"ŞABLON", historyLabel:"GEÇMİŞ", chooseCsvArrow:"CSV seç →", downloadArrow:"İndir ↓", templateArrow:"Şablon ↓",
  devicesCsv:"Cihazlar CSV", devicesImportHelp:"Doğrulama önizlemesiyle toplu cihaz devreye alma.", devicesTemplateHelp:"Excel uyumlu temiz devreye alma şablonu.",
  tagsCsv:"Taglar CSV", tagsImportHelp:"Adresler, veri tipleri, scaling ve byte order.", tagsExportHelp:"Mevcut mühendislik Tag tanımlarını indirin.", tagsTemplateHelp:"Excel uyumlu toplu Tag mühendislik şablonu.",
  alarmsImportHelp:"Alarm kurallarını, öncelikleri, gecikmeleri ve bildirim seçeneklerini toplu aktarın.", alarmsExportHelp:"Tanımlı tüm alarm kurallarını indirin.", alarmHistoryExportHelp:"Aktif / temizlenmiş alarm geçmişini süreleriyle indirin.", alarmsTemplateHelp:"Excel uyumlu alarm devreye alma şablonu.",
  recordingConfig:"Kayıt Konfigürasyonu", historianImportHelp:"Tag, örnekleme süresi ve retention ayarlarını toplu aktarın.", historianExportConfigHelp:"Mevcut Historian kayıt konfigürasyonunu indirin.", historianTemplateHelp:"Cihaz, Tag, örnekleme süresi ve retention şablonu.",
  all:"Tümü", criticalHigh:"Kritik / Yüksek", systemCommunication:"Sistem / Haberleşme", custom:"Özel", autoFit:"Otomatik sığdır"
});

Object.assign(translations.en, {
  tagRuntime:"TAG RUNTIME", alarmEngine:"ALARM ENGINE", historicalVisualization:"HISTORICAL VISUALIZATION", localHistorian:"LOCAL HISTORIAN", windowsAgent:"WINDOWS AGENT",
  settingsActualHelp:"Server identity, LAN discovery and one-time client pairing.", clientAccess:"CLIENT ACCESS", lanAutoDiscovery:"LAN AUTO-DISCOVERY", clientDiscoveryHelp:"Supported PROGNODE clients can discover this Server automatically and remember its stable Server ID instead of a changing IP address.",
  serverName:"Server Name", serverId:"Server ID", clientApi:"Client API", discovery:"Discovery", pairNewClient:"PAIR NEW CLIENT", pairingHelp:"On a supported PROGNODE client, choose this discovered Server and enter the pairing code once. No IP address is required.",
  dataBoundary:"DATA BOUNDARY", localFirstRuntime:"Local-first runtime", processConfiguration:"Process configuration", processData:"Process data", lanClientApi:"LAN client API", cloudDependency:"Cloud dependency", serverOnly:"Server only", localPairedAccess:"Local / paired access", notRequired:"Not required", identityLicenseOnly:"Identity + license only"
});
Object.assign(translations.tr, {
  tagRuntime:"TAG RUNTIME", alarmEngine:"ALARM MOTORU", historicalVisualization:"GEÇMİŞ VERİ GÖRSELLEŞTİRME", localHistorian:"YEREL HISTORIAN", windowsAgent:"WINDOWS AGENT",
  settingsActualHelp:"Server kimliği, LAN keşfi ve tek seferlik istemci eşleştirme.", clientAccess:"İSTEMCİ ERİŞİMİ", lanAutoDiscovery:"LAN OTOMATİK KEŞİF", clientDiscoveryHelp:"Desteklenen PROGNODE istemcileri bu Server'ı otomatik bulabilir ve değişen IP adresi yerine sabit Server ID'yi hatırlar.",
  serverName:"Server Adı", serverId:"Server ID", clientApi:"İstemci API", discovery:"Keşif", pairNewClient:"YENİ İSTEMCİ EŞLEŞTİR", pairingHelp:"Desteklenen bir PROGNODE istemcisinde bulunan Server'ı seçin ve eşleştirme kodunu bir kez girin. IP adresi gerekmez.",
  dataBoundary:"VERİ SINIRI", localFirstRuntime:"Local-first runtime", processConfiguration:"Proses konfigürasyonu", processData:"Proses verisi", lanClientApi:"LAN istemci API", cloudDependency:"Bulut bağımlılığı", serverOnly:"Yalnız Server", localPairedAccess:"Yerel / eşleştirilmiş erişim", notRequired:"Gerekmez", identityLicenseOnly:"Yalnız kimlik + lisans"
});

Object.assign(translations.en, {
  remoteAccess:"REMOTE ACCESS", remoteAccessTitle:"Remote clients", remoteAccessHelp:"LAN clients never consume a remote seat. Phones, tablets and Windows Clients consume one seat only when Remote Access is enabled.",
  subscription:"Subscription", serverBinding:"Server binding", remoteClients:"Remote clients", lastSync:"Last sync", bindRemoteAccess:"Bind Remote Access", syncNow:"Sync now", remoteAccessLocalNote:"Local runtime and LAN access never depend on this cloud connection.",
  remoteNotPurchased:"Not included", remoteLanOnly:"LAN only", remoteCloudPending:"Cloud pending", remoteSeat:"remote seat", remoteSeats:"remote seats", revokeRemote:"Revoke Remote", noRemoteClients:"No Remote Access clients are consuming seats on this Server."
});
Object.assign(translations.tr, {
  remoteAccess:"REMOTE ACCESS", remoteAccessTitle:"Uzak istemciler", remoteAccessHelp:"LAN istemcileri remote seat tüketmez. Telefon, tablet ve Windows Client yalnız Remote Access etkinleştirildiğinde bir seat kullanır.",
  subscription:"Abonelik", serverBinding:"Server bağlantısı", remoteClients:"Remote istemciler", lastSync:"Son senkron", bindRemoteAccess:"Remote Access Bağla", syncNow:"Şimdi senkronla", remoteAccessLocalNote:"Yerel runtime ve LAN erişimi bu bulut bağlantısına hiçbir zaman bağlı değildir.",
  remoteNotPurchased:"Dahil değil", remoteLanOnly:"Yalnız LAN", remoteCloudPending:"Cloud bekleniyor", remoteSeat:"remote seat", remoteSeats:"remote seat", revokeRemote:"Remote'u Kaldır", noRemoteClients:"Bu Server üzerinde seat tüketen Remote Access istemcisi yok."
});


Object.assign(translations.en,{
  backupKicker:"PROJECT SAFETY",backupTitle:"Backup Center",backupIntro:"Encrypted factory snapshot: devices, Tags, alarms, Historian, system records and saved Trend Studio layout.",
  backupPassword:"Backup passphrase (at least 12 characters)",backupPasswordConfirm:"Confirm passphrase",backupCreate:"Create encrypted snapshot",backupRefresh:"Refresh",backupSaved:"Available encrypted snapshots",
  backupRestoreTitle:"Verify and prepare restoration",backupRestoreHelp:"Upload an existing .pgnbackup. This only checks and stores the encrypted file; restoring data requires stopping Core and running RESTORE_PROGNODE_BACKUP.cmd.",backupImportPassword:"Backup passphrase",backupInspect:"Verify uploaded backup",backupAuditTitle:"Recent configuration changes"
});
Object.assign(translations.tr,{
  backupKicker:"PROJE GÜVENLİĞİ",backupTitle:"Yedekleme Merkezi",backupIntro:"Cihaz, Tag, alarm, Historian, sistem kayıtları ve kaydedilmiş Trend Studio düzenini şifreli yedekle.",
  backupPassword:"Yedek parolası (en az 12 karakter)",backupPasswordConfirm:"Parolayı doğrula",backupCreate:"Şifreli yedek oluştur",backupRefresh:"Yenile",backupSaved:"Kayıtlı şifreli yedekler",
  backupRestoreTitle:"Geri yüklemeyi doğrula ve hazırla",backupRestoreHelp:"Mevcut .pgnbackup yükle. Yalnızca doğrulanıp saklanır; geri yükleme için Core durdurulup RESTORE_PROGNODE_BACKUP.cmd çalıştırılmalıdır.",backupImportPassword:"Yedek parolası",backupInspect:"Yüklenen yedeği doğrula",backupAuditTitle:"Son yapılandırma değişiklikleri"
});

const t = key => translations[state.language][key] || translations.en[key] || key;

function escapeHtml(value) {
  return String(value ?? "")
    .replaceAll("&","&amp;").replaceAll("<","&lt;").replaceAll(">","&gt;")
    .replaceAll('"',"&quot;").replaceAll("'","&#039;");
}

async function api(url, options = {}) {
  const sessionToken = sessionStorage.getItem("prognode.accessSession") || "";
  const headers = {
    "Content-Type":"application/json",
    ...(sessionToken ? {"X-PROGNODE-Session":sessionToken} : {}),
    ...(options.headers || {})
  };
  const response = await fetch(url, { ...options, headers });

  if (response.status === 204) return null;

  const body = await response.json().catch(() => null);

  if (!response.ok) {
    const validationMessage = body?.errors
      ? Object.values(body.errors).flat().join(" ")
      : null;

    throw new Error(
      body?.message ||
      validationMessage ||
      body?.detail ||
      body?.title ||
      `Request failed (${response.status})`);
  }

  return body;
}

function showToast(message) {
  const toast = $("toast");
  toast.textContent = message;
  toast.classList.remove("hidden");
  clearTimeout(showToast.timer);
  showToast.timer = setTimeout(() => toast.classList.add("hidden"), 3200);
}

const QUICK_START_SNOOZED_SESSION_KEY = "prognode.quickStart.snoozed.session.rc6.2";
const QUICK_START_HIDDEN_KEY = "prognode.quickStart.hidden.v1";

function commissioningState() {
  const steps = [
    { key:"device", done: state.devices.length > 0 },
    { key:"tag", done: state.tags.length > 0 },
    { key:"alarm", done: state.alarmDefinitions.length > 0 },
    { key:"historian", done: state.historianConfigurations.length > 0 }
  ];
  const completed = steps.filter(x => x.done).length;
  return { steps, completed, total:steps.length, complete:completed === steps.length };
}

function shouldShowFirstRunQuickStart() {
  return !commissioningState().complete
    && localStorage.getItem(QUICK_START_HIDDEN_KEY) !== "1"
    && sessionStorage.getItem(QUICK_START_SNOOZED_SESSION_KEY) !== "1";
}

function updateCommissioningAssistant() {
  const progress = commissioningState();
  const access = hasConfigurationAccess();
  const stepIds = ["firstRunStepDevice","firstRunStepTag","firstRunStepAlarm","firstRunStepHistorian"];
  progress.steps.forEach((step, index) => {
    const el = $(stepIds[index]);
    if (!el) return;
    el.classList.toggle("completed", step.done);
    el.classList.toggle("next", !step.done && progress.steps.slice(0,index).every(x => x.done));
    const badge = el.querySelector("small");
    if (badge) badge.textContent = step.done
      ? (state.language === "tr" ? "TAMAMLANDI" : "COMPLETE")
      : (state.language === "tr" ? "BEKLİYOR" : "PENDING");
  });

  if ($("firstRunProgressText")) $("firstRunProgressText").textContent = `${progress.completed}/${progress.total}`;
  if ($("firstRunProgressFill")) $("firstRunProgressFill").style.width = `${progress.completed / progress.total * 100}%`;
  if ($("overviewSetupProgress")) $("overviewSetupProgress").textContent = `${progress.completed}/${progress.total}`;
  if ($("overviewSetupProgressBar")) $("overviewSetupProgressBar").style.width = `${progress.completed / progress.total * 100}%`;
  const nextIndex = progress.steps.findIndex(x => !x.done);
  const nextLabels = state.language === "tr"
    ? ["Cihaz bağla", "Canlı Tag doğrula", "Alarm oluştur", "Historian kaydını aç"]
    : ["Connect a device", "Verify a live Tag", "Create an alarm", "Enable Historian recording"];
  const nextText = progress.complete
    ? (state.language === "tr" ? "Kurulum tamamlandı" : "Commissioning complete")
    : `${state.language === "tr" ? "Sıradaki" : "Next"}: ${nextLabels[nextIndex]}`;
  if ($("overviewSetupNextStep")) $("overviewSetupNextStep").textContent = nextText;
  if ($("overviewSetupNext")) $("overviewSetupNext").textContent = progress.complete
    ? (state.language === "tr" ? "Kurulum tamamlandı" : "Setup complete")
    : (state.language === "tr" ? "15 dakikalık devreye alma" : "15-minute commissioning");
  document.querySelectorAll(".setup-ring").forEach(el => { el.textContent = String(progress.completed); });
  if ($("firstRunAccessState")) {
    $("firstRunAccessState").textContent = access ? t("firstRunAccessReady") : t("firstRunAccessRequired");
    $("firstRunAccessState").classList.toggle("ready", access);
  }
  if ($("firstRunStart")) $("firstRunStart").textContent = access ? t("firstRunStart") : t("firstRunSignIn");

  const setupButtons = [$("setupAssistantButton"), $("overviewSetupAssistant")].filter(Boolean);
  setupButtons.forEach(button => {
    button.classList.toggle("hidden", progress.complete);
    const label = button.querySelector("strong") || button;
    label.textContent = `${t("setupAssistant")} ${progress.completed}/${progress.total}`;
  });
}

function openFirstRunQuickStart(force = false) {
  if (!force && !shouldShowFirstRunQuickStart()) return;
  if (commissioningState().complete) return;
  updateCommissioningAssistant();
  const dontShow = $("firstRunDontShowAgain");
  if (dontShow) dontShow.checked = false;
  $("firstRunQuickStart")?.classList.remove("hidden");
}

function closeFirstRunQuickStart({snooze = true, permanent = false} = {}) {
  if (permanent) localStorage.setItem(QUICK_START_HIDDEN_KEY, "1");
  if (snooze) sessionStorage.setItem(QUICK_START_SNOOZED_SESSION_KEY, "1");
  $("firstRunQuickStart")?.classList.add("hidden");
}

function openNextCommissioningStep() {
  const progress = commissioningState();
  if (!hasConfigurationAccess()) {
    closeFirstRunQuickStart({snooze:false});
    openAccountModal();
    return;
  }

  state.quickStartActive = true;
  closeFirstRunQuickStart({snooze:false});
  if (!progress.steps[0].done) return openDeviceModal();
  if (!progress.steps[1].done) { navigate("tags"); return openAddTagModal(); }
  if (!progress.steps[2].done) { navigate("alarms"); return openAddAlarmModal(); }
  if (!progress.steps[3].done) { navigate("historian"); return openAddHistorianModal(); }
  state.quickStartActive = false;
}

function startFirstRunQuickSetup() {
  openNextCommissioningStep();
}

function continueCommissioningIfActive() {
  updateCommissioningAssistant();
  if (!state.quickStartActive) return;
  if (commissioningState().complete) {
    state.quickStartActive = false;
    navigate("overview");
    showToast(state.language === "tr" ? "15 dakikalık kurulum akışı tamamlandı." : "15-minute setup flow completed.");
    return;
  }
  setTimeout(() => openFirstRunQuickStart(true), 180);
}

function accountInitials(name) {
  return String(name || "P").split(/\s+/).filter(Boolean).slice(0,2).map(x => x[0]).join("").toUpperCase() || "P";
}

function licenseProductLabel(plan) {
  const value = String(plan || "").toUpperCase();
  if (value === "ALARM_MONITORING") return state.language === "tr" ? "Alarm İzleme" : "Alarm Monitoring";
  if (value === "HISTORIAN") return "Historian";
  if (value === "ALARM_HISTORIAN") return state.language === "tr" ? "Alarm + Historian" : "Alarm + Historian";
  return plan || "—";
}

function billingPeriodLabel(period) {
  const value = String(period || "").toUpperCase();
  if (value === "MONTHLY") return state.language === "tr" ? "Aylık" : "Monthly";
  if (value === "YEARLY") return state.language === "tr" ? "Yıllık" : "Yearly";
  if (value === "SIX_MONTHS") return state.language === "tr" ? "6 Aylık (eski)" : "6 Months (legacy)";
  return period || "—";
}

function graceRemainingText(license = state.license) {
  if (!license?.graceUntil) return "—";
  const graceUntil = new Date(license.graceUntil);
  const ms = graceUntil.getTime() - Date.now();
  if (!Number.isFinite(ms) || ms <= 0) return state.language === "tr" ? "Grace süresi doldu" : "Grace period ended";
  const days = Math.max(1, Math.ceil(ms / 86400000));
  const until = new Intl.DateTimeFormat(state.language === "tr" ? "tr-TR" : "en-GB", {
    day:"2-digit", month:"short", year:"numeric", hour:"2-digit", minute:"2-digit"
  }).format(graceUntil);
  return state.language === "tr"
    ? `Grace: ${days} gün kaldı • ${until} tarihinde biter`
    : `Grace: ${days} day${days === 1 ? "" : "s"} remaining • ends ${until}`;
}

function isCommercialLicenseReady(license = state.license) {
  // A verified import can be represented by three independent sources while signed out:
  // 1) the privacy-safe /api/license summary, 2) /api/access/status, or 3) the current
  // browser import result. Treat any of them as sufficient to present the sign-in step.
  // The backend /api/access/login remains the security authority for actual sign-in.
  const accessStatus = String(state.accessSession?.status || '').toUpperCase();
  return Boolean(
    license?.licenseInstalled ||
    (license?.isValid && license?.assignedUserId) ||
    accessStatus === 'SIGN_IN_REQUIRED' ||
    state.importedAccount !== null
  );
}

function isLocalUserSignedIn() {
  return Boolean(state.accessSession?.authenticated);
}

function licenseRemainingText(license = state.license) {
  if (!license) return state.language === "tr" ? "Aktif değil" : "Not active";
  const status = String(license.status || "").toUpperCase();

  if (status === "REVOKED") return state.language === "tr" ? "İptal edildi" : "Revoked";
  if (status === "GRACE") return graceRemainingText(license);
  if (status === "EXPIRED") return state.language === "tr" ? "Süresi doldu" : "Expired";
  if (status === "INVALID" || !license.isValid) return state.language === "tr" ? "Geçersiz" : "Invalid";
  if (!license.expiresAt) return "—";

  const expires = new Date(license.expiresAt);
  if (Number.isNaN(expires.getTime())) return state.language === "tr" ? "Bitiş tarihi okunamadı" : "Expiry unavailable";

  const remainingMs = expires.getTime() - Date.now();
  if (remainingMs <= 0) return state.language === "tr" ? "Süresi doldu" : "Expired";

  const totalHours = Math.ceil(remainingMs / 3600000);
  if (totalHours < 48) return state.language === "tr"
    ? `${totalHours} saat kaldı`
    : `${totalHours} hour${totalHours === 1 ? '' : 's'} remaining`;

  const days = Math.ceil(totalHours / 24);
  return state.language === "tr"
    ? `${days} gün kaldı`
    : `${days} day${days === 1 ? '' : 's'} remaining`;
}

function licenseExpiryDateText(license = state.license) {
  if (!license?.expiresAt) return state.language === "tr" ? "Süre sınırı yok" : "No expiry";
  const expires = new Date(license.expiresAt);
  if (Number.isNaN(expires.getTime())) return "—";
  return new Intl.DateTimeFormat(state.language === "tr" ? "tr-TR" : "en-GB", {
    day:"2-digit", month:"short", year:"numeric"
  }).format(expires);
}

function hasConfigurationAccess() {
  const license = state.license;
  return Boolean(license?.isValid && license?.assignedUserId && state.accessSession?.authenticated);
}

function isLicenseOverCapacity() {
  return Boolean(state.licenseUsage?.overCapacity);
}

function canMutateConfiguration() {
  return hasConfigurationAccess() && !isLicenseOverCapacity();
}

function requireConfigurationAccess() {
  if (hasConfigurationAccess()) return true;
  showToast(state.language === "tr"
    ? "Yapılandırmayı değiştirmek için lisanslı hesapla giriş yapın."
    : "Sign in with the licensed account to change configuration.");
  openAccountModal();
  return false;
}

function renderAccount() {
  const license = state.license;
  const imported = state.importedAccount;
  const owner = license?.assignedUserName || license?.assignedUserEmail || imported?.userName || imported?.userEmail || null;
  const plan = isLocalUserSignedIn() ? licenseProductLabel(license?.plan || 'VIEW ONLY') : (state.language === 'tr' ? 'Lisans hazır' : 'License ready');
  const activated = isCommercialLicenseReady(license);
  const authenticated = isLocalUserSignedIn();
  const access = hasConfigurationAccess();
  const remaining = licenseRemainingText(license);

  // Customer build: configuration access exists only after offline license sign-in.
  if ($("accountName")) $("accountName").textContent = authenticated ? (owner || t('signedIn')) : t('signIn');
  if ($("accountAvatar")) {
    $("accountAvatar").textContent = accountInitials(owner);
    $("accountAvatar").classList.toggle('hidden', !authenticated);
  }
  $("accountButton")?.classList.toggle('signed-in', authenticated);
  $("accountButton")?.classList.toggle('signed-out', !authenticated);

  // Compact access modal: before login show only import + credentials. After login show
  // only the signed-in state and remaining license time.
  // Once a valid license is imported/installed, remove the import step from this access popup.
  // License replacement remains available on the dedicated License page.
  $("accountLicenseImportSection")?.classList.toggle('hidden', activated);
  $("accountSignedLicense")?.classList.toggle('hidden', !authenticated);
  $("accountCredentials")?.classList.toggle('hidden', authenticated);
  $("accountSignIn")?.classList.toggle('hidden', authenticated);
  $("accountSignOut")?.classList.toggle('hidden', !authenticated);

  if ($("accountSignedLicensePlan")) $("accountSignedLicensePlan").textContent = plan;
  if ($("accountLicenseRemaining")) {
    $("accountLicenseRemaining").textContent = remaining;
    if (license?.expiresAt) $("accountLicenseRemaining").title = new Date(license.expiresAt).toLocaleString();
  }

  if ($("accountLicenseImportState")) {
    $("accountLicenseImportState").textContent = activated
      ? (state.language === 'tr' ? `${plan} lisansı hazır` : `${plan} license ready`)
      : (state.language === 'tr' ? '.pgnlicense içe aktarın' : 'Import .pgnlicense');
  }
  if ($("accountLicenseHint")) {
    $("accountLicenseHint").textContent = activated
      ? (state.language === 'tr' ? 'Lisans yerel olarak doğrulandı. Yapılandırma oturumu açmak için giriş yapın.' : 'License verified locally. Sign in below to open a local configuration session.')
      : (state.language === 'tr' ? 'Lisans yerel olarak doğrulanır. Giriş için bulut bağlantısı gerekmez.' : 'The license is verified locally. No cloud connection is required for sign-in.');
  }

  if ($("accountEmail") && !$("accountEmail").value && activated)
    $("accountEmail").value = license?.assignedUserEmail || imported?.userEmail || '';
  if ($("accountSessionState")) {
    $("accountSessionState").textContent = authenticated
      ? `${license?.assignedUserEmail || owner || t('signedIn')} • ${t('signedIn').toLowerCase()}`
      : activated
        ? (owner
            ? `${owner} • ${state.language === 'tr' ? 'giriş yaparak devam edin' : 'sign in to continue'}`
            : (state.language === 'tr' ? 'Lisans hazır • devam etmek için giriş yapın' : 'License ready • sign in to continue'))
        : (state.language === 'tr' ? 'Devam etmek için geçerli bir .pgnlicense içe aktarın' : 'Import a valid .pgnlicense to continue');
    $("accountSessionState").classList.toggle('authenticated', authenticated);
  }
  // Never deadlock the login UI on a duplicated frontend license check. If no usable
  // license is installed, /api/access/login returns the authoritative validation error.
  // While signed out the button stays clickable; after authentication it is hidden/disabled.
  if ($("accountSignIn")) $("accountSignIn").disabled = authenticated;
  if ($("accountSignOut")) $("accountSignOut").disabled = !authenticated;

  // Detailed license metadata belongs on the License page, not in the account popup.
  if ($("licenseAssignedUser")) $("licenseAssignedUser").textContent = owner || '—';
  if ($("licenseAssignedEmail")) $("licenseAssignedEmail").textContent = license?.assignedUserEmail || '—';
  if ($("licenseAssignedRole")) $("licenseAssignedRole").textContent = license?.portalRole || '—';
  if ($("licenseSiteName")) $("licenseSiteName").textContent = license?.siteName || '—';
  if ($("licenseAccessMode")) $("licenseAccessMode").textContent = access ? 'LOCAL SESSION ACTIVE' : activated ? 'SIGN-IN REQUIRED' : 'VIEW ONLY';

  $("readOnlyBanner")?.classList.toggle("hidden", access);
  applyAccessMode();
}

async function loadAccessStatus() {
  try {
    state.accessSession = await api('/api/access/status');
    if (!state.accessSession?.authenticated) sessionStorage.removeItem('prognode.accessSession');
  } catch (error) {
    console.warn('Access status unavailable', error);
    state.accessSession = null;
    sessionStorage.removeItem('prognode.accessSession');
  }
  renderAccount();
  window.dispatchEvent(new Event('prognode:accesschanged'));
}

async function signInLocalAccess() {
  const email = $("accountEmail")?.value?.trim();
  const password = $("accountPassword")?.value || '';
  if (!email || !password) {
    showToast(state.language === 'tr' ? 'E-posta ve şifre girin.' : 'Enter email and password.');
    return;
  }
  try {
    const result = await api('/api/access/login', {method:'POST', body:JSON.stringify({email,password})});
    if (result.token) sessionStorage.setItem('prognode.accessSession', result.token);
    window.dispatchEvent(new Event('prognode:accesschanged'));
    state.accessSession = result;
    state.importedAccount = null;
    if ($("accountPassword")) $("accountPassword").value = '';
    await refreshLicenseForSession();
    renderAccount();
    showToast(state.language === 'tr' ? 'Giriş başarılı.' : 'Signed in successfully.');
    closeAccountModal();
    if (hasConfigurationAccess()) openFirstRunQuickStart();
  } catch (error) {
    showToast(error.message);
  }
}

async function signOutLocalAccess() {
  qrStopPolling();
  if ($('qrPairBox')) $('qrPairBox').classList.add('hidden');
  if ($('qrGraphic')) $('qrGraphic').replaceChildren();
  pairingQrExpiresAt=0;
  try { await api('/api/access/logout', {method:'POST', body:'{}'}); } catch {}
  sessionStorage.removeItem('prognode.accessSession');
  window.dispatchEvent(new Event('prognode:accesschanged'));
  state.accessSession = null;
  state.importedAccount = null;
  if ($("accountEmail")) $("accountEmail").value = '';
  if ($("accountPassword")) $("accountPassword").value = '';
  await refreshLicenseForSession();
  renderAccount();
  showToast(state.language === 'tr' ? 'Çıkış yapıldı.' : 'Signed out.');
}

function openAccountModal() {
  $("accountModal")?.classList.remove("hidden");
  renderAccount();
}

function closeAccountModal() {
  $("accountModal")?.classList.add("hidden");
}

async function importLicenseFromFile(file) {
  if (!file) {
    showToast(state.language === "tr" ? "Bir .pgnlicense dosyası seçin." : "Choose a .pgnlicense file.");
    return;
  }
  const form = new FormData();
  form.append("license", file);
  const response = await fetch("/api/license/import", { method:"POST", body:form });
  const body = await response.json().catch(() => null);
  if (!response.ok) throw new Error(body?.message || `License import failed (${response.status})`);

  sessionStorage.removeItem('prognode.accessSession');
  state.accessSession = null;
  state.importedAccount = {
    userName: body?.assignedUserName || '',
    userEmail: body?.assignedUserEmail || ''
  };
  sessionStorage.setItem('prognode.importedAccount', JSON.stringify(state.importedAccount));
  // HTTP 200 from /api/license/import means the signed file has already passed canonical
  // validation, Ed25519 verification, entitlement validation and atomic persistence.
  // Mark the access UI ready immediately instead of waiting for the next background poll.
  // RC6.3.1-HF2 fixes the race where the successful import was still rendered as
  // "Import a valid .pgnlicense to continue" and kept Sign in disabled.
  state.license = {
    licenseInstalled: true,
    isValid: false,
    status: 'SIGN_IN_REQUIRED',
    plan: null,
    entitlements: { modules: [] }
  };
  state.licenseUsage = { visible:false, maxTags:null, usedTags:null, remainingTags:null, unlimited:false, overCapacity:false };

  if ($("accountEmail")) $("accountEmail").value = body?.assignedUserEmail || '';
  if ($("accountPassword")) $("accountPassword").value = '';
  if ($("accountLicenseFile")) $("accountLicenseFile").value = '';
  if ($("licenseImportFile")) $("licenseImportFile").value = '';

  // Render once immediately so the import section disappears and Sign in becomes clickable,
  // then reconcile with the authoritative signed-out server summary. importedAccount is kept
  // because the privacy-safe /api/license summary intentionally omits customer identity.
  renderLicense();
  renderAccount();
  renderTags();
  await refreshLicenseForSession();

  // Defensive fallback: a successful import is sufficient evidence that a local signed license
  // is installed. Never leave the sign-in button disabled merely because a stale/redacted UI
  // summary raced the import response.
  if (!isCommercialLicenseReady(state.license)) {
    state.license = {
      ...(state.license || {}),
      licenseInstalled: true,
      isValid: false,
      status: 'SIGN_IN_REQUIRED',
      entitlements: state.license?.entitlements || { modules: [] }
    };
    renderLicense();
    renderAccount();
  }

  $("accountEmail")?.focus();

  const who = body?.assignedUserName || body?.assignedUserEmail || '';
  showToast(state.language === "tr"
    ? `Lisans başarıyla içe aktarıldı${who ? ` • ${who}` : ''}. Şifrenizle giriş yapın.`
    : `License imported successfully${who ? ` • ${who}` : ''}. Sign in with your password.`);
}

function applyAccessMode() {
  const access = hasConfigurationAccess();
  const writable = canMutateConfiguration();
  const overCapacity = isLicenseOverCapacity();
  const selectors = [
    '#devicesAddDevice','#emptyAddDevice','#tagsAddTag','#emptyAddTag','#alarmsAddAlarm',
    '#historianAddTag','#trendsAddTrend','#executeImport','#saveDevice','#saveTag','#saveAlarm',
    '#saveTrend','#saveHistorianConfig','#devicesImport','#tagsImport','#alarmsImport','#historianImport',
    '#opAddDevice','#opAddTag','#opAddAlarm','#opHistorian','#opTrend','#opImport',
    '#pingModbusHost','#testModbusConnection','#sendTestNotification'
  ];
  selectors.forEach(selector => document.querySelectorAll(selector).forEach(el => {
    el.disabled = !writable;
    el.title = writable ? '' : overCapacity
      ? 'OVER_CAPACITY — reduce configured Tags or upgrade the license before changing configuration.'
      : 'View Only — activate a license and sign in to edit configuration.';
  }));

  document.querySelectorAll('[data-edit-device],[data-edit-tag],[data-edit-alarm],[data-edit-trend],[data-edit-historian]')
    .forEach(el => { el.disabled = !writable; el.classList.toggle('access-locked', !writable); });

  // Destructive/reduction actions stay available while OVER_CAPACITY so the customer can
  // return below the signed limit without Core deleting configuration automatically.
  document.querySelectorAll('[data-delete-device],[data-delete-tag],[data-delete-alarm],[data-delete-trend],[data-delete-historian],[data-stop-historian]')
    .forEach(el => { el.disabled = !access; el.classList.toggle('access-locked', !access); });

  updateCommissioningAssistant();
  updateOverviewCommandStrip();
}

function syncTrendRangePills() {
  document.querySelectorAll("[data-range-value]").forEach(button => {
    button.classList.toggle("active", button.dataset.rangeValue === $("trendRange")?.value);
  });
}

function trendAutoFit() {
  if (!state.trendPayload?.series?.length) return;
  const timestamps = state.trendPayload.series.flatMap(s => (s.points || []).map(p => new Date(p.timestamp).getTime())).filter(Number.isFinite);
  if (!timestamps.length) return;
  const spanMinutes = Math.max(5, Math.ceil((Math.max(...timestamps) - Math.min(...timestamps)) / 60000));
  const presets = [5, 15, 60, 480, 1440, 10080];
  const selected = presets.find(x => spanMinutes <= x) || 10080;
  $("trendRange").value = String(selected);
  $("trendRange").dataset.userChanged = "1";
  syncTrendRangePills();
  updateTrendCustomRangeVisibility();
  refreshTrendPoints();
}

function applyTheme() {
  document.documentElement.dataset.theme = state.theme;
  $("themeIcon").textContent = state.theme === "dark" ? "☾" : "☀";
  if ($("brandLogo")) $("brandLogo").src = state.theme === "dark" ? "/assets/prognode.png" : "/assets/prognode-light.png";
  if (state.trendPayload) setTimeout(drawTrendChart, 0);
}

function toggleTheme() {
  state.theme = state.theme === "dark" ? "light" : "dark";
  localStorage.setItem("prognode.theme", state.theme);
  applyTheme();
}

function setLanguage(language) {
  state.language = language;
  localStorage.setItem("prognode.language", language);
  applyLanguage();
}

function applyLanguage() {
  document.documentElement.lang = state.language;

  document.querySelectorAll("[data-i18n]").forEach(el => {
    el.textContent = t(el.dataset.i18n);
  });

  $("langEN").classList.toggle("active", state.language === "en");
  $("langTR").classList.toggle("active", state.language === "tr");

  $("deviceSearch").placeholder =
    state.language === "tr" ? "Cihazlarda ara..." : "Search devices...";

  $("tagSearch").placeholder =
    state.language === "tr" ? "Taglarda ara..." : "Search tags...";

  $("deviceNameInput").placeholder =
    state.language === "tr" ? "örn. Kazan PLC" : "e.g. Boiler PLC";

  renderDevices();
  renderTags();
  renderProtocolGrid();
  renderAlarms();
  renderTrendList();
  renderHistorianStats();
  renderHistorianConfigurations();
  updateTrendCustomRangeVisibility();
  drawTrendChart();
  renderNotifications();
  renderAgentStatus();
  updateOverview();
  if (state.importPreview) renderImportPreview();
  renderServerClock();
  navigate(state.page);

  if (state.license) renderLicense();
  if ($("openLicensePage")) $("openLicensePage").textContent = t("openLicensePage");
  renderAccount();
  syncTrendRangePills();
  updateTagFormVisibility();
  window.dispatchEvent(new Event('prognode:languagechange'));
}

function navigate(page) {
  state.page = page;

  document.querySelectorAll(".page").forEach(x =>
    x.classList.toggle("active", x.id === `page-${page}`));

  document.querySelectorAll(".nav-item").forEach(x =>
    x.classList.toggle("active", x.dataset.page === page));

  const labels = {
    overview:t("overview"), devices:t("devices"), tags:t("tags"),
    alarms:t("alarms"), trends:t("trends"), historian:t("historian"),
    diagnostics:t("diagnostics"), notifications:t("notifications"), dataexchange:t("dataExchange"),
    license:t("license"), settings:t("settings")
  };

  $("pageTitle").textContent = page === "trends" ? "Trend Studio" : (labels[page] || "PROGNODE");
  if ($("pageSubtitle")) {
    $("pageSubtitle").textContent = page === "overview" ? t("plantPerformance") : "";
    $("pageSubtitle").classList.toggle("hidden", page !== "overview");
  }
  $("sidebar").classList.remove("open");
  $("mobileMenu")?.setAttribute("aria-expanded", "false");
  if(page === "settings" && document.querySelector(".settings-tab.active")?.dataset.settingsTab === "backup") void loadBackupCenter();
  window.dispatchEvent(new CustomEvent("prognode:pagechange", {detail:{page}}));
  if (page === "diagnostics") renderDiagnostics();
}

function openAlarmFromNotification() {
  navigate('alarms');
  document.getElementById('page-alarms')?.scrollIntoView({block:'start'});
}

function limitText(limit) {
  if (!limit) return "—";
  return limit.isUnlimited ? "UNLIMITED" : String(limit.value);
}

function yesNo(value) {
  if (state.language === "tr")
    return value ? "ETKİN" : "DEVRE DIŞI";
  return value ? "ENABLED" : "DISABLED";
}

async function loadServerAccess() {
  try {
    const identity = await api("/api/server/identity");
    state.serverIdentity = identity;
    // Pairing codes are not created during polling. Only explicit local admin action
    // in the Manual tab can display the short-lived OTP and independent SAS.
    state.serverPairing = null;
    renderServerAccess();
    await loadRemoteAccess();
    if (document.querySelector('#hf64PairedDevices') && document.querySelector('[data-settings-panel="general"]')?.offsetParent !== null)
      if (typeof window.hf64LoadDevices === "function") void window.hf64LoadDevices();
  } catch (error) {
    console.warn("Server access status unavailable", error);
  }
}

function renderServerAccess() {
  const identity = state.serverIdentity;
  const pairing = state.serverPairing;
  if (!identity) return;
  if ($("serverAccessName")) $("serverAccessName").textContent = identity.displayName || "PROGNODE Server";
  if ($("serverAccessId")) $("serverAccessId").textContent = identity.serverId || "—";
  if ($("serverAccessApi")) $("serverAccessApi").textContent = `API ${identity.apiVersion || "v1"} • TCP ${identity.apiPort}`;
  if ($("serverAccessDiscovery")) $("serverAccessDiscovery").textContent = state.language === "tr"
    ? `UDP ${identity.discoveryPort} • Server ID keşfi`
    : `UDP ${identity.discoveryPort} • Server ID discovery`;
  // Never overwrite the manual tab's short-lived code while background status refreshes.
  if (pairing && $("serverPairingCode")) $("serverPairingCode").textContent = pairing.code;
  if (pairing && $("serverPairingExpiry")) $("serverPairingExpiry").textContent =
    (state.language === "tr" ? `${pairing.expiresInSeconds} sn kaldı` : `${pairing.expiresInSeconds}s remaining`);
}

async function loadRemoteAccess() {
  try {
    const [status, clients] = await Promise.all([
      api("/api/remote-access/status"),
      api("/api/remote-access/clients")
    ]);
    state.remoteAccessStatus = status;
    state.remoteAccessClients = Array.isArray(clients) ? clients : [];
    renderRemoteAccess();
  } catch (error) {
    console.warn("Remote Access status unavailable", error);
  }
}

function renderRemoteAccess() {
  const status = state.remoteAccessStatus;
  if (!status) return;
  const entitled = Boolean(status.entitled);
  const bound = Boolean(status.serverBound);
  const active = String(status.subscriptionStatus || "").toUpperCase() === "ACTIVE";
  const badge = $("remoteAccessBadge");
  if (badge) {
    badge.textContent = !entitled ? t("remoteNotPurchased") : (bound && active ? "ACTIVE" : t("remoteCloudPending"));
    badge.classList.toggle("warning", entitled && !(bound && active));
  }
  if ($("remoteAccessSubscription")) $("remoteAccessSubscription").textContent = entitled ? (status.subscriptionStatus || "PENDING") : t("remoteNotPurchased");
  if ($("remoteAccessBinding")) $("remoteAccessBinding").textContent = entitled ? (status.bindingStatus || "UNBOUND") : "—";
  if ($("remoteAccessCapacity")) {
    $("remoteAccessCapacity").textContent = !entitled ? "0" : status.unlimitedClients
      ? `${status.usedClients || 0} / UNLIMITED`
      : `${status.usedClients || 0} / ${status.maxClients ?? 0}`;
  }
  if ($("remoteAccessLastSync")) $("remoteAccessLastSync").textContent = status.lastSyncedAtUtc ? new Date(status.lastSyncedAtUtc).toLocaleString() : "—";
  if ($("remoteAccessBind")) $("remoteAccessBind").disabled = !entitled || !status.cloudConfigured || bound || !canMutateConfiguration();
  if ($("remoteAccessSync")) $("remoteAccessSync").disabled = !entitled || !status.cloudConfigured || !bound || !canMutateConfiguration();
  if ($("remoteAccessNote")) {
    $("remoteAccessNote").textContent = status.lastError
      ? `${t("remoteAccessLocalNote")} · ${status.lastError}`
      : t("remoteAccessLocalNote");
  }

  const list = $("remoteAccessClientList");
  if (!list) return;
  const clients = state.remoteAccessClients || [];
  if (!clients.length) {
    list.innerHTML = `<div class="remote-client-empty">${escapeHtml(t("noRemoteClients"))}</div>`;
    return;
  }
  list.innerHTML = clients.map(client => {
    const canEnable = entitled && bound && active && Boolean(client.devicePublicKey) && canMutateConfiguration();
    const action = client.remoteEnabled
      ? `<button class="ghost-button remote-revoke" data-client-id="${escapeHtml(client.localClientId)}" ${canMutateConfiguration() ? "" : "disabled"}>${escapeHtml(t("revokeRemote"))}</button>`
      : `<button class="ghost-button remote-enable" data-client-id="${escapeHtml(client.localClientId)}" ${canEnable ? "" : "disabled"}>${escapeHtml(state.language === "tr" ? "Remote Etkinleştir" : "Enable Remote")}</button>`;
    return `
      <div class="remote-client-row">
        <div><strong>${escapeHtml(client.deviceName || "Client")}</strong><small>${escapeHtml(client.platform || "Unknown")} · ${client.remoteEnabled ? escapeHtml(client.userDisplayName || client.userId || "Remote") : escapeHtml(t("remoteLanOnly"))}</small></div>
        <div class="remote-client-row-actions"><span class="active-badge">${escapeHtml(client.remoteEnabled ? (client.status || "ACTIVE") : "LAN")}</span>${action}</div>
      </div>`;
  }).join("");
  list.querySelectorAll(".remote-revoke").forEach(button => button.addEventListener("click", async () => {
    try {
      await api(`/api/remote-access/clients/${button.dataset.clientId}`, {method:"DELETE"});
      showToast(state.language === "tr" ? "Remote seat serbest bırakıldı." : "Remote seat released.");
      await loadRemoteAccess();
    } catch (error) { showToast(error.message); }
  }));
  list.querySelectorAll(".remote-enable").forEach(button => button.addEventListener("click", async () => {
    try {
      const client = clients.find(x => String(x.localClientId) === String(button.dataset.clientId));
      if (!client) return;
      const result = await api("/api/remote-access/clients/register", {
        method:"POST",
        body:JSON.stringify({clientId:client.localClientId, devicePublicKey:client.devicePublicKey, platform:client.platform})
      });
      showToast(result.message || (state.language === "tr" ? "Remote Access etkinleştirildi." : "Remote Access enabled."));
      await loadRemoteAccess();
    } catch (error) { showToast(error.message); }
  }));
}

async function bindRemoteAccess() {
  try {
    const result = await api("/api/remote-access/bind", {method:"POST", body:"{}"});
    showToast(result.message || "Remote Access bound.");
    await loadRemoteAccess();
  } catch (error) { showToast(error.message); }
}

async function syncRemoteAccess() {
  try {
    const result = await api("/api/remote-access/sync", {method:"POST", body:"{}"});
    showToast(result.message || "Remote Access synchronized.");
    await loadRemoteAccess();
  } catch (error) { showToast(error.message); }
}

async function refreshLicenseForSession() {
  try {
    const access = await api('/api/access/status');
    state.accessSession = access;
    if (!access?.authenticated) sessionStorage.removeItem('prognode.accessSession');

    const [license, licenseUsage] = await Promise.all([
      api("/api/license"),
      api("/api/license/usage")
    ]);
    state.license = license;
    state.licenseUsage = licenseUsage;
    renderLicense();
    renderTags();
    renderAccount();
    applyAccessMode();
  } catch (error) {
    console.warn('License refresh unavailable', error);
  }
}

async function loadHealthAndLicense() {
  const [health, license, licenseUsage] =
    await Promise.all([
      api("/api/health"),
      api("/api/license"),
      api("/api/license/usage")
    ]);

  state.health = health;
  state.license = license;
  state.licenseUsage = licenseUsage;

  $("sidebarVersion").textContent = `v${health.coreVersion}`;
  $("metricCoreVersion").textContent = `PROGNODE ${health.coreVersion}`;
  if ($("topLicensePlan")) $("topLicensePlan").textContent = licenseProductLabel(license.plan);

  await loadAccessStatus();
  renderLicense();
  renderAccount();
}

function renderLicense() {
  const license = state.license;
  if (!license) return;

  const modules = new Set(license.entitlements?.modules || []);
  const active = Boolean(license.isValid);
  const signedIn = isLocalUserSignedIn();
  const installed = Boolean(license.licenseInstalled || license.licenseId && !["NONE","INVALID","INSTALLED"].includes(String(license.licenseId).toUpperCase()) || license.licenseId === "INSTALLED");

  $("licenseDetailsPanel")?.classList.toggle("hidden", !signedIn);
  $("licenseSignedOutCard")?.classList.toggle("hidden", signedIn);
  if (!signedIn) {
    if ($("licenseSignedOutTitle")) $("licenseSignedOutTitle").textContent = installed
      ? (state.language === "tr" ? "Lisans ayrıntılarını görmek için giriş yapın" : "Sign in to view license details")
      : (state.language === "tr" ? "Devam etmek için lisans içe aktarın" : "Import a license to continue");
    if ($("licenseSignedOutText")) $("licenseSignedOutText").textContent = installed
      ? (state.language === "tr" ? "Bitiş tarihi, kalan süre, kapasite ve entitlement bilgileri çıkış yapıldığında gizlenir." : "Expiry, remaining time, capacity and entitlement details are hidden while signed out.")
      : (state.language === "tr" ? "İmzalı .pgnlicense dosyasını içe aktarın, ardından lisansa bağlı hesapla giriş yapın." : "Import the signed .pgnlicense file, then sign in with the account linked to it.");
    if ($("licenseSignInButton")) $("licenseSignInButton").textContent = installed
      ? (state.language === "tr" ? "Giriş yap" : "Sign in")
      : (state.language === "tr" ? "Lisans içe aktar / Giriş" : "Import license / Sign in");

    // Defense in depth: remove stale sensitive values from the DOM immediately after sign-out.
    ["licenseCustomer","licenseStatus","licensePlan","licenseExpiryDate","licenseRemainingDays",
     "licenseAssignedUser","licenseAssignedEmail","licenseTagCapacity","licenseTagUsage",
     "licenseBillingPeriod","licenseLifecycleStatus","licenseValidFrom","licenseRemoteAccessCapacity"]
      .forEach(id => { if ($(id)) $(id).textContent = "—"; });
    $("licenseExpiringBanner")?.classList.add("hidden");
    $("licenseGraceBanner")?.classList.add("hidden");
    $("licenseOverCapacityBanner")?.classList.add("hidden");
    renderAccount();
    return;
  }

  if ($("licenseCustomer")) $("licenseCustomer").textContent = license.customer || "—";
  if ($("licenseStatus")) {
    $("licenseStatus").textContent = license.status || "—";
    const lifecycle = String(license.status || "").toUpperCase();
    $("licenseStatus").classList.toggle("warning", lifecycle === "EXPIRING_SOON");
    $("licenseStatus").classList.toggle("danger", lifecycle === "GRACE" || lifecycle === "EXPIRED" || lifecycle === "INVALID" || lifecycle === "REVOKED");
  }
  if ($("licensePlan")) $("licensePlan").textContent = licenseProductLabel(license.plan);
  if ($("licenseExpiryDate")) $("licenseExpiryDate").textContent = licenseExpiryDateText(license);
  if ($("licenseRemainingDays")) $("licenseRemainingDays").textContent = licenseRemainingText(license);
  if ($("licenseAssignedUser")) $("licenseAssignedUser").textContent = license.assignedUserName || license.assignedUserEmail || "—";
  if ($("licenseAssignedEmail")) $("licenseAssignedEmail").textContent = license.assignedUserEmail || "—";
  if ($("licenseAccessMode")) $("licenseAccessMode").textContent = signedIn
    ? (state.language === "tr" ? "Yerel oturum aktif" : "Local session active")
    : (active ? (state.language === "tr" ? "Giriş gerekli" : "Sign-in required") : (state.language === "tr" ? "Lisans gerekli" : "License required"));

  const usage = state.licenseUsage || {};
  const maxTags = usage.maxTags;
  const usedTags = Number(usage.usedTags || 0);
  if ($("licenseTagCapacity")) $("licenseTagCapacity").textContent = usage.unlimited
    ? (usage.legacyUnlimited
        ? (state.language === "tr" ? "Sınırsız • Legacy" : "Unlimited • Legacy")
        : (state.language === "tr" ? "Sınırsız" : "Unlimited"))
    : String(maxTags ?? "—");
  if ($("licenseTagUsage")) $("licenseTagUsage").textContent = usage.unlimited
    ? (state.language === "tr" ? `${usedTags} Tag yapılandırıldı` : `${usedTags} Tags configured`)
    : `${usedTags} / ${maxTags ?? "—"} ${state.language === "tr" ? "kullanılıyor" : "used"}`;
  if ($("licenseBillingPeriod")) $("licenseBillingPeriod").textContent = billingPeriodLabel(license.billingPeriod);
  if ($("licenseLifecycleStatus")) $("licenseLifecycleStatus").textContent = license.status || "—";
  if ($("licenseValidFrom")) {
    const validFrom = license.validFrom ? new Date(license.validFrom) : null;
    $("licenseValidFrom").textContent = validFrom && !Number.isNaN(validFrom.getTime())
      ? `${state.language === "tr" ? "Başlangıç" : "Valid from"}: ${new Intl.DateTimeFormat(state.language === "tr" ? "tr-TR" : "en-GB", {day:"2-digit", month:"short", year:"numeric"}).format(validFrom)}`
      : "—";
  }

  const lifecycle = String(license.status || "").toUpperCase();
  const expiringSoon = lifecycle === "EXPIRING_SOON";
  const inGrace = lifecycle === "GRACE";
  const overCapacity = Boolean(usage.overCapacity);

  $("licenseExpiringBanner")?.classList.toggle("hidden", !expiringSoon);
  if ($("licenseExpiringTitle")) $("licenseExpiringTitle").textContent = state.language === "tr" ? "Lisansın süresi yakında doluyor" : "License expires soon";
  if ($("licenseExpiringRemaining")) $("licenseExpiringRemaining").textContent = expiringSoon ? licenseRemainingText(license) : "—";

  $("licenseGraceBanner")?.classList.toggle("hidden", !inGrace);
  if ($("licenseGraceTitle")) $("licenseGraceTitle").textContent = state.language === "tr" ? "Lisans yenilemesi gerekli" : "License renewal required";
  if ($("licenseGraceRemaining")) $("licenseGraceRemaining").textContent = inGrace ? graceRemainingText(license) : "—";

  $("licenseOverCapacityBanner")?.classList.toggle("hidden", !overCapacity);
  if ($("licenseOverCapacityTitle")) $("licenseOverCapacityTitle").textContent = state.language === "tr" ? "Lisans kapasitesi aşıldı" : "License capacity exceeded";
  if ($("licenseOverCapacityDetail")) $("licenseOverCapacityDetail").textContent = overCapacity
    ? (state.language === "tr"
        ? `${usedTags} / ${maxTags} Tag • Tag sayısını azaltın veya lisansı yükseltin.`
        : `${usedTags} / ${maxTags} Tags • Reduce Tags or upgrade the license.`)
    : "—";
  const setModuleState = (id, enabled) => {
    const el = $(id);
    if (!el) return;
    el.classList.toggle("disabled", !enabled);
    const small = el.querySelector("small");
    if (small) small.textContent = enabled ? t("moduleIncluded") : (state.language === "tr" ? "Dahil değil" : "Not included");
  };
  setModuleState("licenseAlarmModule", modules.has("ALARM"));
  setModuleState("licenseHistorianModule", modules.has("HISTORIAN"));
  const remoteLicensed = Boolean(license.entitlements?.remoteAccessEnabled);
  setModuleState("licenseRemoteAccessModule", remoteLicensed);
  if ($("licenseRemoteAccessCapacity")) {
    const remoteExpiry = license.entitlements?.remoteAccessExpiresAtUtc ? new Date(license.entitlements.remoteAccessExpiresAtUtc) : null;
    const remoteExpired = remoteExpiry && !Number.isNaN(remoteExpiry.getTime()) && remoteExpiry.getTime() <= Date.now();
    $("licenseRemoteAccessCapacity").textContent = remoteLicensed
      ? `${license.entitlements?.remoteAccessUnlimited ? (state.language === "tr" ? "Sınırsız remote client" : "Unlimited remote clients") : `${license.entitlements?.maxRemoteClients ?? 0} remote clients`}${remoteExpired ? (state.language === "tr" ? " • süresi doldu" : " • expired") : ""}`
      : (state.language === "tr" ? "Dahil değil" : "Not included");
  }
  renderAccount();
}

async function importLicenseFile() {
  try {
    await importLicenseFromFile($("licenseImportFile")?.files?.[0]);
    openAccountModal();
    $("accountEmail")?.focus();
  }
  catch (error) { showToast(error.message); }
}

async function loadProtocols() {
  state.protocols = await api("/api/protocols");
  renderProtocolGrid();
}

function renderProtocolGrid() {
  const grid = $("protocolGrid");
  if (!grid || !state.protocols.length) return;

  const trDescriptions = {
    mock:"Arayüz ve motor testleri için dahili simülatör.",
    "modbus-tcp":"Protokol seviyesinde test ve canlı polling bulunan Modbus TCP connector.",
    "siemens-s7-tcp":"S7-1200/1500 için TCP 102, optimize edilmemiş DB okuma. Saha testi.",
    "opc-ua":"Güvenilir SignAndEncrypt oturumu ile salt-okunur NodeId taraması. Saha testi.",
    mqtt:"Harici broker'dan salt-okunur topic aboneliği; sayısal/BOOL UTF-8 veri. Saha testi."
  };

  grid.innerHTML = state.protocols.map(p => `
    <button class="protocol-card ${p.enabled ? "enabled" : "disabled"}"
      ${p.enabled ? `data-protocol="${escapeHtml(p.id)}"` : "disabled"}>
      <div class="protocol-card-top">
        <h3>${escapeHtml(p.name)}</h3>
        <span class="availability">${
          escapeHtml(
            state.language === "tr"
              ? (p.availability === "Available" ? "Kullanılabilir" : "Planlandı")
              : p.availability)
        }</span>
      </div>
      <p>${
        escapeHtml(
          state.language === "tr"
            ? (trDescriptions[p.id] || p.description)
            : p.description)
      }</p>
      <span class="availability">${escapeHtml(p.note || "")}</span>
    </button>
  `).join("");

  grid.querySelectorAll(".protocol-card.enabled").forEach(card =>
    card.addEventListener(
      "click",
      () => selectProtocol(card.dataset.protocol)));
}

function selectProtocol(protocolId) {
  state.selectedProtocol = protocolId;
  state.modbusTestPassed = false;
  state.opcUaCertificate = null;
  state.opcUaCertificateInput = "";
  $("opcUaCertificateResult").classList.add("hidden");

  const protocol =
    state.protocols.find(x => x.id === protocolId);

  $("selectedProtocolName").textContent = protocol?.name || "—";
  $("selectedProtocolLogo").textContent =
    protocolId === "modbus-tcp" ? "MB" : protocolId === "siemens-s7-tcp" ? "S7" :
    protocolId === "opc-ua" ? "UA" : protocolId === "mqtt" ? "MQ" : "M";

  $("s7Fields").classList.toggle("hidden", protocolId !== "siemens-s7-tcp");
  $("mqttFields").classList.toggle("hidden", protocolId !== "mqtt");
  $("opcUaFields").classList.toggle("hidden", protocolId !== "opc-ua");
  $("testS7Connection").classList.toggle("hidden", protocolId !== "siemens-s7-tcp");
  $("mockFields").classList.toggle(
    "hidden",
    protocolId !== "mock");

  $("modbusFields").classList.toggle(
    "hidden",
    protocolId !== "modbus-tcp");

  $("pingModbusHost").classList.toggle(
    "hidden",
    protocolId !== "modbus-tcp");

  $("testModbusConnection").classList.toggle(
    "hidden",
    protocolId !== "modbus-tcp");

  $("modbusTestResult").classList.add("hidden");
  $("modbusTestResult").classList.remove("success","failure");

  showWizardStep(2);
  setTimeout(() => $("deviceNameInput").focus(), 30);
}

async function loadDevices() {
  state.devices = await api("/api/devices");
  renderDevices();
  updateOverview();
  populateTagDeviceSelect();
  applyAccessMode();
}

function syncTableFilter(select, entries, allLabel) {
  const current = select.value;
  select.innerHTML = `<option value="">${escapeHtml(allLabel)}</option>` +
    entries.map(([value,label]) => `<option value="${escapeHtml(value)}">${escapeHtml(label)}</option>`).join("");
  select.value = current;
  if (select.value !== current) select.value = "";
}

function tablePage(rows, requestedPage, pageSize) {
  const size = Number.isInteger(pageSize) && pageSize > 0 ? pageSize : 25;
  const pages = Math.max(1, Math.ceil(rows.length / size));
  const page = Math.min(Math.max(1, requestedPage), pages);
  return {items:rows.slice((page - 1) * size, page * size),page,pages};
}

function renderTablePager(prefix, result, hasData) {
  $(`${prefix}Pager`).classList.toggle("hidden", !hasData);
  $(`${prefix}PageInfo`).textContent = `${result.page} / ${result.pages}`;
  $(`${prefix}PrevPage`).disabled = result.page <= 1;
  $(`${prefix}NextPage`).disabled = result.page >= result.pages;
  $(`${prefix}RowsLabel`).textContent = state.language === "tr" ? "Satır" : "Rows";
}

const bulkSelections = new Map();
function renderBulkSelection(kind, tbody, rows, idOf, deleteOne, reload, deleteMany=null) {
  const id = `${kind}BulkToolbar`;
  let bar = document.getElementById(id);
  if (!bar) {
    bar = document.createElement('div'); bar.id=id; bar.className='bulk-selection-toolbar';
    tbody.closest('.table-wrap')?.before(bar);
  }
  const ids = rows.map(idOf).filter(Boolean).map(String);
  bar.classList.toggle('hidden',ids.length===0);
  const selected = bulkSelections.get(kind) || new Set();
  bulkSelections.set(kind, selected);
  for (const stale of [...selected]) if (!ids.includes(stale)) selected.delete(stale);
  const count=selected.size, all=ids.length>0&&ids.every(x=>selected.has(x));
  bar.innerHTML=`<label><input type="checkbox" data-bulk-page="${kind}" ${all?'checked':''} ${ids.length?'':'disabled'}><span>${state.language==='tr'?'Bu sayfadakilerin tümünü seç':'Select all on this page'}</span></label><span class="bulk-selection-count">${state.language==='tr'?`${count} seçili`:`${count} selected`}</span><button type="button" class="delete-button" data-bulk-delete="${kind}" ${count?'':'disabled'}>${state.language==='tr'?'Seçilenleri sil':'Delete selected'}</button>`;
  tbody.querySelectorAll('[data-bulk-check]').forEach(x=>x.checked=selected.has(x.value));
  bar.querySelector('[data-bulk-page]').onchange=e=>{
    if(e.target.checked)ids.forEach(x=>selected.add(x));else ids.forEach(x=>selected.delete(x));
    renderBulkSelection(kind,tbody,rows,idOf,deleteOne,reload,deleteMany);
  };
  tbody.querySelectorAll('[data-bulk-check]').forEach(x=>x.onchange=()=>{
    x.checked?selected.add(x.value):selected.delete(x.value);
    renderBulkSelection(kind,tbody,rows,idOf,deleteOne,reload,deleteMany);
  });
  bar.querySelector('[data-bulk-delete]').onclick=async e=>{
    const chosen=ids.filter(x=>selected.has(x)); if(!chosen.length)return;
    if(!requireConfigurationAccess())return;
    const warning=kind==='historian'?(state.language==='tr'?'Seçilen Historian kayıt ayarları ve bunlara ait kayıtlı örnekler kalıcı olarak silinecek.':'Selected Historian configurations and their recorded samples will be permanently deleted.'):
      state.language==='tr'?`${chosen.length} kayıt ve ilişkili veriler silinsin mi?`:`Delete ${chosen.length} selected records and related data?`;
    if(!confirm(warning))return;
    e.currentTarget.disabled=true;
    try {
      if(deleteMany) await deleteMany(chosen);
      else await Promise.all(chosen.map(x=>deleteOne(x)));
      selected.clear(); await reload();
      showToast(state.language==='tr'?`${chosen.length} kayıt silindi.`:`Deleted ${chosen.length} records.`);
    } catch(error) { showToast(error.message); }
  };
}

function renderDevices() {
  syncTableFilter($("deviceStatusFilter"),
    [...new Set(state.devices.map(d => d.status).filter(Boolean))].sort().map(status => [status,status]),
    state.language === "tr" ? "Tüm durumlar" : "All statuses");
  $("deviceProtocolFilter").options[0].textContent = state.language === "tr" ? "Tüm protokoller" : "All protocols";
  const query =
    $("deviceSearch").value.trim().toLowerCase();
  const protocol = $("deviceProtocolFilter").value;
  const status = $("deviceStatusFilter").value;

  const filtered =
    state.devices.filter(d =>
      (!protocol || d.protocol === protocol) &&
      (!status || d.status === status) &&
      (d.name.toLowerCase().includes(query) ||
       d.protocol.toLowerCase().includes(query) ||
       (d.host || "").toLowerCase().includes(query)));

  const compare = (a,b) => a.localeCompare(b, state.language === "tr" ? "tr" : "en", {numeric:true,sensitivity:"base"});
  const sort = $("deviceSort").value;
  filtered.sort((a,b) => sort === "name-desc" ? compare(b.name,a.name) :
    sort === "protocol" ? compare(a.protocol,b.protocol) || compare(a.name,b.name) :
    sort === "created-desc" ? Date.parse(b.createdAt || 0) - Date.parse(a.createdAt || 0) :
    sort === "created-asc" ? Date.parse(a.createdAt || 0) - Date.parse(b.createdAt || 0) :
    compare(a.name,b.name));
  const pageResult = tablePage(filtered, state.devicePage, Number($("devicePageSize").value));
  state.devicePage = pageResult.page;

  $("navDeviceCount").textContent = state.devices.length;

  $("deviceTableMeta").textContent =
    state.language === "tr"
      ? `${filtered.length} / ${state.devices.length} cihaz`
      : `${filtered.length} / ${state.devices.length} devices`;

  const has = state.devices.length > 0;

  $("deviceEmpty").classList.toggle("hidden", has);
  $("deviceTableWrap").classList.toggle("hidden", !has);
  renderTablePager("device", pageResult, has);

  $("deviceTableBody").innerHTML =
    pageResult.items.map(device => `
      <tr>
        <td><input type="checkbox" data-bulk-check value="${device.id}" aria-label="Select ${escapeHtml(device.name)}"></td>
        <td><span class="device-name">${escapeHtml(device.name)}</span></td>
        <td><span class="protocol-badge">${escapeHtml(device.protocol)}</span></td>
        <td><span class="status-badge ${statusClass(device.status)}">${escapeHtml(device.status)}</span></td>
        <td>${device.host ? `${escapeHtml(device.host)}:${device.port}` : "—"}</td>
        <td>${device.pollIntervalMs} ms</td>
        <td>${new Date(device.createdAt).toLocaleString(state.language === "tr" ? "tr-TR" : "en-US")}</td>
        <td class="actions-cell">
          ${device.host ? `
            <button class="ping-button"
              data-ping-device="${device.id}"
              title="ICMP diagnostic only">
              ${t("ping")}
            </button>
          ` : ""}
          <button class="edit-button"
            data-edit-device="${device.id}">
            ${t("edit")}
          </button>
          <button class="delete-button"
            data-delete-device="${device.id}">
            ${t("delete")}
          </button>
        </td>
      </tr>
    `).join("") || `<tr><td colspan="7">${state.language === "tr" ? "Filtreye uyan cihaz yok." : "No devices match the filters."}</td></tr>`;
  document.querySelectorAll("[data-ping-device]")
    .forEach(button =>
      button.addEventListener("click", async () => {
        const id = button.dataset.pingDevice;
        const original = button.textContent;

        button.disabled = true;
        button.classList.remove("ping-ok", "ping-fail");
        button.textContent = "...";

        try {
          const result = await api(
            `/api/devices/${id}/ping`,
            { method:"POST" });

          button.classList.add(
            result.success ? "ping-ok" : "ping-fail");

          button.textContent =
            result.success
              ? `${t("ping")} ${result.roundtripTimeMs} ms`
              : `${t("ping")} ✕`;

          button.title = result.message;
        }
        catch(error) {
          button.classList.add("ping-fail");
          button.textContent = `${t("ping")} ✕`;
          button.title = error.message;
        }
        finally {
          button.disabled = false;
          setTimeout(() => {
            if (document.body.contains(button)) {
              button.textContent = original;
              button.classList.remove("ping-ok", "ping-fail");
            }
          }, 5000);
        }
      }));

  renderBulkSelection('devices',$("deviceTableBody"),pageResult.items,x=>x.id,x=>api(`/api/devices/${x}`,{method:'DELETE'}),async()=>{await Promise.all([loadDevices(),loadTags(),loadAlarms(),loadHistorianStats()])});

  document.querySelectorAll("[data-edit-device]")
    .forEach(button =>
      button.addEventListener(
        "click",
        () => openEditDeviceModal(button.dataset.editDevice)));

  document.querySelectorAll("[data-delete-device]")
    .forEach(button =>
      button.addEventListener("click", async () => {
        const device =
          state.devices.find(
            x => x.id === button.dataset.deleteDevice);

        const question =
          state.language === "tr"
            ? `"${device?.name}" silinsin mi?`
            : `Delete "${device?.name}"?`;

        if (!confirm(question))
          return;

        await api(
          `/api/devices/${button.dataset.deleteDevice}`,
          { method:"DELETE" });

        showToast(t("deviceDeleted"));

        await Promise.all([
          loadDevices(),
          loadTags()
        ]);
      }));
}

async function loadTags() {
  state.tags = await api("/api/tags");
  await loadTagValues();
  try { state.licenseUsage = await api("/api/license/usage"); } catch {}
  renderTags();
  renderLicense();
  updateOverview();
  applyAccessMode();
}

async function loadTagValues() {
  const values = await api("/api/tags/values");

  state.tagValues =
    new Map(values.map(x => [x.tagId, x]));
}

function tagDeviceName(id) {
  return state.devices.find(x => x.id === id)?.name || "—";
}

function tagAddressText(tag) {
  if (state.devices.find(x => x.id === tag.deviceId)?.protocol !== "Modbus TCP")
    return tag.address;
  const numeric = Number(tag.address);
  const area = modbusAddressInfo(tag.address);

  if (tag.dataType === "Bool")
    return area?.bitArea ? tag.address : `${tag.address}.${tag.bitIndex ?? 0}`;

  if (["UInt32","Int32","Float32"].includes(tag.dataType))
    return `${tag.address}–${numeric + 1}`;

  return tag.address;
}

function tagDatatypeText(tag) {
  if (state.devices.find(x => x.id === tag.deviceId)?.protocol === "Modbus TCP" &&
      ["UInt32","Int32","Float32"].includes(tag.dataType))
    return `${tag.dataType} / ${tag.byteOrder}`;

  return tag.dataType;
}

function tagValueText(tag, snapshot) {
  if (!snapshot ||
      snapshot.value === null ||
      snapshot.value === undefined)
  {
    return "—";
  }

  if (tag.dataType === "Bool")
  {
    const isTrue = Number(snapshot.value) !== 0;

    if (state.language === "tr")
      return isTrue ? "TRUE" : "FALSE";

    return isTrue ? "TRUE" : "FALSE";
  }

  // WORD is a raw 16-bit bitfield, not a localized measurement.
  if (tag.dataType === "Word")
    return String(Number(snapshot.value));

  const decimals =
    Math.max(
      0,
      Math.min(6, Number(tag.decimalPlaces ?? 0)));

  return Number(snapshot.value)
    .toLocaleString(
      state.language === "tr" ? "tr-TR" : "en-US",
      {
        minimumFractionDigits: decimals,
        maximumFractionDigits: decimals
      });
}

function renderTags() {
  syncTableFilter($("tagDeviceFilter"),
    state.devices.filter(d => state.tags.some(tag => tag.deviceId === d.id))
      .sort((a,b) => a.name.localeCompare(b.name))
      .map(d => [d.id,d.name]),
    state.language === "tr" ? "Tüm cihazlar" : "All devices");
  $("tagTypeFilter").options[0].textContent = state.language === "tr" ? "Tüm veri tipleri" : "All datatypes";
  const query =
    $("tagSearch").value.trim().toLowerCase();
  const deviceId = $("tagDeviceFilter").value;
  const dataType = $("tagTypeFilter").value;

  const filtered =
    state.tags.filter(tag =>
      (!deviceId || tag.deviceId === deviceId) &&
      (!dataType || tag.dataType === dataType) &&
      (tag.name.toLowerCase().includes(query) ||
       tag.address.toLowerCase().includes(query) ||
       tagDeviceName(tag.deviceId).toLowerCase().includes(query)));

  const compare = (a,b) => a.localeCompare(b, state.language === "tr" ? "tr" : "en", {numeric:true,sensitivity:"base"});
  const sort = $("tagSort").value;
  filtered.sort((a,b) => sort === "name-desc" ? compare(b.name,a.name) :
    sort === "device" ? compare(tagDeviceName(a.deviceId),tagDeviceName(b.deviceId)) || compare(a.name,b.name) :
    sort === "address" ? compare(a.address,b.address) || compare(a.name,b.name) :
    compare(a.name,b.name));
  const pageResult = tablePage(filtered, state.tagPage, Number($("tagPageSize").value));
  state.tagPage = pageResult.page;

  $("navTagCount").textContent = state.tags.length;

  $("tagTableMeta").textContent =
    state.language === "tr"
      ? `${filtered.length} / ${state.tags.length} tag`
      : `${filtered.length} / ${state.tags.length} tags`;

  const usage = state.licenseUsage || {};
  if (usage.visible === false) {
    if ($("tagCapacityUsed")) $("tagCapacityUsed").textContent = state.language === "tr" ? "Giriş yapın" : "Sign in";
    if ($("tagCapacityRemaining")) $("tagCapacityRemaining").textContent = state.language === "tr" ? "Kapasite girişten sonra görünür" : "Capacity visible after sign-in";
    $("tagCapacityInline")?.classList.remove("over-capacity");
  }
  const usedTags = Number.isFinite(Number(usage.usedTags)) ? Number(usage.usedTags) : state.tags.length;
  const maxTags = usage.maxTags == null ? null : Number(usage.maxTags);
  const remainingTags = usage.remainingTags == null ? null : Number(usage.remainingTags);
  if (usage.visible !== false) {
    if ($("tagCapacityUsed")) {
      $("tagCapacityUsed").textContent = usage.unlimited
        ? `${usedTags} / ∞`
        : maxTags != null && Number.isFinite(maxTags)
          ? `${usedTags} / ${maxTags}`
          : `${usedTags} / —`;
    }
    if ($("tagCapacityRemaining")) {
      $("tagCapacityRemaining").textContent = usage.unlimited
        ? (state.language === "tr" ? "Kalan: sınırsız" : "Remaining: unlimited")
        : remainingTags != null && Number.isFinite(remainingTags)
          ? `${state.language === "tr" ? "Kalan" : "Remaining"}: ${remainingTags}`
          : `${state.language === "tr" ? "Kalan" : "Remaining"}: —`;
    }
    $("tagCapacityInline")?.classList.toggle("over-capacity", Boolean(usage.overCapacity));
  }

  const has = state.tags.length > 0;

  $("tagEmpty").classList.toggle("hidden", has);
  $("tagTableWrap").classList.toggle("hidden", !has);
  renderTablePager("tag", pageResult, has);

  $("tagTableBody").innerHTML =
    pageResult.items.map(tag => {
      const snapshot =
        state.tagValues.get(tag.id);

      const quality =
        snapshot?.quality || "Waiting";

      const qualityClass =
        quality.toLowerCase() === "good"
          ? "quality-good"
          : quality.toLowerCase() === "bad"
            ? "quality-bad"
            : "quality-waiting";

      const updated =
        snapshot?.timestamp
          ? new Date(snapshot.timestamp)
              .toLocaleTimeString(
                state.language === "tr"
                  ? "tr-TR"
                  : "en-US")
          : "—";

      return `
        <tr title="${escapeHtml(snapshot?.error || "")}">
          <td><input type="checkbox" data-bulk-check value="${tag.id}" aria-label="Select ${escapeHtml(tag.name)}"></td>
          <td>
            <span class="signal-name">${escapeHtml(tag.name)}</span>
          </td>
          <td>${escapeHtml(tagDeviceName(tag.deviceId))}</td>
          <td>
            <span class="protocol-badge">${escapeHtml(tagAddressText(tag))}</span>
          </td>
          <td>${escapeHtml(tagDatatypeText(tag))}</td>
          <td>
            <span class="live-value">${escapeHtml(tagValueText(tag, snapshot))}</span>
            ${tag.unit ? `<span class="live-unit">${escapeHtml(tag.unit)}</span>` : ""}
          </td>
          <td>
            <span class="quality-badge ${qualityClass}">
              ${escapeHtml(quality.toUpperCase())}
            </span>
          </td>
          <td>${updated}</td>
          <td class="actions-cell">
            <button class="edit-button"
              data-edit-tag="${tag.id}">
              ${t("edit")}
            </button>
            <button class="delete-button"
              data-delete-tag="${tag.id}">
              ${t("delete")}
            </button>
          </td>
        </tr>
      `;
    }).join("") || `<tr><td colspan="8">${state.language === "tr" ? "Filtreye uyan Tag yok." : "No Tags match the filters."}</td></tr>`;

  renderBulkSelection('tags',$("tagTableBody"),pageResult.items,x=>x.id,x=>api(`/api/tags/${x}`,{method:'DELETE'}),async()=>{await loadTags();await loadAlarms();await loadHistorianStats()});
  document.querySelectorAll("[data-edit-tag]")
    .forEach(button =>
      button.addEventListener(
        "click",
        () => openEditTagModal(button.dataset.editTag)));

  document.querySelectorAll("[data-delete-tag]")
    .forEach(button =>
      button.addEventListener("click", async () => {
        if (!requireConfigurationAccess()) return;
        const tag =
          state.tags.find(
            x => x.id === button.dataset.deleteTag);

        const question =
          state.language === "tr"
            ? `"${tag?.name}" tagı silinsin mi?`
            : `Delete tag "${tag?.name}"?`;

        if (!confirm(question))
          return;

        await api(
          `/api/tags/${button.dataset.deleteTag}`,
          { method:"DELETE" });

        showToast(t("tagDeleted"));
        await loadTags();
      }));
  // Table paging/filtering recreates action buttons. Reapply the current session
  // gate after every render, not only after the initial API load.
  applyAccessMode();
}

function populateTagDeviceSelect() {
  const select = $("tagDevice");

  const devices =
    state.devices.filter(
      x => ["Modbus TCP", "Siemens S7 TCP", "MQTT", "OPC UA"].includes(x.protocol));

  select.innerHTML =
    devices.length
      ? devices
          .map(x =>
            `<option value="${x.id}">
              ${escapeHtml(x.name)} — ${escapeHtml(x.host)}:${x.port}
            </option>`)
          .join("")
      : `<option value="">
          ${
            state.language === "tr"
              ? "Önce desteklenen bir cihaz ekleyin"
              : "Add a supported device first"
          }
        </option>`;
}

function openDeviceModal() {
  if (!requireConfigurationAccess()) return;
  state.editingDeviceId = null;
  state.editingDeviceOriginal = null;
  state.selectedProtocol = null;
  state.modbusTestPassed = false;
  state.opcUaCertificate = null;
  state.opcUaCertificateInput = "";
  $("opcUaCertificateResult").classList.add("hidden");

  $("deviceModal").classList.remove("hidden");
  $("deviceNameInput").value = "";
  document.querySelector("#deviceModal .modal-head h2").textContent = t("addDevice");
  $("wizardBack").classList.remove("hidden");
  showWizardStep(1);
}

function openEditDeviceModal(id) {
  if (!requireConfigurationAccess()) return;
  const device = state.devices.find(x => x.id === id);
  if (!device) return;

  state.editingDeviceId = id;
  state.editingDeviceOriginal = {...device};
  state.selectedProtocol = device.protocol === "Modbus TCP" ? "modbus-tcp" :
    device.protocol === "Siemens S7 TCP" ? "siemens-s7-tcp" :
    device.protocol === "MQTT" ? "mqtt" : device.protocol === "OPC UA" ? "opc-ua" : "mock";
  state.modbusTestPassed = true;
  state.opcUaCertificate = null;
  state.opcUaCertificateInput = "";
  $("opcUaCertificateResult").classList.add("hidden");

  $("deviceModal").classList.remove("hidden");
  document.querySelector("#deviceModal .modal-head h2").textContent = t("editDevice");
  $("wizardBack").classList.add("hidden");

  const protocol = state.protocols.find(x => x.id === state.selectedProtocol);
  $("selectedProtocolName").textContent = protocol?.name || device.protocol;
  $("selectedProtocolLogo").textContent = state.selectedProtocol === "modbus-tcp" ? "MB" :
    state.selectedProtocol === "siemens-s7-tcp" ? "S7" :
    state.selectedProtocol === "opc-ua" ? "UA" : state.selectedProtocol === "mqtt" ? "MQ" : "M";
  $("deviceNameInput").value = device.name;

  $("s7Fields").classList.toggle("hidden", state.selectedProtocol !== "siemens-s7-tcp");
  $("mqttFields").classList.toggle("hidden", state.selectedProtocol !== "mqtt");
  $("opcUaFields").classList.toggle("hidden", state.selectedProtocol !== "opc-ua");
  $("testS7Connection").classList.toggle("hidden", state.selectedProtocol !== "siemens-s7-tcp");
  $("mockFields").classList.toggle("hidden", state.selectedProtocol !== "mock");
  $("modbusFields").classList.toggle("hidden", state.selectedProtocol !== "modbus-tcp");
  $("pingModbusHost").classList.toggle("hidden", state.selectedProtocol !== "modbus-tcp");
  $("testModbusConnection").classList.toggle("hidden", state.selectedProtocol !== "modbus-tcp");

  if (state.selectedProtocol === "modbus-tcp") {
    $("modbusHost").value = device.host || "127.0.0.1";
    $("modbusPort").value = device.port ?? 502;
    $("modbusUnitId").value = device.unitId ?? 1;
    $("modbusPoll").value = device.pollIntervalMs ?? 1000;
  }

  if (state.selectedProtocol === "siemens-s7-tcp") {
    $("s7Host").value = device.host || ""; $("s7Port").value = device.port ?? 102;
    $("s7Rack").value = Math.floor((device.unitId ?? 1)/32); $("s7Slot").value = (device.unitId ?? 1)%32;
    $("s7Poll").value = device.pollIntervalMs ?? 1000;
  }
  if (state.selectedProtocol === "mqtt") {
    $("mqttHost").value = device.host || "mqtts://";
    $("mqttPort").value = device.port ?? 8883;
    $("mqttPoll").value = device.pollIntervalMs ?? 1000;
  }
  if (state.selectedProtocol === "opc-ua") {
    $("opcUaUrl").value = device.host || "";
    $("opcUaPoll").value = device.pollIntervalMs ?? 1000;
  }
  $("s7TestResult").classList.add("hidden");
  $("modbusPingResult").classList.add("hidden");
  $("modbusPingResult").classList.add("hidden");
  $("modbusTestResult").classList.add("hidden");
  showWizardStep(2);
}

function deviceCommunicationChanged() {
  if (!state.editingDeviceOriginal || (state.selectedProtocol !== "modbus-tcp" && state.selectedProtocol !== "siemens-s7-tcp"))
    return false;

  const d = state.editingDeviceOriginal;
  if (state.selectedProtocol === "siemens-s7-tcp") return $("s7Host").value.trim() !== (d.host || "") || readNumber("s7Port") !== Number(d.port) || (readNumber("s7Rack")*32+readNumber("s7Slot")) !== Number(d.unitId);

  return (
    $("modbusHost").value.trim() !== (d.host || "") ||
    readNumber("modbusPort") !== Number(d.port) ||
    readNumber("modbusUnitId") !== Number(d.unitId)
  );
}

function closeDeviceModal() {
  $("deviceModal").classList.add("hidden");
}

function showWizardStep(step) {
  const first = step === 1;

  $("wizardStep1").classList.toggle("hidden", !first);
  $("wizardStep2").classList.toggle("hidden", first);
  $("wizardStep1Label").classList.toggle("active", first);
  $("wizardStep1Label").classList.toggle("completed", !first);
  $("wizardStep2Label").classList.toggle("active", !first);
  $("wizardStep2Label").classList.toggle("completed", false);

  const shown = first ? $("wizardStep1") : $("wizardStep2");
  shown.classList.remove("wizard-pane-animate");
  void shown.offsetWidth;
  shown.classList.add("wizard-pane-animate");
}


const readNumber = id => Number($(id).value);

async function inspectOpcUaCertificate() {
  const endpointUrl = $("opcUaUrl").value.trim();
  const box = $("opcUaCertificateResult");
  state.opcUaCertificate = null;
  box.classList.remove("hidden", "success", "failure");
  box.textContent = state.language === "tr" ? "Sunucu sertifikası okunuyor..." : "Reading server certificate...";
  try {
    const info = await api("/api/devices/opc-ua/certificate/inspect", {
      method:"POST",body:JSON.stringify({endpointUrl})});
    if(endpointUrl !== $("opcUaUrl").value.trim())return;
    state.opcUaCertificate = info;
    state.opcUaCertificateInput = endpointUrl;
    renderOpcUaCertificate();
  } catch(error) {
    box.classList.add("failure");
    box.textContent = error.message;
  }
}

function renderOpcUaCertificate() {
  const info = state.opcUaCertificate;
  if(!info)return;
  const tr = state.language === "tr";
  const box = $("opcUaCertificateResult");
  box.classList.remove("hidden", "success", "failure");
  box.classList.add(info.trusted ? "success" : "failure");
  box.innerHTML = `<strong>${info.trusted ? (tr ? "Sertifika güvenilir" : "Certificate trusted") :
    (tr ? "Sertifika onay bekliyor" : "Certificate awaiting approval")}</strong><br>
    ${escapeHtml(info.subject)}<br>SHA-256: <code>${escapeHtml(info.sha256)}</code><br>
    SHA-1: <code>${escapeHtml(info.sha1)}</code><br>
    ${tr ? "Geçerlilik" : "Valid"}: ${escapeHtml(new Date(info.validFrom).toLocaleString())} – ${escapeHtml(new Date(info.validUntil).toLocaleString())}
    ${info.trusted ? "" : `<br><label><input type="checkbox" id="opcUaFingerprintVerified"> ${tr ?
      "Parmak izini PLC/TIA sertifikasıyla bağımsız karşılaştırdım" :
      "I independently matched this fingerprint with the PLC/TIA certificate"}</label>
      <button type="button" class="secondary" id="approveOpcUaCertificate" disabled>${tr ? "Sertifikayı onayla" : "Trust certificate"}</button>`}`;
  if(!info.trusted){
    $("opcUaFingerprintVerified").addEventListener("change",()=>{
      $("approveOpcUaCertificate").disabled = !$("opcUaFingerprintVerified").checked;
    });
    $("approveOpcUaCertificate").addEventListener("click",approveOpcUaCertificate);
  }
}

async function approveOpcUaCertificate() {
  if(!requireConfigurationAccess() || !$("opcUaFingerprintVerified").checked)return;
  const info = state.opcUaCertificate;
  const endpointUrl = $("opcUaUrl").value.trim();
  if(!info || state.opcUaCertificateInput !== endpointUrl)return;
  try {
    const trusted = await api("/api/devices/opc-ua/certificate/trust", {
      method:"POST",body:JSON.stringify({endpointUrl,sha256:info.sha256})});
    if(endpointUrl !== $("opcUaUrl").value.trim())return;
    state.opcUaCertificate = trusted;
    renderOpcUaCertificate();
  } catch(error) { showToast(error.message); }
}


async function pingModbusHost() {
  const box = $("modbusPingResult");

  box.classList.remove("hidden", "success", "failure");
  box.textContent =
    state.language === "tr"
      ? "Ping gönderiliyor..."
      : "Pinging...";

  try {
    const result = await api(
      "/api/network/ping",
      {
        method:"POST",
        body:JSON.stringify({
          host:$("modbusHost").value.trim(),
          timeoutMs:1200
        })
      });

    box.classList.add(
      result.success ? "success" : "failure");

    box.innerHTML =
      result.success
        ? `<strong>${t("pingOk")}</strong><br>${escapeHtml(result.address || result.host)} • ${result.roundtripTimeMs} ms`
        : `<strong>${t("pingFail")}</strong><br>${escapeHtml(result.message)}`;
  }
  catch(error) {
    box.classList.add("failure");
    box.textContent = error.message;
  }
}

async function testModbus() {
  const box = $("modbusTestResult");

  state.modbusTestPassed = false;

  box.classList.remove(
    "hidden",
    "success",
    "failure");

  box.textContent =
    state.language === "tr"
      ? "Test ediliyor..."
      : "Testing...";

  try {
    const result =
      await api(
        "/api/devices/modbus-tcp/test",
        {
          method:"POST",
          body:JSON.stringify({
            host:$("modbusHost").value.trim(),
            port:readNumber("modbusPort"),
            unitId:readNumber("modbusUnitId"),
            timeoutMs:readNumber("modbusTimeout")
          })
        });

    state.modbusTestPassed = result.success;

    box.classList.add(
      result.success
        ? "success"
        : "failure");

    box.innerHTML =
      result.success
        ? `<strong>${t("testSuccess")}</strong><br>
           TCP + Modbus FC03 • ${result.responseTimeMs} ms<br>
           40001 = <strong>${result.holdingRegister40001}</strong>`
        : `<strong>${t("testFailed")}</strong><br>
           ${escapeHtml(result.message)}`;
  }
  catch (error) {
    box.classList.add("failure");
    box.textContent = error.message;
  }
}

async function saveDevice() {
  if (!requireConfigurationAccess()) return;
  try {
    const name = $("deviceNameInput").value.trim();

    if (!name)
      throw new Error(state.language === "tr" ? "Cihaz adı gerekli." : "Device name is required.");

    if (state.selectedProtocol === "opc-ua") {
      const endpointUrl = $("opcUaUrl").value.trim();
      if (state.opcUaCertificateInput !== endpointUrl || !state.opcUaCertificate?.trusted)
        await inspectOpcUaCertificate();
      if (!state.opcUaCertificate?.trusted || state.opcUaCertificateInput !== endpointUrl)
        throw new Error(state.language === "tr" ?
          "Önce PLC sertifikasının parmak izini doğrulayıp onaylayın." :
          "Verify and trust the PLC certificate before saving.");
    }

    if (state.editingDeviceId) {
      if (state.selectedProtocol === "siemens-s7-tcp") {
        await api(`/api/devices/${state.editingDeviceId}`, {method:"PUT",body:JSON.stringify({name,host:$("s7Host").value.trim(),port:readNumber("s7Port"),unitId:readNumber("s7Rack")*32+readNumber("s7Slot"),pollIntervalMs:readNumber("s7Poll")})});
      }
      else if (state.selectedProtocol === "modbus-tcp") {
        await api(
          `/api/devices/${state.editingDeviceId}`,
          {
            method:"PUT",
            body:JSON.stringify({
              name,
              host:$("modbusHost").value.trim(),
              port:readNumber("modbusPort"),
              unitId:readNumber("modbusUnitId"),
              pollIntervalMs:readNumber("modbusPoll")
            })
          });
      }
      else if (state.selectedProtocol === "mqtt") {
        await api(`/api/devices/${state.editingDeviceId}`, {method:"PUT",body:JSON.stringify({
          name, host:$("mqttHost").value.trim(), port:readNumber("mqttPort"),
          pollIntervalMs:readNumber("mqttPoll")
        })});
      }
      else if (state.selectedProtocol === "opc-ua") {
        await api(`/api/devices/${state.editingDeviceId}`, {method:"PUT",body:JSON.stringify({
          name, host:$("opcUaUrl").value.trim(), pollIntervalMs:readNumber("opcUaPoll")
        })});
      }
      else {
        await api(
          `/api/devices/${state.editingDeviceId}`,
          {
            method:"PUT",
            body:JSON.stringify({ name })
          });
      }

      closeDeviceModal();
      await loadDevices();
      navigate("devices");
      showToast(state.language === "tr" ? "Cihaz güncellendi." : "Device updated.");
      return;
    }

    if (state.selectedProtocol === "mock") {
      await api(
        "/api/devices/mock",
        {
          method:"POST",
          body:JSON.stringify({name})
        });
    }
    else if (state.selectedProtocol === "siemens-s7-tcp") {
      await api("/api/devices/siemens-s7-tcp",{method:"POST",body:JSON.stringify({name,host:$("s7Host").value.trim(),port:readNumber("s7Port"),rack:readNumber("s7Rack"),slot:readNumber("s7Slot"),pollIntervalMs:readNumber("s7Poll")})});
    }
    else if (state.selectedProtocol === "modbus-tcp") {
      await api(
        "/api/devices/modbus-tcp",
        {
          method:"POST",
          body:JSON.stringify({
            name,
            host:$("modbusHost").value.trim(),
            port:readNumber("modbusPort"),
            unitId:readNumber("modbusUnitId"),
            pollIntervalMs:readNumber("modbusPoll")
          })
        });
    }
    else if (state.selectedProtocol === "mqtt") {
      await api("/api/devices/mqtt", {method:"POST",body:JSON.stringify({
        name, host:$("mqttHost").value.trim(), port:readNumber("mqttPort"),
        pollIntervalMs:readNumber("mqttPoll")
      })});
    }
    else if (state.selectedProtocol === "opc-ua") {
      await api("/api/devices/opc-ua", {method:"POST",body:JSON.stringify({
        name, endpointUrl:$("opcUaUrl").value.trim(), pollIntervalMs:readNumber("opcUaPoll")
      })});
    }
    else {
      throw new Error(state.language === "tr" ? "Bir protokol seçin." : "Select a protocol.");
    }

    closeDeviceModal();
    await loadDevices();
    navigate("devices");
    showToast(t("deviceSaved"));
    continueCommissioningIfActive();
  }
  catch(error) {
    showToast(error.message);
  }
}

function resetTagForm() {
  state.editingTagId = null;

  populateTagDeviceSelect();

  $("tagName").value = "";
  $("tagDataType").value = "UInt16";
  state.tagSuggestedAddress = tagAddressExample();
  $("tagAddress").value = state.tagSuggestedAddress;
  $("tagBitIndex").value = "0";
  $("tagByteOrder").value = "ABCD";
  $("tagUnit").value = "";
  $("tagDecimalPlaces").value = "0";
  $("tagOffset").value = "0";

  updateTagFormVisibility();
  validateTagAddressClient();
}

function openAddTagModal() {
  if (!requireConfigurationAccess()) return;
  const supportedDevices =
    state.devices.filter(
      x => ["Modbus TCP", "Siemens S7 TCP", "MQTT", "OPC UA"].includes(x.protocol));

  if (!supportedDevices.length) {
    showToast(
      state.language === "tr"
        ? "Önce desteklenen bir cihaz ekleyin."
        : "Add a supported device first.");

    navigate("devices");
    return;
  }

  resetTagForm();

  document.querySelector(
    "#tagModal .modal-head h2")
    .textContent = t("addTag");

  $("tagModal").classList.remove("hidden");
  setTimeout(() => $("tagName").focus(), 30);
}

function openEditTagModal(id) {
  if (!requireConfigurationAccess()) return;
  const tag =
    state.tags.find(x => x.id === id);

  if (!tag)
    return;

  state.editingTagId = id;

  populateTagDeviceSelect();

  $("tagDevice").value = tag.deviceId;
  $("tagName").value = tag.name;
  $("tagAddress").value = tag.address;
  $("tagDataType").value = tag.dataType;
  $("tagBitIndex").value = tag.bitIndex ?? 0;
  $("tagByteOrder").value = tag.byteOrder || "ABCD";
  $("tagUnit").value = tag.unit || "";
  $("tagDecimalPlaces").value =
    String(tag.decimalPlaces ?? 0);
  $("tagOffset").value =
    String(tag.offset ?? 0);
  state.tagSuggestedAddress = null;

  updateTagFormVisibility();
  validateTagAddressClient();

  document.querySelector(
    "#tagModal .modal-head h2")
    .textContent = t("editTag");

  $("tagModal").classList.remove("hidden");
}

function closeTagModal() {
  $("tagModal").classList.add("hidden");
}

function tagIsS7() { return state.devices.some(d => d.id === $("tagDevice").value && d.protocol === "Siemens S7 TCP"); }
function tagIsMqtt() { return state.devices.some(d => d.id === $("tagDevice").value && d.protocol === "MQTT"); }
function tagIsOpcUa() { return state.devices.some(d => d.id === $("tagDevice").value && d.protocol === "OPC UA"); }
function tagAddressExample() {
  const type = $("tagDataType").value;
  if (tagIsS7())
    return type === "Bool" ? "DB1.DBX0.0" : ["Word","UInt16","Int16"].includes(type) ? "DB1.DBW2" : "DB1.DBD4";
  if (tagIsMqtt()) return type === "Bool" ? "plant/running" : "plant/temperature";
  if (tagIsOpcUa()) return type === "Bool" ? "ns=2;s=Running" : "ns=2;s=Temperature";
  return nextModbusTagAddress($("tagDevice").value,type);
}
function refreshTagAddressExample() {
  const next = tagAddressExample();
  if (!state.editingTagId && (!$("tagAddress").value || $("tagAddress").value === state.tagSuggestedAddress))
    $("tagAddress").value = next;
  state.tagSuggestedAddress = next;
  updateTagFormVisibility();
  validateTagAddressClient();
}
function s7AddressValid(address, type) {
  const patterns = {Bool:/^DB\d+\.DBX\d+\.[0-7]$/i,Word:/^DB\d+\.DBW\d+$/i,UInt16:/^DB\d+\.DBW\d+$/i,Int16:/^DB\d+\.DBW\d+$/i,UInt32:/^DB\d+\.DBD\d+$/i,Int32:/^DB\d+\.DBD\d+$/i,Float32:/^DB\d+\.DBD\d+$/i};
  if (!patterns[type]?.test(address)) return false;
  const match=/^DB(\d+)\.DB[XWD](\d+)/i.exec(address);
  const length=type==="Bool"?1:["Word","UInt16","Int16"].includes(type)?2:4;
  return !!match && Number(match[1])>=1 && Number(match[1])<=65535 && Number(match[2])>=0 && Number(match[2])+length<=65536;
}
async function testS7Connection() {
  if (!requireConfigurationAccess()) return;
  const box=$("s7TestResult");box.classList.remove("hidden","success","failure");box.textContent="Connecting to Siemens PLC...";
  try {const result=await api("/api/devices/siemens-s7-tcp/test",{method:"POST",body:JSON.stringify({host:$("s7Host").value.trim(),port:readNumber("s7Port"),rack:readNumber("s7Rack"),slot:readNumber("s7Slot"),timeoutMs:readNumber("s7Timeout")})});
    box.classList.add(result.success?"success":"failure");box.textContent=result.message+" ("+result.responseTimeMs+" ms)";
  } catch(e) {box.classList.add("failure");box.textContent=e.message;}
}
function updateTagFormVisibility() {
  if (!$("tagDataType"))
    return;

  const type = $("tagDataType").value;
  const area = modbusAddressInfo($("tagAddress").value);
  const s7 = tagIsS7();
  const modbus = !s7 && !tagIsMqtt() && !tagIsOpcUa();
  const isBool = type === "Bool";
  const isFloat = type === "Float32";
  const isWord = type === "Word";
  const is32 = ["UInt32","Int32","Float32"].includes(type);
  const boolNeedsBit = isBool && area?.area === "Holding Register";

  $("tagAddress").placeholder = tagAddressExample();
  $("tagName").placeholder = s7 ? "Motor Speed" : tagIsMqtt() ? "Temperature" : tagIsOpcUa() ? "Pressure" : "Tank Level PV";
  $("tagDataType").querySelector('option[value="Bool"]').textContent = s7 ? "BOOL (DBX byte.bit)" : tagIsMqtt() ? "BOOL (true/false payload)" : tagIsOpcUa() ? "BOOL (Value)" : "BOOL (Coil / Discrete / Register Bit)";

  $("tagBitField").classList.toggle("hidden", !modbus || !boolNeedsBit);
  $("tagByteOrderField").classList.toggle("hidden", !modbus || !is32);
  $("tagUnitField").classList.toggle("hidden", isBool || isWord);
  $("tagDecimalsField").classList.toggle("hidden", isBool || isWord);
  $("tagOffsetField").classList.toggle("hidden", isBool || isWord);

  $("tagDecimalsLabel").textContent = isFloat ? t("displayDecimals") : t("decimalPoint");
  $("tagDecimalsHelp").textContent = isFloat ? t("displayDecimalsHelp") : t("decimalPointHelp");
  if (tagIsMqtt()) $("tagAddressHelp").textContent = state.language === "tr"
    ? "MQTT adresi tam topic olmalı (örn. plant/temperature). Wildcard yok; UTF-8 metin payload sayısal veya true/false olmalı. Retained değer belirsiz kaliteyle gösterilir."
    : "Use an exact MQTT topic, e.g. plant/temperature. No wildcards; UTF-8 payload must be numeric or true/false. Retained values are marked uncertain.";
  else if (tagIsOpcUa()) $("tagAddressHelp").textContent = state.language === "tr"
    ? "OPC UA NodeId girin: ns=2;i=13 veya TIA'daki tam http://...;i=13 adresi. Yalnızca sayısal ve BOOL Value okunur."
    : "Use ns=2;i=13 or the full Siemens http://...;i=13 NodeId. Only numeric and BOOL Values are read.";
  else if (s7) $("tagAddressHelp").textContent = state.language === "tr"
    ? "S7 mutlak DB adresi kullanın: BOOL için DB1.DBX0.0, WORD/UINT16/INT16 için DB1.DBW2, 32-bit tipler için DB1.DBD4. BOOL biti DBX adresindedir; ayrı BitIndex girilmez."
    : "Use an absolute S7 DB address: DB1.DBX0.0 for BOOL, DB1.DBW2 for WORD/UINT16/INT16, DB1.DBD4 for 32-bit values. The BOOL bit is in the DBX address; do not enter a separate BitIndex.";
  else $("tagAddressHelp").textContent = t("tagAddressHelp");
}

function modbusAddressInfo(address) {
  const numeric = Number(String(address || "").trim());
  if (!Number.isInteger(numeric)) return null;
  if (numeric >= 1 && numeric <= 9999)
    return { area:"Coil", functionCode:1, bitArea:true, first:1, last:9999 };
  if (numeric >= 10001 && numeric <= 19999)
    return { area:"Discrete Input", functionCode:2, bitArea:true, first:10001, last:19999 };
  if (numeric >= 30001 && numeric <= 39999)
    return { area:"Input Register", functionCode:4, bitArea:false, first:30001, last:39999 };
  if (numeric >= 40001 && numeric <= 49999)
    return { area:"Holding Register", functionCode:3, bitArea:false, first:40001, last:49999 };
  return null;
}

function tagRegisterWidth(type) {
  return ["UInt32","Int32","Float32"].includes(type) ? 2 : 1;
}

function nextModbusTagAddress(deviceId,type) {
  const first=type==='Bool'?1:40001,last=type==='Bool'?9999:49999;
  const targetArea=type==='Bool'?'Coil':'Holding Register';
  const width=type==='Bool'?1:tagRegisterWidth(type);
  const occupied=state.tags.filter(t=>t.deviceId===deviceId&&t.id!==state.editingTagId)
    .map(t=>{const area=modbusAddressInfo(t.address);if(area?.area!==targetArea)return null;
      const start=Number(t.address);return {start,end:start+(area.bitArea?1:tagRegisterWidth(t.dataType))-1}})
    .filter(Boolean).sort((a,b)=>a.start-b.start);
  let candidate=first;
  for(const slot of occupied){if(slot.end<candidate)continue;
    if(candidate+width-1<slot.start)break;
    candidate=Math.max(candidate,slot.end+1);
  }
  return candidate+width-1<=last?String(candidate).padStart(5,'0'):'';
}

function describeTagAddress(tagLike) {
  if (/^DB\d+\.DB/i.test(tagLike.address)) return tagLike.address;
  const area = modbusAddressInfo(tagLike.address);
  if (tagLike.dataType === "Bool")
    return area?.bitArea ? `${tagLike.address}` : `${tagLike.address}.${tagLike.bitIndex ?? 0}`;

  const start = Number(tagLike.address);
  const width = tagRegisterWidth(tagLike.dataType);
  return width === 1 ? `${start}` : `${start}-${start + width - 1}`;
}

function findTagAddressConflict(candidate) {
  const candidateArea = modbusAddressInfo(candidate.address);
  if (!candidateArea) return null;

  const start = Number(candidate.address);
  const end = start + (candidateArea.bitArea ? 1 : tagRegisterWidth(candidate.dataType)) - 1;

  for (const existing of state.tags) {
    if (existing.id === state.editingTagId || existing.deviceId !== candidate.deviceId)
      continue;

    const existingArea = modbusAddressInfo(existing.address);
    if (!existingArea || existingArea.area !== candidateArea.area)
      continue;

    const exStart = Number(existing.address);
    const exEnd = exStart + (existingArea.bitArea ? 1 : tagRegisterWidth(existing.dataType)) - 1;
    const overlaps = start <= exEnd && exStart <= end;
    if (!overlaps) continue;

    if (!candidateArea.bitArea &&
        candidate.dataType === "Bool" &&
        existing.dataType === "Bool" &&
        start === exStart &&
        Number(candidate.bitIndex) !== Number(existing.bitIndex)) {
      continue;
    }

    return existing;
  }

  return null;
}

function currentTagDraft() {
  const address = $("tagAddress").value.trim();
  const area = modbusAddressInfo(address);
  return {
    deviceId:$("tagDevice").value,
    address,
    dataType:$("tagDataType").value,
    bitIndex:$("tagDataType").value === "Bool" && area?.area === "Holding Register"
      ? Number($("tagBitIndex").value)
      : null
  };
}

function opcUaNodeAddressValid(address) {
  const value = String(address || "").trim();
  return value.length <= 256 && (
    /^(?:ns=\d+;)?[isgb]=.+$/i.test(value) ||
    /^nsu=.+;[isgb]=.+$/i.test(value) ||
    /^(?:https?:\/\/|urn:).+;[isgb]=.+$/i.test(value));
}

function validateTagAddressClient() {
  const stateBox = $("tagAddressState");
  const saveButton = $("saveTag");
  if (tagIsMqtt() || tagIsOpcUa()) {
    const address = $("tagAddress").value.trim();
    const good = tagIsMqtt()
      ? address.length > 0 && new TextEncoder().encode(address).length <= 512 && !/[+#\x00-\x1f\x7f]/.test(address)
      : opcUaNodeAddressValid(address);
    stateBox.textContent = good ? (tagIsMqtt() ? "MQTT topic OK" : "OPC UA NodeId OK") :
      (tagIsMqtt() ? "Use an exact topic without + or #." : "Use ns=2;i=13 or the Siemens Node ID http://...;i=13.");
    stateBox.classList.toggle("error", !good); saveButton.disabled = !good || !canMutateConfiguration(); return good;
  }
  if (tagIsS7()) {
    const good=s7AddressValid($("tagAddress").value.trim(),$("tagDataType").value);
    stateBox.textContent=good?"S7 absolute DB address OK":"Use DB1.DBX0.0 / DB1.DBW2 / DB1.DBD4 matching datatype.";
    stateBox.classList.toggle("error",!good);saveButton.disabled=!good||!canMutateConfiguration();return good;
  }
  const draft = currentTagDraft();
  const numeric = Number(draft.address);
  const area = modbusAddressInfo(draft.address);
  let message = "";

  if (!area) {
    message = state.language === "tr"
      ? "Adres 00001..09999, 10001..19999, 30001..39999 veya 40001..49999 aralığında olmalı."
      : "Address must be in 00001..09999, 10001..19999, 30001..39999 or 40001..49999.";
  } else if (area.bitArea && draft.dataType !== "Bool") {
    message = state.language === "tr"
      ? `${area.area} yalnızca BOOL veri tipini destekler.`
      : `${area.area} supports BOOL datatype only.`;
  } else if (area.area === "Input Register" && draft.dataType === "Bool") {
    message = state.language === "tr"
      ? "Input Register (3xxxx) BOOL bit desteklemez; WORD veya sayısal veri tipi kullanın."
      : "Input Register (3xxxx) does not support BOOL bit access; use WORD or a numeric datatype.";
  } else if (!area.bitArea && draft.dataType === "Bool" &&
             (!Number.isInteger(draft.bitIndex) || draft.bitIndex < 0 || draft.bitIndex > 15)) {
    message = state.language === "tr"
      ? "Register BOOL için Bit 0..15 gerekli."
      : "Register BOOL requires Bit 0..15.";
  } else {
    const width = area.bitArea ? 1 : tagRegisterWidth(draft.dataType);
    if (numeric + width - 1 > area.last) {
      message = state.language === "tr"
        ? "Seçilen 32-bit veri tipi Modbus alan sınırını aşıyor."
        : "The selected 32-bit datatype exceeds the Modbus area boundary.";
    } else {
      const conflict = findTagAddressConflict(draft);
      if (conflict) {
        message = state.language === "tr"
          ? `${describeTagAddress(draft)} adresi '${conflict.name}' tagı ile çakışıyor.`
          : `${describeTagAddress(draft)} overlaps with tag '${conflict.name}'.`;
      }
    }
  }

  stateBox.textContent = message;
  stateBox.classList.toggle("error", !!message);
  saveButton.disabled = !!message || !canMutateConfiguration();
  return !message;
}

async function saveTag() {
  if (!requireConfigurationAccess()) return;
  try {
    if (!$("tagName").value.trim())
      throw new Error(state.language === "tr" ? "Tag adı gerekli." : "Tag name is required.");

    if (!validateTagAddressClient())
      return;

    const body = {
      deviceId:$("tagDevice").value,
      name:$("tagName").value.trim(),
      address:$("tagAddress").value.trim(),
      dataType:$("tagDataType").value,
      bitIndex:!tagIsS7() && !tagIsMqtt() && !tagIsOpcUa() && $("tagDataType").value === "Bool" &&
        modbusAddressInfo($("tagAddress").value)?.area === "Holding Register"
          ? Number($("tagBitIndex").value)
          : null,
      byteOrder:$("tagByteOrder").value,
      unit:$("tagUnit").value.trim(),
      offset:Number($("tagOffset").value || 0),
      decimalPlaces:Number($("tagDecimalPlaces").value || 0)
    };

    if (state.editingTagId) {
      await api(
        `/api/tags/${state.editingTagId}`,
        {
          method:"PUT",
          body:JSON.stringify(body)
        });

      showToast(t("tagUpdated"));
    }
    else {
      await api(
        "/api/tags",
        {
          method:"POST",
          body:JSON.stringify(body)
        });

      showToast(t("tagSaved"));
    }

    closeTagModal();
    await loadTags();
    navigate("tags");
    continueCommissioningIfActive();
  }
  catch(error) {
    showToast(error.message);
  }
}



function alarmSourceTags() {
  return state.tags.filter(
    tag => ["Bool","Word","UInt16","Int16","UInt32","Int32","Float32"].includes(tag.dataType));
}

function alarmTag(id) {
  return state.tags.find(x => x.id === id) || null;
}

function alarmTagName(id) {
  return alarmTag(id)?.name || "—";
}

function deviceName(id) {
  return state.devices.find(x => x.id === id)?.name || "—";
}

function alarmDeviceNameFromTagId(tagId) {
  const tag = alarmTag(tagId);
  return tag ? deviceName(tag.deviceId) : "—";
}

async function loadAlarms() {
  const [definitions, active] =
    await Promise.all([
      api("/api/alarms/definitions"),
      api("/api/alarms/active")
    ]);

  state.alarmDefinitions = definitions;
  state.activeAlarms = active;
  await refreshAlarmHistory();
  renderAlarms();
  updateOverview();
  applyAccessMode();
}

function updateAlarmIndicators() {
  const count = state.activeAlarms.length;
  const bell = $("alarmBell");
  const navCount = $("navAlarmCount");

  if (!bell || !navCount)
    return;

  bell.classList.toggle(
    "hidden",
    count === 0);

  navCount.classList.remove("hidden");

  $("alarmBellCount").textContent =
    String(count);

  navCount.textContent =
    String(state.alarmDefinitions.length);
  navCount.title = `${state.alarmDefinitions.length} configured alarms · ${count} active`;

  document.title =
    count > 0
      ? `(${count}) PROGNODE`
      : "PROGNODE";
}

function formatAlarmDuration(item) {
  const start =
    new Date(item.activeAt).getTime();

  const end =
    item.clearedAt
      ? new Date(item.clearedAt).getTime()
      : Date.now();

  if (!Number.isFinite(start) ||
      !Number.isFinite(end) ||
      end < start)
  {
    return "—";
  }

  let seconds =
    Math.floor(
      (end - start) / 1000);

  const days =
    Math.floor(seconds / 86400);

  seconds %= 86400;

  const hours =
    Math.floor(seconds / 3600);

  seconds %= 3600;

  const minutes =
    Math.floor(seconds / 60);

  seconds %= 60;

  if (days > 0)
    return `${days}d ${hours}h ${minutes}m`;

  if (hours > 0)
    return `${hours}h ${minutes}m ${seconds}s`;

  if (minutes > 0)
    return `${minutes}m ${seconds}s`;

  return `${seconds}s`;
}

function statusClass(status) {
  const value = String(status || "").trim().toLowerCase();
  if (["online", "connected", "running", "healthy", "good", "active"].includes(value)) return "status-good";
  if (["offline", "disconnected", "failed", "error", "bad", "expired"].includes(value)) return "status-bad";
  if (["degraded", "warning", "maintenance", "pending", "stale"].includes(value)) return "status-warn";
  return "status-neutral";
}

function priorityClass(priority) {
  return `priority-${String(priority || "low").toLowerCase()}`;
}

function alarmConditionText(definition) {
  const tag = alarmTag(definition.tagId);
  const condition = definition.condition || "DigitalEquals";

  if (condition === "DigitalEquals") {
    const trigger = definition.triggerValue ? "TRUE" : "FALSE";
    if (tag?.dataType === "Word")
      return `Bit ${definition.bitIndex ?? 0} = ${trigger}`;
    return trigger;
  }

  const symbols = {
    GreaterThan:">",
    GreaterThanOrEqual:">=",
    LessThan:"<",
    LessThanOrEqual:"<="
  };
  const threshold = definition.threshold ?? "—";
  const deadband = Number(definition.deadband || 0);
  return `${symbols[condition] || condition} ${threshold}${deadband > 0 ? ` (DB ${deadband})` : ""}`;
}

function alarmNotifyText(definition) {
  const states = [];

  if (definition.notifyOnActive)
    states.push("ACTIVE");

  if (definition.notifyOnCleared)
    states.push("CLEARED");

  states.push(definition.requiresAcknowledgement === false
    ? t("ackNotRequiredShort")
    : t("ackRequiredShort"));
  if (definition.notificationMode === "RepeatUntilAcknowledged")
    states.push(`${t("repeatActiveShort")} ${definition.repeatIntervalSeconds || 60}s`);

  return states.length
    ? states.join(" + ")
    : "—";
}

function renderAlarms() {
  if (!$("alarmActiveTableBody"))
    return;

  const includesQuery = (values, query) => !query || values.some(value =>
    String(value ?? "").toLowerCase().includes(query));
  const activeQuery = $("alarmActiveSearch").value.trim().toLowerCase();
  const activePriority = $("alarmActivePriority").value;
  const activeState = $("alarmActiveState").value;
  const activeFiltered = state.activeAlarms.filter(alarm =>
    (!activePriority || alarm.priority === activePriority) &&
    (!activeState || alarm.state === activeState) &&
    includesQuery([alarm.sourceName,alarm.text,alarm.isSystem ? alarm.sourceName : deviceName(alarm.deviceId)],activeQuery));
  const activePage = tablePage(activeFiltered,state.alarmActivePage,Number($("alarmActivePageSize").value));
  state.alarmActivePage = activePage.page;

  const definitionQuery = $("alarmDefinitionSearch").value.trim().toLowerCase();
  const definitionPriority = $("alarmDefinitionPriority").value;
  const definitionFiltered = state.alarmDefinitions.filter(definition =>
    (!definitionPriority || definition.priority === definitionPriority) &&
    includesQuery([definition.text,alarmTagName(definition.tagId),alarmDeviceNameFromTagId(definition.tagId),alarmConditionText(definition)],definitionQuery));
  const definitionPage = tablePage(definitionFiltered,state.alarmDefinitionPage,Number($("alarmDefinitionPageSize").value));
  state.alarmDefinitionPage = definitionPage.page;

  const historyPage = {items:state.alarmHistory,page:state.alarmHistoryPage,
    pages:Math.max(1,Math.ceil(state.alarmHistoryFilteredCount / Number($("alarmHistoryPageSize").value)))};
  const formatAckTimestamp = value => value
    ? new Date(value).toLocaleString(state.language === "tr" ? "tr-TR" : "en-US", {
        year:"numeric",month:"2-digit",day:"2-digit",
        hour:"2-digit",minute:"2-digit",second:"2-digit"
      })
    : "—";

  $("alarmActiveMeta").textContent = `${activeFiltered.length} / ${state.activeAlarms.length}`;
  $("alarmDefinitionMeta").textContent = `${definitionFiltered.length} / ${state.alarmDefinitions.length}`;
  const definitionCount = $("alarmDefinitionHeaderCount");
  if (definitionCount) definitionCount.textContent = String(state.alarmDefinitions.length);
  $("alarmHistoryMeta").textContent = `${state.alarmHistoryFilteredCount} / ${state.alarmHistoryCount}`;
  $("alarmHistoryCount").textContent = `(${state.alarmHistoryCount})`;
  renderTablePager("alarmActive",activePage,state.activeAlarms.length > 0);
  renderTablePager("alarmDefinition",definitionPage,state.alarmDefinitions.length > 0);
  renderTablePager("alarmHistory",historyPage,state.alarmHistoryFilteredCount > 0);

  $("activeAlarmCount").textContent =
    state.activeAlarms.length;

  $("alarmActiveEmpty").classList.toggle(
    "hidden",
    state.activeAlarms.length > 0);

  $("alarmActiveTableWrap").classList.toggle(
    "hidden",
    state.activeAlarms.length === 0);

  $("alarmActiveTableBody").innerHTML =
    activePage.items.map(a => {
      const device =
        a.isSystem
          ? a.sourceName
          : deviceName(a.deviceId);

      const tag =
        a.isSystem
          ? "SYSTEM"
          : a.sourceName;

      return `
        <tr>
          <td class="${a.isSystem ? "system-source" : ""}">
            ${escapeHtml(device)}
          </td>
          <td class="${a.isSystem ? "system-source" : ""}">
            ${escapeHtml(tag)}
          </td>
          <td>${escapeHtml(a.text)}</td>
          <td>
            <span class="priority-badge ${priorityClass(a.priority)}">
              ${escapeHtml(String(a.priority).toUpperCase())}
            </span>
          </td>
          <td class="alarm-state-${String(a.state).toLowerCase()}">
            ${escapeHtml(String(a.state).toUpperCase())}
          </td>
          <td>
            ${new Date(a.activeSince).toLocaleString(
              state.language === "tr" ? "tr-TR" : "en-US")}
          </td>
          <td>${formatAckTimestamp(a.acknowledgedAt)}</td>
          <td>${escapeHtml(a.acknowledgedBy || "—")}</td>
          <td>
            ${
              a.state === "Acknowledged" || a.requiresAcknowledgement === false
                ? ""
                : `<button class="ack-button"
                     data-ack-alarm="${escapeHtml(a.alarmKey)}">
                     ${t("acknowledge")}
                   </button>`
            }
          </td>
        </tr>`;
    }).join("") || (state.activeAlarms.length ? `<tr><td colspan="9">${state.language === "tr" ? "Filtreye uyan aktif alarm yok." : "No active alarms match the filters."}</td></tr>` : "");

  document
    .querySelectorAll("[data-ack-alarm]")
    .forEach(button =>
      button.addEventListener(
        "click",
        async () => {
          await api(
            "/api/alarms/ack",
            {
              method:"POST",
              body:JSON.stringify({
                alarmKey:
                  button.dataset.ackAlarm
              })
            });

          showToast(t("alarmAcknowledged"));
          await loadAlarms();
        }));

  $("alarmDefinitionEmpty").classList.toggle(
    "hidden",
    state.alarmDefinitions.length > 0);

  $("alarmDefinitionTableWrap").classList.toggle(
    "hidden",
    state.alarmDefinitions.length === 0);

  const definitionBody=$("alarmDefinitionTableBody");
  const definitionKey=JSON.stringify([state.language,definitionPage.items]);
  if(definitionBody.dataset.renderKey!==definitionKey){
  definitionBody.innerHTML =
    definitionPage.items.map(d => `
      <tr>
        <td><input type="checkbox" data-bulk-check value="${d.id}" aria-label="Select ${escapeHtml(d.text)}"></td>
        <td>${escapeHtml(alarmDeviceNameFromTagId(d.tagId))}</td>
        <td>${escapeHtml(alarmTagName(d.tagId))}</td>
        <td>${escapeHtml(alarmConditionText(d))}</td>
        <td>${escapeHtml(d.text)}</td>
        <td>
          <span class="priority-badge ${priorityClass(d.priority)}">
            ${escapeHtml(String(d.priority).toUpperCase())}
          </span>
        </td>
        <td>${escapeHtml(alarmNotifyText(d))}</td>
        <td>${d.delayOnMs} / ${d.delayOffMs} ms</td>
        <td class="actions-cell">
          <button class="edit-button"
            data-edit-alarm="${d.id}">
            ${t("edit")}
          </button>
          <button class="delete-button"
            data-delete-alarm="${d.id}">
            ${t("delete")}
          </button>
        </td>
      </tr>
    `).join("") || (state.alarmDefinitions.length ? `<tr><td colspan="8">${state.language === "tr" ? "Filtreye uyan alarm tanımı yok." : "No alarm definitions match the filters."}</td></tr>` : "");

  document
    .querySelectorAll("[data-edit-alarm]")
    .forEach(button =>
      button.addEventListener(
        "click",
        () =>
          openEditAlarmModal(
            button.dataset.editAlarm)));

  document
    .querySelectorAll("[data-delete-alarm]")
    .forEach(button =>
      button.addEventListener(
        "click",
        async () => {
          if (!requireConfigurationAccess()) return;
          if (!confirm(
            state.language === "tr"
              ? "Alarm tanımı silinsin mi?"
              : "Delete alarm definition?"))
          {
            return;
          }

          button.disabled=true;
          try {
            await api(
              `/api/alarms/definitions/${button.dataset.deleteAlarm}`,
              {method:"DELETE"});
            showToast(t("alarmDeleted"));
            await loadAlarms();
          } catch(error) {
            button.disabled=false;
            showToast(error.message);
          }
        }));
  definitionBody.dataset.renderKey=definitionKey;
  applyAccessMode();
  }
  renderBulkSelection('alarmDefinitions',definitionBody,definitionPage.items,x=>x.id,
    x=>api(`/api/alarms/definitions/${x}`,{method:'DELETE'}),async()=>loadAlarms());

  $("alarmHistoryTableBody").innerHTML =
    historyPage.items.map(e => {
      const device =
        e.isSystem
          ? e.sourceName
          : deviceName(e.deviceId);

      const tag =
        e.isSystem
          ? "SYSTEM"
          : e.sourceName;

      return `
        <tr>
          <td><input type="checkbox" data-bulk-check value="${e.occurrenceId||''}" aria-label="Select alarm occurrence" ${e.occurrenceId?'':'disabled'}></td>
          <td>
            ${new Date(e.activeAt).toLocaleString(
              state.language === "tr" ? "tr-TR" : "en-US")}
          </td>
          <td>
            ${
              e.clearedAt
                ? new Date(e.clearedAt).toLocaleString(
                    state.language === "tr" ? "tr-TR" : "en-US")
                : `<span class="alarm-state-${String(e.state).toLowerCase()}">
                     ${escapeHtml(String(e.state).toUpperCase())}
                   </span>`
            }
          </td>
          <td>${formatAlarmDuration(e)}</td>
          <td>${formatAckTimestamp(e.acknowledgedAt)}</td>
          <td>${escapeHtml(e.acknowledgedBy || "—")}</td>
          <td class="${e.isSystem ? "system-source" : ""}">
            ${escapeHtml(device)}
          </td>
          <td class="${e.isSystem ? "system-source" : ""}">
            ${escapeHtml(tag)}
          </td>
          <td>${escapeHtml(e.text)}</td>
          <td>
            <span class="priority-badge ${priorityClass(e.priority)}">
              ${escapeHtml(String(e.priority).toUpperCase())}
            </span>
          </td>
        </tr>`;
    }).join("") || `<tr><td colspan="10">${state.language === "tr" ? "Filtreye uyan alarm geçmişi yok." : "No alarm history matches the filters."}</td></tr>`;
  renderBulkSelection('alarmHistory',$("alarmHistoryTableBody"),historyPage.items,x=>x.occurrenceId,
    ()=>Promise.resolve(),async()=>refreshAlarmHistory(),ids=>api('/api/alarms/history/delete-selected',{
      method:'POST',body:JSON.stringify({occurrenceIds:ids})}));

  updateAlarmIndicators();
}

function populateAlarmTagSelect() {
  const tags =
    alarmSourceTags();

  $("alarmTag").innerHTML =
    tags.length
      ? tags.map(tag => `
          <option value="${tag.id}">
            ${escapeHtml(deviceName(tag.deviceId))}
            •
            ${escapeHtml(tag.name)}
          </option>
        `).join("")
      : `<option value="">
          ${
            state.language === "tr"
              ? "Önce desteklenen bir alarm Tagı ekleyin"
              : "Add a supported alarm Tag first"
          }
        </option>`;
}

function updateAlarmFormVisibility() {
  const tag = state.tags.find(x => x.id === $("alarmTag").value);
  const isDigital = tag && ["Bool","Word"].includes(tag.dataType);

  if (isDigital) {
    $("alarmCondition").value = "DigitalEquals";
    $("alarmCondition").disabled = true;
  } else {
    $("alarmCondition").disabled = false;
    if ($("alarmCondition").value === "DigitalEquals")
      $("alarmCondition").value = "GreaterThanOrEqual";
  }

  $("alarmBitField").classList.toggle("hidden", !(isDigital && tag?.dataType === "Word"));
  $("alarmTriggerField").classList.toggle("hidden", !isDigital);
  $("alarmThresholdField").classList.toggle("hidden", !!isDigital);
  $("alarmDeadbandField").classList.toggle("hidden", !!isDigital);
}

function resetAlarmForm() {
  state.editingAlarmId = null;

  populateAlarmTagSelect();

  $("alarmBitIndex").value = "0";
  $("alarmCondition").value = "DigitalEquals";
  $("alarmTriggerValue").value = "true";
  $("alarmThreshold").value = "80";
  $("alarmDeadband").value = "0";
  $("alarmPriority").value = "Medium";
  $("alarmText").value = "";
  $("alarmDelayOn").value = "0";
  $("alarmDelayOff").value = "0";
  $("alarmNotifyActive").checked = true;
  $("alarmNotifyCleared").checked = false;
  $("alarmNotificationMode").value = "NotifyOnce";
  $("alarmRepeatInterval").value = "60";
  $("alarmRequiresAcknowledgement").checked = ALARM_DEFAULT_REQUIRES_ACK;
  $("alarmContinueAfterClear").checked = false;
  updateAlarmNotificationPolicyVisibility();

  updateAlarmFormVisibility();
}

function updateAlarmNotificationPolicyVisibility() {
  const repeat = $("alarmNotificationMode")?.value === "RepeatUntilAcknowledged";
  const requiresAck = $("alarmRequiresAcknowledgement")?.checked !== false;
  $("alarmRepeatIntervalField")?.classList.toggle("hidden", !repeat);
  $("alarmContinueAfterClearCard")?.classList.toggle("hidden", !repeat || !requiresAck);
  if (!requiresAck && $("alarmContinueAfterClear"))
    $("alarmContinueAfterClear").checked = false;
}

function openAddAlarmModal() {
  if (!requireConfigurationAccess()) return;
  if (!alarmSourceTags().length)
  {
    showToast(
      state.language === "tr"
        ? "Önce desteklenen bir alarm Tagı oluşturun."
        : "Create a supported alarm Tag first.");

    navigate("tags");
    return;
  }

  resetAlarmForm();

  $("alarmModalTitle").textContent =
    t("addAlarm");

  $("alarmModal").classList.remove("hidden");
}

function openEditAlarmModal(id) {
  if (!requireConfigurationAccess()) return;
  const d =
    state.alarmDefinitions.find(
      x => x.id === id);

  if (!d)
    return;

  state.editingAlarmId = id;

  populateAlarmTagSelect();

  $("alarmTag").value = d.tagId;
  $("alarmBitIndex").value = d.bitIndex ?? 0;
  $("alarmCondition").value = d.condition || "DigitalEquals";
  $("alarmTriggerValue").value =
    d.triggerValue ? "true" : "false";
  $("alarmThreshold").value = d.threshold ?? 80;
  $("alarmDeadband").value = d.deadband ?? 0;

  $("alarmPriority").value = d.priority;
  $("alarmText").value = d.text;
  $("alarmDelayOn").value = d.delayOnMs;
  $("alarmDelayOff").value = d.delayOffMs;
  $("alarmNotifyActive").checked =
    d.notifyOnActive !== false;

  $("alarmNotifyCleared").checked =
    d.notifyOnCleared === true;
  $("alarmNotificationMode").value = d.notificationMode || "NotifyOnce";
  $("alarmRepeatInterval").value = String(d.repeatIntervalSeconds || 60);
  $("alarmRequiresAcknowledgement").checked = d.requiresAcknowledgement !== false;
  $("alarmContinueAfterClear").checked = d.continueAfterClearUntilAcknowledged === true;

  updateAlarmFormVisibility();
  updateAlarmNotificationPolicyVisibility();

  $("alarmModalTitle").textContent =
    t("editAlarm");

  $("alarmModal").classList.remove("hidden");
}

function closeAlarmModal() {
  $("alarmModal").classList.add("hidden");
}

async function saveAlarm() {
  if (!requireConfigurationAccess()) return;
  try {
    const tag =
      state.tags.find(
        x => x.id === $("alarmTag").value);

    if (!tag)
    {
      throw new Error(
        state.language === "tr"
          ? "Kaynak Tag gerekli."
          : "Source Tag is required.");
    }

    if (!$("alarmText").value.trim())
    {
      throw new Error(
        state.language === "tr"
          ? "Alarm metni gerekli."
          : "Alarm text is required.");
    }

    const numericAlarm = !["Bool","Word"].includes(tag.dataType);
    if (numericAlarm) {
      const threshold = Number($("alarmThreshold").value);
      const deadband = Number($("alarmDeadband").value || 0);
      if (!Number.isFinite(threshold))
        throw new Error(state.language === "tr" ? "Numeric alarm için Threshold gerekli." : "Threshold is required for a numeric alarm.");
      if (!Number.isFinite(deadband) || deadband < 0)
        throw new Error(state.language === "tr" ? "Deadband 0 veya daha büyük olmalı." : "Deadband must be 0 or greater.");
    }

    const body = {
      tagId:tag.id,
      text:$("alarmText").value.trim(),
      priority:$("alarmPriority").value,
      bitIndex:
        tag.dataType === "Word"
          ? Number($("alarmBitIndex").value)
          : null,
      triggerValue:
        $("alarmTriggerValue").value === "true",
      condition:$("alarmCondition").value,
      threshold:["Bool","Word"].includes(tag.dataType)
        ? null
        : Number($("alarmThreshold").value),
      deadband:["Bool","Word"].includes(tag.dataType)
        ? 0
        : Number($("alarmDeadband").value || 0),
      delayOnMs:
        Number($("alarmDelayOn").value || 0),
      delayOffMs:
        Number($("alarmDelayOff").value || 0),
      notifyOnActive:
        $("alarmNotifyActive").checked,
      notifyOnCleared:
        $("alarmNotifyCleared").checked,
      notificationMode:
        $("alarmNotificationMode").value,
      repeatIntervalSeconds:
        Number($("alarmRepeatInterval").value || 60),
      continueAfterClearUntilAcknowledged:
        $("alarmContinueAfterClear").checked,
      requiresAcknowledgement:
        $("alarmRequiresAcknowledgement").checked
    };
    const getSavedAckRequirement = saved => {
      const entry = Object.entries(saved || {}).find(([key]) =>
        key.toLowerCase() === "requiresacknowledgement");
      return typeof entry?.[1] === "boolean" ? entry[1] : null;
    };
    const verifySavedAckRequirement = saved => {
      const savedValue = getSavedAckRequirement(saved);
      if (savedValue === null)
        throw new Error(state.language === "tr"
          ? "Core yanıtında ACK ayarı yok. Core'u yeniden derleyip başlatın."
          : "The Core response does not include the ACK setting. Rebuild and restart Core.");
      if (savedValue !== body.requiresAcknowledgement)
        throw new Error(state.language === "tr"
          ? `Core ACK ayarını ${savedValue ? "açık" : "kapalı"} kaydetti; istenen değer ${body.requiresAcknowledgement ? "açık" : "kapalı"}. Core'u yeniden başlatıp tekrar deneyin.`
          : `Core saved ACK as ${savedValue ? "enabled" : "disabled"}, but ${body.requiresAcknowledgement ? "enabled" : "disabled"} was requested. Restart Core and try again.`);
    };

    if (state.editingAlarmId)
    {
      const saved = await api(
        `/api/alarms/definitions/${state.editingAlarmId}`,
        {
          method:"PUT",
          body:JSON.stringify(body)
        });

      verifySavedAckRequirement(saved);

      showToast(t("alarmUpdated"));
    }
    else
    {
      const saved = await api(
        "/api/alarms/definitions",
        {
          method:"POST",
          body:JSON.stringify(body)
        });

      verifySavedAckRequirement(saved);

      showToast(t("alarmSaved"));
    }

    closeAlarmModal();
    await loadAlarms();
    navigate("alarms");
    continueCommissioningIfActive();
  }
  catch(error)
  {
    showToast(error.message);
  }
}

async function refreshActiveAlarms() {
  try {
    state.activeAlarms =
      await api("/api/alarms/active");

    renderAlarms();
    updateOverview();
  }
  catch(error)
  {
    console.warn(error);
  }
}

async function refreshAlarmHistory() {
  try {
    const requestId = ++state.alarmHistoryRequestId;
    const total = await api("/api/alarms/history/count");
    if(requestId !== state.alarmHistoryRequestId)return;
    const size = Number($("alarmHistoryPageSize").value) || 25;
    const filters = `&search=${encodeURIComponent($("alarmHistorySearch").value.trim())}&priority=${encodeURIComponent($("alarmHistoryPriority").value)}`;
    const first = await api(`/api/alarms/history/page?offset=${(state.alarmHistoryPage-1)*size}&limit=${size}${filters}`);
    if(requestId !== state.alarmHistoryRequestId)return;
    const pages = Math.max(1,Math.ceil((Number(first.total) || 0) / size));
    state.alarmHistoryPage = Math.min(Math.max(1,state.alarmHistoryPage),pages);
    const page = first.items.length || state.alarmHistoryPage === 1 ? first :
      await api(`/api/alarms/history/page?offset=${(state.alarmHistoryPage-1)*size}&limit=${size}${filters}`);
    if(requestId !== state.alarmHistoryRequestId)return;
    state.alarmHistory = page.items;
    state.alarmHistoryCount = Number(total.count) || 0;
    state.alarmHistoryFilteredCount = Number(page.total) || 0;

    renderAlarms();
  }
  catch(error)
  {
    console.warn(error);
  }
}

/* =========================
   Trend + Historian V0.6
   ========================= */

const trendPalette = [
  "#3ed7e8",
  "#51e6a6",
  "#f6c96b",
  "#ff7180",
  "#a78bfa",
  "#60a5fa",
  "#f472b6",
  "#fb923c"
];

async function loadTrends() {
  state.trends =
    await api("/api/trends");

  if (state.selectedTrendId &&
      !state.trends.some(
        x => x.id === state.selectedTrendId))
  {
    state.selectedTrendId = null;
  }

  if (!state.selectedTrendId &&
      state.trends.length)
  {
    state.selectedTrendId =
      state.trends[0].id;
  }

  renderTrendList();

  if (state.selectedTrendId)
  {
    const trend =
      state.trends.find(
        x => x.id === state.selectedTrendId);

    if (trend &&
        !$("trendRange").dataset.userChanged)
    {
      $("trendRange").value =
        String(
          trend.defaultWindowMinutes);
    }
    syncTrendRangePills();

    await refreshTrendPoints();
  }
  else
  {
    state.trendPayload = null;
    drawTrendChart();
  }
  applyAccessMode();
}

function intervalLabel(seconds) {
  if (seconds < 60)
    return `${seconds}s`;

  return `${seconds / 60}m`;
}

function retentionLabel(days) {
  if (days === 1825)
    return state.language === "tr"
      ? "5 yıl"
      : "5 years";

  if (days === 365)
    return state.language === "tr"
      ? "1 yıl"
      : "1 year";

  return `${days}d`;
}

function renderTrendList() {
  if (!$("trendList"))
    return;

  $("trendCount").textContent =
    state.trends.length;
  if ($("navTrendCount")) {
    const available = state.historianConfigurations.filter(x=>x.configuration?.enabled===true).length;
    $("navTrendCount").textContent = String(available);
    $("navTrendCount").title = state.language === "tr"
      ? `${available} Historian sinyali Trend Studio'da kullanılabilir; en fazla 15 grafik aynı anda açılır.`
      : `${available} Historian signals available in Trend Studio; up to 15 charts can be open at once.`;
  }

  $("trendList").innerHTML =
    state.trends.length
      ? state.trends.map(trend => `
          <div class="trend-item ${trend.id === state.selectedTrendId ? "active" : ""}"
               data-select-trend="${trend.id}">
            <div class="trend-item-copy">
              <strong>${escapeHtml(trend.name)}</strong>
              <span>
                ${trend.tagIds.length} tag
              </span>
            </div>

            <div class="trend-item-actions">
              <button class="trend-mini-button"
                data-edit-trend="${trend.id}">✎</button>

              <button class="trend-mini-button"
                data-delete-trend="${trend.id}">×</button>
            </div>
          </div>
        `).join("")
      : `<div class="empty-inline">
          ${
            state.language === "tr"
              ? "Henüz trend yok."
              : "No trends yet."
          }
        </div>`;

  document
    .querySelectorAll("[data-select-trend]")
    .forEach(item =>
      item.addEventListener(
        "click",
        async event => {
          if (event.target.closest("[data-edit-trend]") ||
              event.target.closest("[data-delete-trend]"))
          {
            return;
          }

          state.selectedTrendId =
            item.dataset.selectTrend;

          state.hiddenTrendTags.clear();
          state.trendHover = null;

          $("trendRange").dataset.userChanged = "";

          renderTrendList();

          const trend =
            state.trends.find(
              x => x.id === state.selectedTrendId);

          if (trend)
          {
            $("trendRange").value =
              String(
                trend.defaultWindowMinutes);
          }

          updateTrendCustomRangeVisibility();
          syncTrendRangePills();
          await refreshTrendPoints();
        }));

  document
    .querySelectorAll("[data-edit-trend]")
    .forEach(button =>
      button.addEventListener(
        "click",
        event => {
          event.stopPropagation();
          openEditTrendModal(
            button.dataset.editTrend);
        }));

  document
    .querySelectorAll("[data-delete-trend]")
    .forEach(button =>
      button.addEventListener(
        "click",
        async event => {
          event.stopPropagation();

          if (!confirm(
            state.language === "tr"
              ? "Trend silinsin mi?"
              : "Delete trend?"))
          {
            return;
          }

          await api(
            `/api/trends/${button.dataset.deleteTrend}`,
            {method:"DELETE"});

          showToast(t("trendDeleted"));

          if (state.selectedTrendId ===
              button.dataset.deleteTrend)
          {
            state.selectedTrendId = null;
          }

          await loadTrends();
          await loadHistorianStats();
        }));
}

function trendColorFor(tagId, trend) {
  const index =
    trend.tagIds.indexOf(tagId);

  return trend.colors?.[index] ||
    trendPalette[index % trendPalette.length];
}

function populateTrendTagChecklist(
  selectedIds = [],
  selectedColors = [])
{
  const selected =
    new Set(selectedIds);

  const colorMap =
    new Map(
      selectedIds.map(
        (id, index) => [
          id,
          selectedColors[index] ||
            trendPalette[index % trendPalette.length]
        ]));

  $("trendTagChecklist").innerHTML =
    state.tags.length
      ? state.tags.map(
          (tag, index) => {
            const color =
              colorMap.get(tag.id) ||
              trendPalette[index % trendPalette.length];

            return `
              <label class="tag-check-item">
                <input type="checkbox"
                  value="${tag.id}"
                  ${selected.has(tag.id) ? "checked" : ""}/>

                <span>
                  ${escapeHtml(tag.name)}
                </span>

                <input type="color"
                  data-trend-color="${tag.id}"
                  value="${escapeHtml(color)}"
                  title="Series color" />
              </label>
            `;
          }).join("")
      : `<div class="empty-inline">
          ${
            state.language === "tr"
              ? "Önce Tag ekleyin."
              : "Add Tags first."
          }
        </div>`;
}

function openAddTrendModal() {
  if (!requireConfigurationAccess()) return;
  if (!state.tags.length)
  {
    showToast(
      state.language === "tr"
        ? "Önce Tag ekleyin."
        : "Add Tags first.");

    navigate("tags");
    return;
  }

  state.editingTrendId = null;

  $("trendName").value = "";
  $("trendDefaultWindow").value = "60";

  populateTrendTagChecklist([]);

  $("trendModalTitle").textContent =
    t("addTrend");

  $("trendModal").classList.remove("hidden");
}

function openEditTrendModal(id) {
  if (!requireConfigurationAccess()) return;
  const trend =
    state.trends.find(x => x.id === id);

  if (!trend)
    return;

  state.editingTrendId = id;

  $("trendName").value = trend.name;
  $("trendDefaultWindow").value =
    String(trend.defaultWindowMinutes);

  populateTrendTagChecklist(
    trend.tagIds,
    trend.colors || []);

  $("trendModalTitle").textContent =
    t("editTrend");

  $("trendModal").classList.remove("hidden");
}

function closeTrendModal() {
  $("trendModal").classList.add("hidden");
}

async function saveTrend() {
  try {
    const checked =
      [...$("trendTagChecklist")
        .querySelectorAll(
          'input[type="checkbox"]:checked')];

    const tagIds =
      checked.map(x => x.value);

    const colors =
      checked.map(x =>
        $("trendTagChecklist")
          .querySelector(
            `[data-trend-color="${x.value}"]`)
          ?.value || "#3ed7e8");

    if (!$("trendName").value.trim())
    {
      throw new Error(
        state.language === "tr"
          ? "Trend adı gerekli."
          : "Trend name is required.");
    }

    if (tagIds.length < 1 ||
        tagIds.length > 8)
    {
      throw new Error(
        state.language === "tr"
          ? "1 ile 8 arasında Tag seçin."
          : "Select between 1 and 8 Tags.");
    }

    const body = {
      name:$("trendName").value.trim(),
      tagIds,
      colors,
      defaultWindowMinutes:
        Number($("trendDefaultWindow").value)
    };

    if (state.editingTrendId)
    {
      await api(
        `/api/trends/${state.editingTrendId}`,
        {
          method:"PUT",
          body:JSON.stringify(body)
        });

      showToast(t("trendUpdated"));
    }
    else
    {
      const created =
        await api(
          "/api/trends",
          {
            method:"POST",
            body:JSON.stringify(body)
          });

      state.selectedTrendId =
        created.id;

      showToast(t("trendSaved"));
    }

    closeTrendModal();
    await loadTrends();
    await loadHistorianStats();
    navigate("trends");
  }
  catch(error)
  {
    showToast(error.message);
  }
}

function updateTrendCustomRangeVisibility() {
  const custom =
    $("trendRange").value === "custom";

  $("trendCustomRange").classList.toggle(
    "hidden",
    !custom);

  if (custom)
  {
    const now = new Date();
    const oneHourAgo =
      new Date(now.getTime() - 60 * 60 * 1000);

    if (!$("trendFrom").value)
      $("trendFrom").value =
        dateToLocalInput(oneHourAgo);

    if (!$("trendTo").value)
      $("trendTo").value =
        dateToLocalInput(now);
  }
}

function dateToLocalInput(date) {
  const local =
    new Date(
      date.getTime() -
      date.getTimezoneOffset() * 60000);

  return local
    .toISOString()
    .slice(0,16);
}

function currentTrendRange() {
  const mode =
    $("trendRange").value;

  if (mode === "custom")
  {
    const from =
      new Date($("trendFrom").value);

    const to =
      new Date($("trendTo").value);

    if (!Number.isFinite(from.getTime()) ||
        !Number.isFinite(to.getTime()) ||
        to <= from)
    {
      throw new Error(
        state.language === "tr"
          ? "Geçerli başlangıç ve bitiş tarihi seçin."
          : "Select a valid start and end date.");
    }

    return {from,to};
  }

  const minutes =
    Number(mode || 60);

  const to =
    new Date();

  const from =
    new Date(
      to.getTime() -
      minutes * 60 * 1000);

  return {from,to};
}

async function refreshTrendPoints() {
  if (!state.selectedTrendId)
    return;

  try
  {
    const range =
      currentTrendRange();

    state.trendPayload =
      await api(
        `/api/trends/${state.selectedTrendId}/points` +
        `?from=${encodeURIComponent(range.from.toISOString())}` +
        `&to=${encodeURIComponent(range.to.toISOString())}` +
        `&maxPoints=5000`);

    drawTrendChart();
  }
  catch(error)
  {
    console.warn(error);
  }
}

function formatTrendValue(tag, value) {
  if (value === null ||
      value === undefined ||
      !Number.isFinite(Number(value)))
  {
    return "—";
  }

  if (tag?.dataType === "Bool")
    return Number(value) !== 0
      ? "TRUE"
      : "FALSE";

  const decimals =
    Math.max(
      0,
      Math.min(
        6,
        Number(tag?.decimalPlaces ?? 2)));

  const formatted =
    Number(value).toLocaleString(
      state.language === "tr"
        ? "tr-TR"
        : "en-US",
      {
        minimumFractionDigits: decimals,
        maximumFractionDigits: decimals
      });

  return tag?.unit
    ? `${formatted} ${tag.unit}`
    : formatted;
}

function renderTrendLegendAndStats(payload) {
  if (!payload)
  {
    $("trendLegend").innerHTML = "";
    $("trendStats").innerHTML = "";
    return;
  }

  $("trendLegend").innerHTML =
    payload.trend.tagIds.map(
      (id,index) => {
        const tag =
          state.tags.find(x => x.id === id);

        const color =
          payload.trend.colors?.[index] ||
          trendPalette[index % trendPalette.length];

        const hidden =
          state.hiddenTrendTags.has(id);

        return `
          <button class="legend-item ${hidden ? "hidden-series" : ""}"
            data-toggle-trend-series="${id}">
            <span class="legend-dot"
              style="background:${escapeHtml(color)}"></span>
            ${escapeHtml(tag?.name || id)}
          </button>
        `;
      }).join("");

  document
    .querySelectorAll("[data-toggle-trend-series]")
    .forEach(button =>
      button.addEventListener(
        "click",
        () => {
          const id =
            button.dataset.toggleTrendSeries;

          if (state.hiddenTrendTags.has(id))
            state.hiddenTrendTags.delete(id);
          else
            state.hiddenTrendTags.add(id);

          drawTrendChart();
        }));

  $("trendStats").innerHTML =
    payload.series.map(
      (series,index) => {
        const tag =
          state.tags.find(
            x => x.id === series.tagId);

        const color =
          trendColorFor(
            series.tagId,
            payload.trend);

        return `
          <div class="trend-stat">
            <strong style="color:${escapeHtml(color)}">
              ${escapeHtml(tag?.name || series.tagId)}
            </strong>
            <span>
              N=${series.sampleCount}
              • MIN ${escapeHtml(formatTrendValue(tag, series.minimum))}
              • MAX ${escapeHtml(formatTrendValue(tag, series.maximum))}
              • AVG ${escapeHtml(formatTrendValue(tag, series.average))}
            </span>
          </div>
        `;
      }).join("");
}

function nearestPoint(points, timeMs) {
  if (!points?.length)
    return null;

  let low = 0;
  let high = points.length - 1;

  while (low < high)
  {
    const mid =
      Math.floor((low + high) / 2);

    const midTime =
      new Date(
        points[mid].timestamp).getTime();

    if (midTime < timeMs)
      low = mid + 1;
    else
      high = mid;
  }

  const a = points[low];
  const b = low > 0
    ? points[low - 1]
    : null;

  if (!b)
    return a;

  return Math.abs(
      new Date(a.timestamp).getTime() -
      timeMs)
    <
    Math.abs(
      new Date(b.timestamp).getTime() -
      timeMs)
      ? a
      : b;
}

function updateTrendTooltip() {
  const tooltip =
    $("trendTooltip");

  const payload =
    state.trendPayload;

  const hover =
    state.trendHover;

  if (!payload ||
      !hover ||
      !state.trendChartModel)
  {
    tooltip.classList.add("hidden");
    return;
  }

  const rows = [];

  payload.series.forEach(
    series => {
      if (state.hiddenTrendTags.has(
        series.tagId))
      {
        return;
      }

      const point =
        nearestPoint(
          series.points,
          hover.timeMs);

      if (!point)
        return;

      const tag =
        state.tags.find(
          x => x.id === series.tagId);

      const color =
        trendColorFor(
          series.tagId,
          payload.trend);

      rows.push(`
        <div class="trend-tooltip-row">
          <span class="trend-tooltip-color"
            style="background:${escapeHtml(color)}"></span>
          <span>
            ${escapeHtml(tag?.name || series.tagId)}:
            <b>${escapeHtml(String(point.quality || "").toLowerCase() === "good" ? formatTrendValue(tag, point.value) : `COMM: ${String(point.quality || "BAD").toUpperCase()}`)}</b>
          </span>
        </div>
      `);
    });

  tooltip.innerHTML = `
    <strong>
      ${new Date(hover.timeMs).toLocaleString(
        state.language === "tr"
          ? "tr-TR"
          : "en-US")}
    </strong>
    ${rows.join("")}
  `;

  tooltip.style.left =
    `${Math.min(
      hover.x + 12,
      Math.max(
        8,
        $("trendCanvas").clientWidth - 260)
    )}px`;

  tooltip.style.top =
    `${Math.max(
      8,
      hover.y - 18
    )}px`;

  tooltip.classList.remove("hidden");
}

function drawTrendChart() {
  const canvas =
    $("trendCanvas");

  if (!canvas)
    return;

  const shell =
    canvas.parentElement;

  const rect =
    shell.getBoundingClientRect();

  const dpr =
    window.devicePixelRatio || 1;

  const width =
    Math.max(
      320,
      rect.width);

  const height = 410;

  canvas.width =
    Math.floor(width * dpr);

  canvas.height =
    Math.floor(height * dpr);

  canvas.style.width =
    `${width}px`;

  canvas.style.height =
    `${height}px`;

  const ctx =
    canvas.getContext("2d");

  ctx.setTransform(
    dpr,0,0,dpr,0,0);

  ctx.clearRect(
    0,0,width,height);

  const payload =
    state.trendPayload;

  $("trendChartTitle").textContent =
    payload?.trend?.name || "—";

  renderTrendLegendAndStats(payload);

  const visibleSeries =
    payload?.series.filter(
      s =>
        !state.hiddenTrendTags.has(
          s.tagId))
    ?? [];

  const hasPoints =
    visibleSeries.some(
      s => s.points.length);

  $("trendChartEmpty").classList.toggle(
    "hidden",
    hasPoints);

  if (!payload || !hasPoints)
  {
    state.trendChartModel = null;
    updateTrendTooltip();
    return;
  }

  const style =
    getComputedStyle(
      document.documentElement);

  const grid =
    style.getPropertyValue("--line-soft").trim() ||
    "#193246";

  const text =
    style.getPropertyValue("--muted").trim() ||
    "#829aab";

  const all =
    visibleSeries
      .flatMap(
        s =>
          s.points
            .filter(p => p.value !== null && p.value !== undefined && String(p.quality).toLowerCase() === "good")
            .map(p => Number(p.value)))
      .filter(Number.isFinite);

  if (!all.length)
    return;

  let min =
    Math.min(...all);

  let max =
    Math.max(...all);

  if (min === max)
  {
    min -= 1;
    max += 1;
  }

  const padding =
    Math.max(
      (max - min) * 0.08,
      0.001);

  min -= padding;
  max += padding;

  const left = 64;
  const right = 20;
  const top = 20;
  const bottom = 42;

  const plotW =
    width - left - right;

  const plotH =
    height - top - bottom;

  const fromMs =
    new Date(payload.from).getTime();

  const toMs =
    new Date(payload.to).getTime();

  ctx.strokeStyle = grid;
  ctx.lineWidth = 1;
  ctx.fillStyle = text;
  ctx.font = "10px Segoe UI";

  for (let i=0;i<=5;i++)
  {
    const y =
      top + plotH * i / 5;

    ctx.beginPath();
    ctx.moveTo(left,y);
    ctx.lineTo(left+plotW,y);
    ctx.stroke();

    const v =
      max - (max-min) * i / 5;

    ctx.fillText(
      Number(v).toFixed(2),
      5,
      y + 3);
  }

  for (let i=0;i<=5;i++)
  {
    const x =
      left + plotW * i / 5;

    ctx.beginPath();
    ctx.moveTo(x,top);
    ctx.lineTo(x,top+plotH);
    ctx.stroke();

    const ts =
      new Date(
        fromMs +
        (toMs-fromMs) * i / 5);

    ctx.fillText(
      ts.toLocaleString(
        state.language === "tr"
          ? "tr-TR"
          : "en-US",
        {
          month:"2-digit",
          day:"2-digit",
          hour:"2-digit",
          minute:"2-digit"
        }),
      x - 30,
      height - 12);
  }

  // Shade communication/quality gaps first.
  const gapColor = style.getPropertyValue("--danger").trim() || "#ff6478";
  visibleSeries.forEach(series => {
    const pts=series.points;
    let gapStart=null;
    for (let i=0;i<pts.length;i++) {
      const p=pts[i];
      const bad=String(p.quality || "").toLowerCase() !== "good" || p.value === null || p.value === undefined;
      const time=new Date(p.timestamp).getTime();
      if (bad && gapStart===null) gapStart=time;
      const nextGood=!bad && gapStart!==null;
      const last=i===pts.length-1 && gapStart!==null;
      if (nextGood || last) {
        const endTime=nextGood ? time : new Date(pts[i].timestamp).getTime();
        const x1=left+(gapStart-fromMs)/Math.max(1,toMs-fromMs)*plotW;
        const x2=left+(endTime-fromMs)/Math.max(1,toMs-fromMs)*plotW;
        ctx.save();
        ctx.globalAlpha=.10;
        ctx.fillStyle=gapColor;
        ctx.fillRect(Math.max(left,x1),top,Math.max(3,x2-x1),plotH);
        ctx.restore();
        gapStart=null;
      }
    }
  });

  visibleSeries.forEach(series => {
    const tag=state.tags.find(x => x.id === series.tagId);
    const color=trendColorFor(series.tagId,payload.trend);
    ctx.strokeStyle=color;
    ctx.lineWidth=2;
    ctx.beginPath();
    let hasPath=false;
    let previousY=null;

    series.points.forEach(point => {
      const good=String(point.quality || "").toLowerCase() === "good" && point.value !== null && point.value !== undefined;
      if (!good) { hasPath=false; previousY=null; return; }
      const time=new Date(point.timestamp).getTime();
      const x=left+(time-fromMs)/Math.max(1,toMs-fromMs)*plotW;
      const y=top+(max-Number(point.value))/(max-min)*plotH;
      if (!hasPath) { ctx.moveTo(x,y); hasPath=true; }
      else if (tag?.dataType === "Bool") { ctx.lineTo(x,previousY); ctx.lineTo(x,y); }
      else ctx.lineTo(x,y);
      previousY=y;
    });
    ctx.stroke();
  });

  state.trendChartModel = {
    left,
    top,
    plotW,
    plotH,
    fromMs,
    toMs,
    min,
    max
  };

  if (state.trendHover)
  {
    const timeMs =
      state.trendHover.timeMs;

    const x =
      left +
      (timeMs-fromMs) /
      Math.max(1,toMs-fromMs) *
      plotW;

    ctx.save();

    ctx.strokeStyle =
      style.getPropertyValue("--text-soft").trim() ||
      "#b9cdda";

    ctx.setLineDash([4,4]);
    ctx.beginPath();
    ctx.moveTo(x,top);
    ctx.lineTo(x,top+plotH);
    ctx.stroke();
    ctx.restore();

    visibleSeries.forEach(
      series => {
        const point =
          nearestPoint(
            series.points,
            timeMs);

        if (!point || point.value === null || point.value === undefined || String(point.quality || "").toLowerCase() !== "good")
          return;

        const pointTime =
          new Date(
            point.timestamp).getTime();

        const px =
          left +
          (pointTime-fromMs) /
          Math.max(1,toMs-fromMs) *
          plotW;

        const py =
          top +
          (max-Number(point.value)) /
          (max-min) *
          plotH;

        ctx.beginPath();
        ctx.fillStyle =
          trendColorFor(
            series.tagId,
            payload.trend);

        ctx.arc(
          px,
          py,
          3.5,
          0,
          Math.PI*2);

        ctx.fill();
      });
  }

  updateTrendTooltip();
}

function handleTrendMouseMove(event) {
  const model =
    state.trendChartModel;

  if (!model)
    return;

  const rect =
    $("trendCanvas").getBoundingClientRect();

  const x =
    event.clientX - rect.left;

  const y =
    event.clientY - rect.top;

  if (x < model.left ||
      x > model.left + model.plotW ||
      y < model.top ||
      y > model.top + model.plotH)
  {
    state.trendHover = null;
    drawTrendChart();
    return;
  }

  const ratio =
    (x - model.left) /
    model.plotW;

  const timeMs =
    model.fromMs +
    ratio *
    (model.toMs-model.fromMs);

  state.trendHover = {
    timeMs,
    x,
    y
  };

  drawTrendChart();
}

function handleTrendMouseLeave() {
  state.trendHover = null;
  drawTrendChart();
}

async function exportTrendCsv() {
  if (!state.selectedTrendId)
  {
    showToast(
      state.language === "tr"
        ? "Önce trend seçin."
        : "Select a trend first.");

    return;
  }

  try
  {
    const range =
      currentTrendRange();

    const response =
      await fetch(
        `/api/historian/export.csv` +
        `?trendId=${encodeURIComponent(state.selectedTrendId)}` +
        `&from=${encodeURIComponent(range.from.toISOString())}` +
        `&to=${encodeURIComponent(range.to.toISOString())}`);

    if (!response.ok)
      throw new Error(
        `Export failed (${response.status})`);

    const blob =
      await response.blob();

    const url =
      URL.createObjectURL(blob);

    const a =
      document.createElement("a");

    a.href = url;

    const disposition =
      response.headers.get(
        "content-disposition") || "";

    const match =
      disposition.match(
        /filename="?([^"]+)"?/i);

    a.download =
      match?.[1] ||
      `PROGNODE_Trend_${new Date().toISOString().replace(/[-:]/g, "").slice(0, 15)}.csv`;

    document.body.appendChild(a);
    a.click();
    a.remove();
    URL.revokeObjectURL(url);
  }
  catch(error)
  {
    showToast(error.message);
  }
}

function exportTrendArchive() {
  if (!state.selectedTrendId) {
    showToast(state.language === "tr" ? "Önce trend seçin." : "Select a trend first.");
    return;
  }
  const range = currentTrendRange();
  window.location.assign(
    `/api/historian/export.zip?trendId=${encodeURIComponent(state.selectedTrendId)}` +
    `&from=${encodeURIComponent(range.from.toISOString())}` +
    `&to=${encodeURIComponent(range.to.toISOString())}`);
}

function exportTrendPng() {
  const canvas =
    $("trendCanvas");

  if (!state.trendPayload)
  {
    showToast(
      state.language === "tr"
        ? "Önce trend seçin."
        : "Select a trend first.");

    return;
  }

  const a =
    document.createElement("a");

  const safe =
    (state.trendPayload.trend.name || "Trend")
      .replace(/[^a-z0-9_-]+/gi,"_");

  a.download =
    `PROGNODE_${safe}_${Date.now()}.png`;

  a.href =
    canvas.toDataURL("image/png");

  document.body.appendChild(a);
  a.click();
  a.remove();
}


async function loadHistorianStats() {
  try {
    const [stats, configs] = await Promise.all([
      api("/api/historian/stats"),
      api("/api/historian/configurations")
    ]);
    state.historianStats = stats;
    state.historianConfigurations = configs;
    renderHistorianStats();
    renderHistorianConfigurations();
    window.PrognodeStudio?.onHistorianChanged?.();
    window.dispatchEvent(new Event('prognode:historianchanged'));
    updateOverview();
    applyAccessMode();
  } catch(error) {
    console.warn("Historian load failed", error);
  }
}

function formatBytes(bytes) {
  const value = Number(bytes || 0);
  if (value < 1024) return `${value} B`;
  if (value < 1024*1024) return `${(value/1024).toFixed(1)} KB`;
  if (value < 1024*1024*1024) return `${(value/1024/1024).toFixed(1)} MB`;
  return `${(value/1024/1024/1024).toFixed(2)} GB`;
}

function renderHistorianStats() {
  if (!$("historianTotalSamples") || !state.historianStats) return;
  const s = state.historianStats;
  $("historianTotalSamples").textContent = Number(s.totalSamples || 0).toLocaleString(state.language === "tr" ? "tr-TR" : "en-US");
  $("historianRecordedTags").textContent = String(state.historianConfigurations.filter(x=>x.configuration?.enabled===true).length);
  if ($("navHistorianCount")) $("navHistorianCount").textContent = String(state.historianConfigurations.filter(x=>x.configuration?.enabled===true).length);
  if ($("navTrendCount")) {
    const available = state.historianConfigurations.filter(x=>x.configuration?.enabled===true).length;
    $("navTrendCount").textContent = String(available);
    $("navTrendCount").title = state.language === "tr"
      ? `${available} kayıtlı Tag Trend Studio'da kullanılabilir; en fazla 15 grafik aynı anda açılır.`
      : `${available} recorded Tags available in Trend Studio; up to 15 charts can be open at once.`;
  }
  $("historianDbSize").textContent = formatBytes(s.databaseBytes);

  if (s.oldestSample && s.newestSample) {
    const oldest=new Date(s.oldestSample), newest=new Date(s.newestSample);
    const days=Math.max(0,Math.round((newest-oldest)/86400000));
    $("historianTimeSpan").textContent = days > 0 ? `${days}d` : "<1d";
    $("historianTimeSpanDetail").textContent = `${oldest.toLocaleString(state.language === "tr" ? "tr-TR" : "en-US")} → ${newest.toLocaleString(state.language === "tr" ? "tr-TR" : "en-US")}`;
  } else {
    $("historianTimeSpan").textContent="—";
    $("historianTimeSpanDetail").textContent="—";
  }
}

function historianTag(status) {
  return state.tags.find(x => x.id === status.configuration.tagId) || null;
}

function historianDevice(status) {
  const tag=historianTag(status);
  return tag ? deviceName(tag.deviceId) : "—";
}

function renderHistorianConfigurations() {
  if (!$("historianConfigTableBody")) return;
  const rows=state.historianConfigurations.filter(x=>x.configuration?.enabled===true);
  if ($("navHistorianCount")) $("navHistorianCount").textContent=String(rows.length);
  $("historianEmpty").classList.toggle("hidden", rows.length > 0);
  $("historianTableWrap").classList.toggle("hidden", rows.length === 0);

  $("historianConfigTableBody").innerHTML = rows.map(status => {
    const c=status.configuration;
    const tag=historianTag(status);
    const q=(status.lastQuality || "Waiting").toLowerCase();
    return `<tr>
      <td><input type="checkbox" data-bulk-check value="${c.id}" aria-label="Select Historian configuration"></td>
      <td>${escapeHtml(historianDevice(status))}</td>
      <td><strong>${escapeHtml(tag?.name || c.tagId)}</strong></td>
      <td>${escapeHtml(intervalLabel(c.sampleIntervalSeconds))}</td>
      <td>${escapeHtml(retentionLabel(c.retentionDays))}</td>
      <td>${status.firstSample ? new Date(status.firstSample).toLocaleString(state.language === "tr" ? "tr-TR" : "en-US") : "Waiting"}</td>
      <td>${status.lastSample ? new Date(status.lastSample).toLocaleString(state.language === "tr" ? "tr-TR" : "en-US") : "—"}</td>
      <td class="historian-quality-${escapeHtml(q)}">${escapeHtml((status.lastQuality || "WAITING").toUpperCase())}</td>
      <td>${Number(status.sampleCount || 0).toLocaleString(state.language === "tr" ? "tr-TR" : "en-US")}</td>
      <td class="historian-action-row">
        <button class="historian-trend-button" data-open-historian-trend="${c.tagId}">${state.language === "tr" ? "Trend'de aç" : "Open in Trend"}</button>
        <button class="edit-button" data-edit-historian="${c.id}">${t("edit")}</button>
        <button class="historian-stop-button" data-stop-historian="${c.id}">${t("stopRecording")}</button>
        <button class="historian-csv-button" data-csv-historian="${c.tagId}" data-csv-name="${escapeHtml(tag?.name || "Historian")}">CSV ZIP ↓</button>
      </td>
    </tr>`;
  }).join("");

  renderBulkSelection('historian',$('historianConfigTableBody'),rows,x=>x.configuration.id,
    x=>api(`/api/historian/configurations/${x}?deleteData=true`,{method:'DELETE'}),async()=>loadHistorianStats());

  document.querySelectorAll("[data-edit-historian]").forEach(b => b.addEventListener("click", () => openEditHistorianModal(b.dataset.editHistorian)));
  document.querySelectorAll("[data-open-historian-trend]").forEach(b => b.addEventListener("click", () => {
    const tagId = b.dataset.openHistorianTrend;
    if (!tagId) return;
    sessionStorage.setItem("prognode.pendingHistorianTrendTag", tagId);
    navigate("trends");
    $("fullscreenTrendFrame")?.contentWindow?.postMessage({type:"pgn:open-historian-tag",tagId},location.origin);
  }));
  document.querySelectorAll("[data-csv-historian]").forEach(b => b.addEventListener("click", () => {
    const tagId = b.dataset.csvHistorian;
    window.location.assign(`/api/data-exchange/historian/${encodeURIComponent(tagId)}.zip`);
  }));

  document.querySelectorAll("[data-stop-historian]").forEach(b => b.addEventListener("click", async () => {
    const keep=confirm(state.language === "tr" ? "Historian kaydı durdurulsun ve eski veriler korunsun mu?\n\nTamam = koru\nİptal = veri silme seçeneğine geç" : "Stop Historian recording and keep existing data?\n\nOK = keep history\nCancel = choose whether to delete data");
    let deleteData=false;
    if (!keep) {
      deleteData=confirm(state.language === "tr" ? "Eski historian verilerini de KALICI olarak silmek istiyor musun?" : "Permanently delete existing Historian data for this Tag too?");
      if (!deleteData) return;
    }
    await api(`/api/historian/configurations/${b.dataset.stopHistorian}?deleteData=${deleteData}`, {method:"DELETE"});
    showToast(state.language === "tr" ? "Historian kaydı durduruldu." : "Historian recording stopped.");
    await loadHistorianStats();
  }));
}

function populateHistorianTagSelect(selectedTagId=null) {
  const configured=new Set(state.historianConfigurations.map(x => x.configuration.tagId));
  const tags=state.tags.filter(x => !configured.has(x.id) || x.id === selectedTagId);
  $("historianTag").innerHTML = tags.length ? tags.map(tag => `<option value="${tag.id}">${escapeHtml(deviceName(tag.deviceId))} • ${escapeHtml(tag.name)}</option>`).join("") : `<option value="">${state.language === "tr" ? "Tüm Taglar Historian'da" : "All Tags are already in Historian"}</option>`;
}

function openAddHistorianModal() {
  if (!requireConfigurationAccess()) return;
  if (!state.tags.length) { showToast(state.language === "tr" ? "Önce Tag ekleyin." : "Add a Tag first."); navigate("tags"); return; }
  state.editingHistorianId=null;
  populateHistorianTagSelect();
  $("historianSampleInterval").value="30";
  $("historianRetention").value="365";
  $("historianTag").disabled=false;
  $("historianModalTitle").textContent=t("historianAddTag");
  $("historianModal").classList.remove("hidden");
}

function openEditHistorianModal(id) {
  if (!requireConfigurationAccess()) return;
  const status=state.historianConfigurations.find(x => x.configuration.id === id);
  if (!status) return;
  const c=status.configuration;
  state.editingHistorianId=id;
  populateHistorianTagSelect(c.tagId);
  $("historianTag").value=c.tagId;
  $("historianTag").disabled=true;
  $("historianSampleInterval").value=String(c.sampleIntervalSeconds);
  $("historianRetention").value=String(c.retentionDays);
  $("historianModalTitle").textContent=state.language === "tr" ? "Historian Ayarı" : "Edit Historian";
  $("historianModal").classList.remove("hidden");
}

function closeHistorianModal() { $("historianModal").classList.add("hidden"); }

async function saveHistorianConfiguration() {
  if (!requireConfigurationAccess()) return;
  try {
    const body={
      tagId:$("historianTag").value,
      sampleIntervalSeconds:Number($("historianSampleInterval").value),
      retentionDays:Number($("historianRetention").value)
    };
    if (!body.tagId) throw new Error(state.language === "tr" ? "Tag seçin." : "Select a Tag.");
    if (state.editingHistorianId) {
      await api(`/api/historian/configurations/${state.editingHistorianId}`, {method:"PUT",body:JSON.stringify(body)});
    } else {
      await api("/api/historian/configurations", {method:"POST",body:JSON.stringify(body)});
    }
    closeHistorianModal();
    showToast(state.language === "tr" ? "Historian ayarı kaydedildi." : "Historian configuration saved.");
    await loadHistorianStats();
    continueCommissioningIfActive();
  } catch(error) { showToast(error.message); }
}

async function syncServerClock() {
  try {
    const payload = await api("/api/system/time");
    state.serverClock = {
      baseUtcMs: Number(payload.utcUnixMilliseconds),
      offsetMinutes: Number(payload.offsetMinutes || 0),
      perfBase: performance.now(),
      timeZone: payload.timeZone || ""
    };
    renderServerClock();
  } catch (error) {
    console.warn("Core PC clock sync failed", error);
  }
}

function renderServerClock() {
  if (!state.serverClock) return;

  const elapsed = performance.now() - state.serverClock.perfBase;
  const localMs = state.serverClock.baseUtcMs + elapsed + state.serverClock.offsetMinutes * 60000;
  const date = new Date(localMs);
  const locale = state.language === "tr" ? "tr-TR" : "en-GB";

  $("serverClockTime").textContent = new Intl.DateTimeFormat(locale, {
    timeZone:"UTC", hour:"2-digit", minute:"2-digit", second:"2-digit", hour12:false
  }).format(date);

  $("serverClockDate").textContent = new Intl.DateTimeFormat(locale, {
    timeZone:"UTC", day:"2-digit", month:"2-digit", year:"numeric"
  }).format(date);

  $("serverClock").title = state.serverClock.timeZone
    ? `PROGNODE Core PC • ${state.serverClock.timeZone}`
    : "PROGNODE Core PC time";
}

async function loadAgentStatus() {
  try {
    state.agentStatus = await api("/api/agent/status");
    renderAgentStatus();
  } catch (error) {
    state.agentStatus = null;
    renderAgentStatus();
  }
}

function renderDiagnostics() {
  if (state.page !== "diagnostics" || !$("diagnosticsDeviceRows")) return;
  const tr = state.language === "tr";
  const locale = tr ? "tr-TR" : "en-US";
  const coreRunning = String(state.health?.status || "").toLowerCase() === "running";
  const core = $("diagnosticsCoreStatus");
  core.textContent = state.health ? (coreRunning ? (tr ? "Çalışıyor" : "Running") : String(state.health.status || "—")) : "—";
  core.classList.toggle("status-good", coreRunning);
  core.classList.toggle("status-bad", !!state.health && !coreRunning);
  $("diagnosticsCoreVersion").textContent = state.health?.coreVersion ? `v${state.health.coreVersion}` : "—";
  const agentOnline = state.agentStatus?.isOnline === true;
  const agent = $("diagnosticsAgentStatus");
  agent.textContent = agentOnline ? (tr ? "Bağlı" : "Online") : (tr ? "Bağlı değil" : "Offline");
  agent.classList.toggle("status-good", agentOnline);
  agent.classList.toggle("status-bad", !agentOnline);
  $("diagnosticsAgentDetail").textContent = state.agentStatus?.machineName || "Windows";
  let good = 0, other = 0, waiting = 0;
  for (const tag of state.tags) {
    const snapshot = state.tagValues.get(tag.id);
    const quality = String(snapshot?.quality || "").toLowerCase();
    if (quality === "good") good++;
    else if (quality) other++;
    else waiting++;
  }
  $("diagnosticsTagQuality").textContent = `${good} / ${state.tags.length}`;
  $("diagnosticsTagDetail").textContent = tr ? `${other} diğer · ${waiting} bekliyor` : `${other} other · ${waiting} waiting`;
  $("diagnosticsHistorianSamples").textContent = Number(state.historianStats?.totalSamples || 0).toLocaleString(locale);
  $("diagnosticsHistorianSize").textContent = state.historianStats ? formatBytes(state.historianStats.databaseBytes) : "—";
  $("diagnosticsDeviceRows").innerHTML = state.devices.length ? state.devices.map(device => {
    const tags = state.tags.filter(tag => String(tag.deviceId) === String(device.id));
    let deviceGood = 0, deviceOther = 0, deviceWaiting = 0, latest = 0, readError = "";
    for (const tag of tags) {
      const snapshot = state.tagValues.get(tag.id);
      const quality = String(snapshot?.quality || "").toLowerCase();
      if (quality === "good") deviceGood++;
      else if (quality) { deviceOther++; if (!readError && snapshot?.error) readError = String(snapshot.error); }
      else deviceWaiting++;
      const timestamp = Date.parse(snapshot?.timestamp || "");
      if (Number.isFinite(timestamp)) latest = Math.max(latest, timestamp);
    }
    return `<tr><td><strong>${escapeHtml(device.name)}</strong></td><td>${escapeHtml(device.protocol || "—")}</td><td>${escapeHtml(device.status || "—")}</td><td>${deviceGood} / ${deviceOther} / ${deviceWaiting}</td><td>${latest ? escapeHtml(new Date(latest).toLocaleString(locale)) : "—"}</td><td class="diagnostics-read-error">${readError ? escapeHtml(readError) : "—"}</td></tr>`;
  }).join("") : `<tr><td colspan="6">${tr ? "Henüz cihaz tanımlanmadı." : "No devices configured yet."}</td></tr>`;
  $("diagnosticsUpdatedAt").textContent = `${tr ? "Güncellendi" : "Updated"}: ${new Date().toLocaleTimeString(locale)}`;
}

function renderAgentStatus() {
  const dot = $("trayAgentDot");
  const label = $("trayAgentStatus");
  const detail = $("trayAgentDetail");

  if (!dot || !label || !detail) return;

  const online = Boolean(state.agentStatus?.isOnline);
  dot.classList.toggle("ok", online);
  dot.classList.toggle("pending", !online);
  dot.classList.toggle("offline", !online);
  label.textContent = online ? t("agentOnline") : t("agentOffline");

  if (online) {
    const machine = state.agentStatus.machineName || "Windows";
    const version = state.agentStatus.version ? ` • v${state.agentStatus.version}` : "";
    detail.textContent = `${machine}${version}`;
  } else if (state.agentStatus?.lastSeenUtc) {
    const lastSeen = new Date(state.agentStatus.lastSeenUtc).toLocaleString(state.language === "tr" ? "tr-TR" : "en-US");
    detail.textContent = state.language === "tr"
      ? `Son görülme: ${lastSeen}. PROGNODE Host + Agent ile başlatın.`
      : `Last seen: ${lastSeen}. Start PROGNODE Host + Agent.`;
  } else {
    detail.textContent = state.language === "tr"
      ? "Agent çalışmıyor. VS Code: PROGNODE Host + Agent veya START_PROGNODE_WITH_AGENT.cmd kullanın."
      : "Agent is not running. Use VS Code: PROGNODE Host + Agent or START_PROGNODE_WITH_AGENT.cmd.";
  }
}

async function loadNotifications() {
  state.notifications = await api("/api/notifications");
  await loadNotificationDelivery().catch(() => {});
  try {
    const health = await api("/api/mobile/v2/notifications/health");
    const node = $("notificationStorageHealth");
    if (node) {
      node.style.display = health.storageHealthy ? "none" : "block";
      node.textContent = health.storageHealthy ? "" : (state.language === "tr"
        ? "Bildirim veritabanı hatası: " : "Notification database error: ") +
        String(health.lastStorageError || "Check SQLite/disk space");
    }
  } catch (_) { /* Restricted to locally signed-in engineer session. */ }
  renderNotifications();
  renderOverviewFeeds();
}

async function loadNotificationDelivery() {
  const policy=await api('/api/notifications/delivery');
  const global=$('notificationGlobalEnabled'),list=$('notificationClientControls');
  if(!global||!list)return;
  const tr=state.language==='tr';
  const editable=isLocalUserSignedIn()&&['OWNER','ORGANIZATION_ADMIN'].includes(state.accessSession?.portalRole);
  $('notificationDeliveryTitle').textContent=tr?'Bildirim teslimi':'Notification delivery';
  $('notificationDeliveryHelp').textContent=tr?'Teslimi kapatmak alarm ve bildirim geçmişini silmez. Kapalıyken yeni uyarılar gönderilmez.':'Pausing delivery keeps alarm and notification history. New alerts are not delivered while paused.';
  $('notificationGlobalLabel').textContent=tr?'Bildirimleri gönder':'Deliver notifications';
  global.checked=!!policy.enabled;global.disabled=!editable;
  list.innerHTML=(policy.clients||[]).length?(policy.clients||[]).map(client=>`<label class="notification-delivery-row"><span><strong>${escapeHtml(client.name)}</strong><small>${escapeHtml(client.platform||'')} · ${escapeHtml(client.clientId)}</small></span><input type="checkbox" data-notification-client="${escapeHtml(client.clientId)}" ${client.notificationsEnabled?'checked':''} ${editable?'':'disabled'}></label>`).join(''):`<p class="panel-copy">${tr?'Henüz eşleşmiş istemci yok.':'No paired clients yet.'}</p>`;
}

async function updateNotificationDelivery(clientId,enabled) {
  const path=clientId?`/api/notifications/delivery/clients/${encodeURIComponent(clientId)}`:'/api/notifications/delivery';
  await api(path,{method:'PUT',body:JSON.stringify({enabled})});
  await loadNotificationDelivery();
  showToast(state.language==='tr'?'Bildirim teslimi güncellendi.':'Notification delivery updated.');
}

function renderNotifications() {
  const box = $("notificationEvents");

  if (!box)
    return;

  box.innerHTML =
    state.notifications.length
      ? state.notifications
          .map(n => `
            <div class="event-item">
              <strong>${escapeHtml(n.title)}</strong>
              <span>${escapeHtml(n.message)}</span>
              <span>${
                new Date(n.timestamp)
                  .toLocaleString(
                    state.language === "tr"
                      ? "tr-TR"
                      : "en-US")
              }</span>
            </div>
          `).join("")
      : `<div class="event-item">
          <span>${t("noNotificationEvents")}</span>
        </div>`;
}

async function sendTestNotification() {
  try {
    const notification =
      await api(
        "/api/notifications/test",
        {
          method:"POST",
          body:JSON.stringify({
            title:"PROGNODE",
            message:
              state.language === "tr"
                ? "Windows Agent bildirim yolu çalışıyor."
                : "Windows Agent notification path is working."
          })
        });

    state.notifications =
      [notification, ...state.notifications]
        .slice(0,20);

    renderNotifications();
    if (state.agentStatus?.isOnline) {
      showToast(t("testNotificationQueued"));
    } else {
      showToast(state.language === "tr"
        ? "Bildirim Core'a kaydedildi fakat Windows Agent çalışmıyor."
        : "Notification was queued in Core, but Windows Agent is not running.");
    }
  }
  catch(error) {
    showToast(error.message);
  }
}


// ============================================================================
// V0.7.1 DEV1 — operational dashboard, Notification Center and CSV data exchange
// ============================================================================

function dev3Count(value) {
  return Number(value || 0).toLocaleString(state.language === "tr" ? "tr-TR" : "en-US");
}

function overviewRelativeTime(value) {
  if (!value) return "";
  const ms = Date.now() - new Date(value).getTime();
  if (!Number.isFinite(ms)) return "";
  const sec = Math.max(0, Math.round(ms / 1000));
  if (sec < 60) return state.language === "tr" ? "şimdi" : "now";
  const min = Math.round(sec / 60);
  if (min < 60) return state.language === "tr" ? `${min} dk önce` : `${min} min ago`;
  const hr = Math.round(min / 60);
  if (hr < 24) return state.language === "tr" ? `${hr} sa önce` : `${hr} hr ago`;
  const day = Math.round(hr / 24);
  return state.language === "tr" ? `${day} gün önce` : `${day} d ago`;
}

function overviewSeverityClass(value) {
  const s = String(value || "").toLowerCase();
  if (s.includes("critical") || s.includes("high")) return "danger";
  if (s.includes("medium") || s.includes("warning")) return "warning";
  if (s.includes("clear") || s.includes("restore") || s.includes("good") || s.includes("online")) return "success";
  return "info";
}

function overviewFeedItem({title, text, meta, severity, badge}) {
  const cls = overviewSeverityClass(severity || badge || text || title);
  return `<div class="overview-feed-item pro ${cls}">
    <span class="overview-feed-dot"></span>
    <div class="overview-feed-copy"><strong>${escapeHtml(title)}</strong><span>${escapeHtml(text || "")}</span></div>
    <div class="overview-feed-meta">${badge ? `<em>${escapeHtml(String(badge).toUpperCase())}</em>` : ""}${meta ? `<small>${escapeHtml(meta)}</small>` : ""}</div>
  </div>`;
}

function renderOverviewFeeds() {
  const alarmList = $("overviewAlarmList");
  const notificationList = $("overviewNotificationList");

  if (alarmList) {
    const alarms = (state.activeAlarms || []).slice(0, 4);
    $("overviewAlarmHeading").textContent = state.language === "tr" ? `${alarms.length} Aktif` : `${alarms.length} Active`;
    $("overviewAlarmHeading").classList.toggle("has-alerts", alarms.length > 0);
    alarmList.innerHTML = alarms.length
      ? alarms.map(a => overviewFeedItem({
          title: `${a.sourceName || "PROGNODE"} • ${a.text || "Alarm"}`,
          text: a.isSystem ? (state.language === "tr" ? "Sistem alarmı" : "System alarm") : (a.tagName || a.source || ""),
          meta: overviewRelativeTime(a.activeSince || a.lastChangedAt),
          severity: a.priority,
          badge: a.priority
        })).join("")
      : `<div class="overview-feed-empty"><span class="status-dot ok"></span>${state.language === "tr" ? "Sistem sakin. Aktif alarm yok." : "System quiet. No active alarms."}</div>`;
  }

  if (notificationList) {
    const items = (state.notifications || []).slice(0, 5);
    if ($("overviewNotificationHeading")) {
      $("overviewNotificationHeading").textContent = state.language === "tr" ? `${items.length} Son` : `${items.length} Recent`;
    }
    notificationList.innerHTML = items.length
      ? items.map(n => `<button type="button" class="overview-notification-action" data-open-notification-alarm="${escapeHtml(String(n.id||''))}" aria-label="${escapeHtml(state.language==='tr'?'Alarm sayfasını aç':'Open alarms page')}">${overviewFeedItem({
          title: n.title || n.source || "PROGNODE",
          text: n.message || "",
          meta: overviewRelativeTime(n.timestamp || n.createdAt),
          severity: n.severity
        })}</button>`).join("")
      : `<div class="overview-feed-empty">${t("noNotificationEvents")}</div>`;
  }
}

function renderOverviewDeviceHealth() {
  const total = state.devices.length;
  const offlineIds = new Set(
    (state.activeAlarms || [])
      .filter(a => String(a.text || "").toLowerCase().includes("communication lost"))
      .map(a => String(a.deviceId || ""))
      .filter(Boolean)
  );

  const degradedIds = new Set();
  for (const tag of state.tags || []) {
    if (offlineIds.has(String(tag.deviceId))) continue;
    const snapshot = state.tagValues.get(tag.id) || state.tagValues.get(String(tag.id));
    const q = String(snapshot?.quality || "").toLowerCase();
    if (q && q !== "good" && q !== "waiting") degradedIds.add(String(tag.deviceId));
  }

  const offline = Math.min(total, offlineIds.size);
  const degraded = Math.min(Math.max(total - offline, 0), degradedIds.size);
  const maintenance = 0;
  const online = Math.max(total - offline - degraded - maintenance, 0);
  const pct = value => total > 0 ? `${Math.round(value / total * 100)}%` : "0%";

  if ($("overviewDeviceTotal")) $("overviewDeviceTotal").textContent = dev3Count(total);
  if ($("overviewDeviceOnline")) $("overviewDeviceOnline").textContent = dev3Count(online);
  if ($("overviewDeviceDegraded")) $("overviewDeviceDegraded").textContent = dev3Count(degraded);
  if ($("overviewDeviceOffline")) $("overviewDeviceOffline").textContent = dev3Count(offline);
  if ($("overviewDeviceMaintenance")) $("overviewDeviceMaintenance").textContent = dev3Count(maintenance);
  if ($("overviewDeviceOnlinePct")) $("overviewDeviceOnlinePct").textContent = pct(online);
  if ($("overviewDeviceDegradedPct")) $("overviewDeviceDegradedPct").textContent = pct(degraded);
  if ($("overviewDeviceOfflinePct")) $("overviewDeviceOfflinePct").textContent = pct(offline);
  if ($("overviewDeviceMaintenancePct")) $("overviewDeviceMaintenancePct").textContent = pct(maintenance);
  if ($("overviewDeviceHealthHeading")) $("overviewDeviceHealthHeading").textContent = `${dev3Count(total)} ${t("devices")}`;

  const donut = $("overviewDeviceDonut");
  if (donut) {
    const denom = Math.max(total, 1);
    const p1 = online / denom * 100;
    const p2 = p1 + degraded / denom * 100;
    const p3 = p2 + offline / denom * 100;
    donut.style.background = total
      ? `conic-gradient(var(--green) 0 ${p1}%, var(--yellow) ${p1}% ${p2}%, var(--danger) ${p2}% ${p3}%, #68849a ${p3}% 100%)`
      : `conic-gradient(#173247 0 100%)`;
  }
}

function renderOverviewHistorian() {
  const stats = state.historianStats || {};
  if ($("overviewHistorianDb")) $("overviewHistorianDb").textContent = formatBytes(Number(stats.databaseBytes || 0));

  const configs = (state.historianConfigurations || []).filter(x=>x.configuration?.enabled===true);
  if ($("overviewHistorianHeading")) {
    $("overviewHistorianHeading").textContent = state.language === "tr"
      ? `${configs.length} Tag kaydediliyor`
      : `Recording ${configs.length} tag${configs.length === 1 ? "" : "s"}`;
  }
  const bad = configs.filter(x => {
    const q = String(x.lastQuality || "waiting").toLowerCase();
    return q !== "good" && q !== "waiting";
  }).length;
  if ($("overviewHistorianQuality")) {
    $("overviewHistorianQuality").textContent = bad === 0
      ? (state.language === "tr" ? "İyi" : "Good")
      : (state.language === "tr" ? `${bad} sorun` : `${bad} issue${bad === 1 ? "" : "s"}`);
    $("overviewHistorianQuality").classList.toggle("danger-text", bad > 0);
  }

  const chart = $("overviewHistorianActivity");
  if (chart) {
    const values = configs.map(x => Number(x.sampleCount || 0)).filter(Number.isFinite);
    if (values.length) {
      const max = Math.max(...values, 1);
      chart.innerHTML = values.slice(0, 18).map((v, i) => {
        const pct = Math.max(8, Math.round(v / max * 100));
        return `<i style="--bar:${pct}%" title="${dev3Count(v)}"></i>`;
      }).join("");
    } else {
      chart.innerHTML = `<span class="historian-chart-empty">${state.language === "tr" ? "Henüz historian noktası yok" : "No historian points yet"}</span>`;
    }
  }
}

function overviewHealthCounts() {
  const total = state.devices.length;
  const offlineIds = new Set(
    (state.activeAlarms || [])
      .filter(a => String(a?.definition?.text || a?.text || "").toLowerCase().includes("communication lost"))
      .map(a => String(a?.deviceId || a?.definition?.deviceId || ""))
      .filter(Boolean));
  const degradedIds = new Set();
  for (const tag of state.tags || []) {
    if (offlineIds.has(String(tag.deviceId))) continue;
    const snapshot = state.tagValues.get(tag.id) || state.tagValues.get(String(tag.id));
    const q = String(snapshot?.quality || "").toLowerCase();
    if (q && q !== "good" && q !== "waiting") degradedIds.add(String(tag.deviceId));
  }
  const offline = Math.min(total, offlineIds.size);
  const degraded = Math.min(Math.max(total - offline, 0), degradedIds.size);
  return { total, offline, degraded, online:Math.max(total - offline - degraded, 0) };
}

function updateOverviewCommandStrip() {
  if (!$("overviewCommandStrip")) return;
  const health = overviewHealthCounts();
  const alarms = state.activeAlarms.length;
  const recording = state.historianConfigurations.filter(x=>x.configuration?.enabled===true).length;
  const access = hasConfigurationAccess();
  const coreRunning = String(state.health?.status || "Running").toLowerCase() === "running";
  const licenseValid = Boolean(state.license?.isValid);
  const needsAttention = alarms > 0 || health.offline > 0 || health.degraded > 0 || !coreRunning;

  $("overviewPosture").textContent = needsAttention ? t("attentionRequired") : t("systemStable");
  $("overviewPosture").classList.toggle("danger-text", needsAttention);
  $("overviewPosture").classList.toggle("healthy-text", !needsAttention);
  $("overviewStatusBeacon")?.classList.toggle("attention", needsAttention);
  $("overviewStatusBeacon")?.classList.toggle("stable", !needsAttention);

  const detail = [];
  if (!coreRunning) detail.push(state.language === "tr" ? "Core durumu kontrol edilmeli" : "Core health requires attention");
  if (health.offline) detail.push(state.language === "tr" ? `${health.offline} cihaz offline` : `${health.offline} device${health.offline===1?'':'s'} offline`);
  if (health.degraded) detail.push(state.language === "tr" ? `${health.degraded} cihaz zayıf` : `${health.degraded} device${health.degraded===1?'':'s'} degraded`);
  if (alarms) detail.push(state.language === "tr" ? `${alarms} aktif alarm` : `${alarms} active alarm${alarms===1?'':'s'}`);
  if (!detail.length) detail.push(state.language === "tr" ? "Aktif kritik durum yok" : "No active critical conditions");
  $("overviewPostureDetail").textContent = detail.join(" • ");
  if ($("overviewLastRefresh")) $("overviewLastRefresh").textContent = state.language === "tr" ? "Canlı yerel durum" : "Live local status";
  if ($("systemHealthLabel")) $("systemHealthLabel").textContent = needsAttention
    ? (state.language === "tr" ? "Dikkat gerekiyor" : "Attention required")
    : (state.language === "tr" ? "Sistem stabil" : "System stable");
  if ($("systemHealthDot")) {
    $("systemHealthDot").classList.toggle("ok", !needsAttention);
    $("systemHealthDot").classList.toggle("alert", needsAttention);
  }
  if ($("systemHealthPill")) $("systemHealthPill").classList.toggle("attention", needsAttention);

  $("overviewConnectivity").textContent = `${health.online}/${health.total} online`;
  if ($("overviewConnectivityDetail")) $("overviewConnectivityDetail").textContent = health.total === 0
    ? (state.language === "tr" ? "Henüz cihaz yok" : "No devices configured")
    : health.offline > 0
      ? (state.language === "tr" ? `${health.offline} offline` : `${health.offline} offline`)
      : (state.language === "tr" ? "Tüm cihazlar erişilebilir" : "All devices reachable");

  $("overviewAlarmPosture").textContent = alarms ? `${alarms} ${state.language === "tr" ? "aktif" : "active"}` : (state.language === "tr" ? "Temiz" : "Clear");
  if ($("overviewAlarmDetail")) $("overviewAlarmDetail").textContent = alarms
    ? (state.language === "tr" ? "Operatör ilgisi gerekiyor" : "Operator attention required")
    : (state.language === "tr" ? "Aktif alarm yok" : "No active alarms");

  $("overviewRecordingPosture").textContent = recording ? `${recording} ${state.language === "tr" ? "Tag" : `tag${recording===1?'':'s'}`}` : (state.language === "tr" ? "Kapalı" : "Not configured");
  if ($("overviewRecordingDetail")) $("overviewRecordingDetail").textContent = recording
    ? (state.language === "tr" ? "Yerel kayıt aktif" : "Local recording active")
    : (state.language === "tr" ? "Historian bekliyor" : "Historian idle");

  if ($("overviewCorePosture")) {
    $("overviewCorePosture").textContent = coreRunning ? t("healthy") : (state.health?.status || "Unknown");
    $("overviewCorePosture").classList.toggle("healthy-text", coreRunning);
    $("overviewCorePosture").classList.toggle("danger-text", !coreRunning);
  }
  if ($("overviewCoreDetail")) $("overviewCoreDetail").textContent = state.health?.coreVersion ? `PROGNODE ${state.health.coreVersion}` : "PROGNODE Core";

  const licenseLifecycle = String(state.license?.status || "").toUpperCase();
  const licenseWarning = licenseLifecycle === "EXPIRING_SOON";
  const licenseCritical = licenseLifecycle === "GRACE" || licenseLifecycle === "EXPIRED" || licenseLifecycle === "INVALID";
  if ($("overviewLicensePosture")) {
    $("overviewLicensePosture").textContent = licenseLifecycle === "GRACE"
      ? (state.language === "tr" ? "Grace • yenileme gerekli" : "Grace • renewal required")
      : licenseLifecycle === "EXPIRING_SOON"
        ? (state.language === "tr" ? "Yakında sona eriyor" : "Expiring soon")
        : (licenseValid ? licenseProductLabel(state.license?.plan) : (state.language === "tr" ? "Lisans yok" : "Not licensed"));
    $("overviewLicensePosture").classList.toggle("warning-text", licenseWarning);
    $("overviewLicensePosture").classList.toggle("danger-text", licenseCritical);
  }
  if ($("overviewLicenseDetail")) $("overviewLicenseDetail").textContent = licenseValid ? licenseRemainingText(state.license) : (state.language === "tr" ? ".pgnlicense içe aktarın" : "Import .pgnlicense");

  if ($("overviewAccessPosture")) {
    $("overviewAccessPosture").textContent = access ? t("signedInLocal") : t("viewOnly");
    $("overviewAccessPosture").classList.toggle("healthy-text", access);
  }
  if ($("overviewAccessDetail")) $("overviewAccessDetail").textContent = access
    ? (state.language === "tr" ? "Yapılandırma açık" : "Configuration enabled")
    : (state.language === "tr" ? "Yapılandırmak için giriş yapın" : "Sign in to configure");

  updateCommissioningAssistant();
}

function updateOverview() {
  const deviceCount = state.devices.length;
  const tagCount = state.tags.length;
  const historianCount = state.historianConfigurations.filter(x=>x.configuration?.enabled===true).length;
  const activeAlarmCount = state.activeAlarms.length;
  const historianPoints = Number(state.historianStats?.totalSamples || 0);

  $("metricDevices").textContent = dev3Count(deviceCount);
  $("metricTags").textContent = dev3Count(tagCount);
  $("metricActiveAlarms").textContent = dev3Count(activeAlarmCount);
  $("metricRecordingTags").textContent = dev3Count(historianCount);
  $("metricHistorianPoints").textContent = dev3Count(historianPoints);
  if ($("overviewRecordingTags")) $("overviewRecordingTags").textContent = dev3Count(historianCount);
  if ($("overviewHistorianPoints")) $("overviewHistorianPoints").textContent = dev3Count(historianPoints);

  $("metricDevicesSub").textContent = deviceCount === 0
    ? t("noDevicesConfigured")
    : (state.language === "tr" ? `${deviceCount} yapılandırılmış cihaz` : `${deviceCount} configured device${deviceCount === 1 ? "" : "s"}`);
  $("metricTagsSub").textContent = tagCount === 0
    ? (state.language === "tr" ? "Henüz canlı Tag yok" : "No live Tags yet")
    : (state.language === "tr" ? `${tagCount} runtime Tag` : `${tagCount} runtime Tag${tagCount === 1 ? "" : "s"}`);
  $("metricAlarmSub").textContent = activeAlarmCount === 0
    ? t("noActiveAlarmsNow")
    : (state.language === "tr" ? "Operatör ilgisi gerekiyor" : "Operator attention required");
  $("metricRecordingSub").textContent = historianCount === 0
    ? (state.language === "tr" ? "Historian yapılandırılmadı" : "Historian not configured")
    : (state.language === "tr" ? "Yerel kayıt aktif" : "Local recording configured");
  $("metricHistorianSub").textContent = t("localRecording");

  const coreRunning = String(state.health?.status || "Running").toLowerCase() === "running";
  $("metricCoreHealth").textContent = coreRunning ? t("healthy") : (state.health?.status || "Unknown");
  $("metricCoreHealth").classList.toggle("healthy-text", coreRunning);
  $("metricCoreHealth").classList.toggle("danger-text", !coreRunning);
  $("metricCoreVersion").textContent = state.health?.coreVersion ? `PROGNODE ${state.health.coreVersion}` : "PROGNODE";

  const hero = $("overviewHero");
  if (hero) hero.classList.add("operational");
  if ($("overviewKicker")) $("overviewKicker").textContent = t("prognodeIndustrialMonitoring");
  if ($("overviewHeadline")) $("overviewHeadline").textContent = t("criticalEquipment");
  if ($("overviewSummary")) $("overviewSummary").textContent = t("resilientTomorrow");

  updateOverviewCommandStrip();
  renderOverviewDeviceHealth();
  renderOverviewHistorian();
  renderOverviewFeeds();
  if (typeof window.customerV2Refresh === "function") window.customerV2Refresh({
    devices: deviceCount, tags: tagCount, alarms: activeAlarmCount,
    recording: historianCount, samples: historianPoints,
    licensed: Boolean(state.license?.isValid) && !['REVOKED','EXPIRED','INVALID'].includes(String(state.license?.status||'').toUpperCase()), coreHealthy: String(state.health?.status||'').toLowerCase()==='running', language: state.language
  });
}

function renderAgentStatus() {
  const dot = $("trayAgentDot");
  const label = $("trayAgentStatus");
  const detail = $("trayAgentDetail");
  const online = Boolean(state.agentStatus?.isOnline);

  if (dot) {
    dot.classList.toggle("ok", online);
    dot.classList.toggle("pending", !online);
    dot.classList.toggle("offline", !online);
  }
  if (label) label.textContent = online ? t("agentOnline") : t("agentOffline");

  let detailText;
  if (online) {
    const machine = state.agentStatus.machineName || "Windows";
    const version = state.agentStatus.version ? ` • v${state.agentStatus.version}` : "";
    detailText = `${machine}${version}`;
  } else if (state.agentStatus?.lastSeenUtc) {
    const lastSeen = new Date(state.agentStatus.lastSeenUtc).toLocaleString(state.language === "tr" ? "tr-TR" : "en-US");
    detailText = state.language === "tr" ? `Son görülme: ${lastSeen}` : `Last seen: ${lastSeen}`;
  } else {
    detailText = state.language === "tr" ? "Agent çalışmıyor. PROGNODE Host + Agent ile başlatın." : "Agent is not running. Start PROGNODE Host + Agent.";
  }
  if (detail) detail.textContent = detailText;

  if ($("notificationAgentMetric")) $("notificationAgentMetric").textContent = online ? t("agentOnline") : t("agentOffline");
  if ($("notificationAgentMetricSub")) $("notificationAgentMetricSub").textContent = detailText;
  if ($("notificationDeliveryBadge")) {
    const mode = state.agentStatus?.notificationMode || (online ? "Windows Agent" : "LOCAL ONLY");
    $("notificationDeliveryBadge").textContent = mode.toUpperCase();
    $("notificationDeliveryBadge").classList.toggle("active", online && mode.toLowerCase().includes("action"));
  }
}

function dev3NotificationMatches(item, filter) {
  const severity = String(item.severity || "").toLowerCase();
  const haystack = `${item.title || ""} ${item.message || ""}`.toLowerCase();
  if (filter === "critical") return severity === "critical" || severity === "high";
  if (filter === "alarm") return haystack.includes("alarm") || ["critical","high","medium","low"].includes(severity);
  if (filter === "system") return haystack.includes("communication") || haystack.includes("system") || haystack.includes("agent") || haystack.includes("core");
  return true;
}

function renderNotifications() {
  const box = $("notificationEvents");
  if (!box) return;
  const filter = $("notificationFilter")?.value || "all";
  const all = state.notifications || [];
  const items = all.filter(n => dev3NotificationMatches(n, filter));
  const criticalCount = all.filter(n => ["critical","high"].includes(String(n.severity || "").toLowerCase())).length;

  if ($("notificationEventMetric")) $("notificationEventMetric").textContent = dev3Count(all.length);
  if ($("notificationCriticalMetric")) $("notificationCriticalMetric").textContent = dev3Count(criticalCount);
  if ($("navNotificationCount")) {
    $("navNotificationCount").textContent = dev3Count(all.length);
    $("navNotificationCount").classList.toggle("hidden", all.length === 0);
  }

  box.innerHTML = items.length
    ? items.map(n => {
        const severity = String(n.severity || "information").toLowerCase();
        const when = n.timestamp ? new Date(n.timestamp).toLocaleString(state.language === "tr" ? "tr-TR" : "en-US") : "";
        return `<button type="button" class="notification-event severity-${escapeHtml(severity)}" data-open-notification-alarm="${escapeHtml(String(n.id||''))}" aria-label="${escapeHtml(state.language==='tr'?'Alarm sayfasını aç':'Open alarms page')}">
          <span class="notification-event-severity" title="${escapeHtml(n.severity || "Information")}"></span>
          <div class="notification-event-copy"><strong>${escapeHtml(n.title || "PROGNODE")}</strong><span>${escapeHtml(n.message || "")}</span></div>
          <time>${escapeHtml(when)}</time>
        </button>`;
      }).join("")
    : `<div class="overview-feed-item empty">${t("noNotificationEvents")}</div>`;

  renderOverviewFeeds();
}

function dev3NormalizeHeader(value) {
  return String(value || "").trim().replace(/^\uFEFF/, "").toLowerCase().replace(/[^a-z0-9]/g, "");
}

function dev3DetectDelimiter(source) {
  const lines = String(source || "").replace(/^\uFEFF/, "").split(/\r?\n/);
  const firstDataLine = lines.find(line => line.trim() && !/^sep\s*=\s*./i.test(line.trim())) || "";
  const candidates = [";", ",", "\t"];
  let best = ";";
  let bestCount = -1;

  for (const delimiter of candidates) {
    let quoted = false;
    let count = 0;
    for (let i = 0; i < firstDataLine.length; i++) {
      const c = firstDataLine[i];
      if (c === '"') {
        if (quoted && firstDataLine[i + 1] === '"') { i++; continue; }
        quoted = !quoted;
      } else if (!quoted && c === delimiter) {
        count++;
      }
    }
    if (count > bestCount) {
      best = delimiter;
      bestCount = count;
    }
  }
  return best;
}

function dev3ParseCsv(text) {
  const rows = [];
  let row = [], field = "", quoted = false;
  let source = String(text || "").replace(/^\uFEFF/, "");

  // Excel can add a separator directive on the first line. It is metadata,
  // not a CSV row, so remove it before parsing.
  source = source.replace(/^sep\s*=\s*[^\r\n]+\r?\n/i, "");
  const delimiter = dev3DetectDelimiter(source);

  for (let i = 0; i < source.length; i++) {
    const c = source[i];
    if (quoted) {
      if (c === '"' && source[i + 1] === '"') { field += '"'; i++; }
      else if (c === '"') quoted = false;
      else field += c;
    } else if (c === '"') {
      quoted = true;
    } else if (c === delimiter) {
      row.push(field.trim());
      field = "";
    } else if (c === '\n') {
      row.push(field.trim());
      if (row.some(x => x !== "")) rows.push(row);
      row = [];
      field = "";
    } else if (c !== '\r') {
      field += c;
    }
  }

  row.push(field.trim());
  if (row.some(x => x !== "")) rows.push(row);
  if (rows.length < 2) return [];

  const headers = rows[0].map(dev3NormalizeHeader);
  return rows.slice(1).map((values, index) => {
    const obj = { __row: index + 2, __delimiter: delimiter };
    headers.forEach((h, i) => obj[h] = values[i] ?? "");
    return obj;
  });
}

function dev3Cell(row, ...names) {
  for (const name of names) {
    const key = dev3NormalizeHeader(name);
    if (Object.prototype.hasOwnProperty.call(row, key)) return String(row[key] ?? "").trim();
  }
  return "";
}

function dev3Bool(value, fallback = false) {
  const v = String(value ?? "").trim().toLowerCase();
  if (!v) return fallback;
  return ["1","true","yes","y","on","evet","aktif"].includes(v);
}

function dev3Number(value, fallback = 0) {
  const v = String(value ?? "").trim().replace(",", ".");
  if (!v) return fallback;
  const n = Number(v);
  return Number.isFinite(n) ? n : NaN;
}

function dev3Datatype(value) {
  const v = String(value || "").trim().toUpperCase().replace(/[\s_/-]/g, "");
  const map = { BOOL:"Bool",BOOLEAN:"Bool",WORD:"Word",UINT16:"UInt16",UINT:"UInt16",INT16:"Int16",INT:"Int16",UINT32:"UInt32",UDINT:"UInt32",INT32:"Int32",DINT:"Int32",FLOAT32:"Float32",FLOAT:"Float32",REAL:"Float32" };
  return map[v] || null;
}

function dev3Priority(value) {
  const raw = String(value || "Medium").trim().toLowerCase();
  return ({low:"Low",medium:"Medium",high:"High",critical:"Critical"})[raw] || null;
}

function dev3Protocol(value) {
  const key = String(value || "Modbus TCP").toLowerCase().replace(/[^a-z0-9]/g, "");
  return ({modbus:"Modbus TCP",modbustcp:"Modbus TCP",s7:"Siemens S7 TCP",s7tcp:"Siemens S7 TCP",siemenss7tcp:"Siemens S7 TCP",mqtt:"MQTT",opcua:"OPC UA"})[key] || null;
}

function dev3ImportExistingMode(){return $("importExistingMode")?.value==='skip'?'skip':'update'}
function dev3ImportIdentity(dataset,payload){const body=payload?.body;if(!body)return null;
  if(dataset==='devices')return state.devices.find(d=>d.name.toLowerCase()===body.name.toLowerCase())||null;
  if(dataset==='tags')return state.tags.find(t=>t.name.toLowerCase()===body.name.toLowerCase())||null;
  if(dataset==='alarms')return state.alarmDefinitions.find(a=>a.tagId===body.tagId&&a.text.toLowerCase()===body.text.toLowerCase())||null;
  if(dataset==='historian')return state.historianConfigurations.find(x=>x.configuration.tagId===body.tagId)?.configuration||null;
  return null;
}
function dev3ImportRowKey(dataset,payload){const b=payload?.body;if(!b)return '';
  if(dataset==='devices')return b.name.toLowerCase();
  if(dataset==='tags')return b.name.toLowerCase();
  if(dataset==='alarms')return `${b.tagId}:${b.text.toLowerCase()}`;
  if(dataset==='historian')return String(b.tagId);
  return '';
}

function dev3PreviewRow(row, dataset) {
  const errors = [];
  let payload = null;
  let preview = "";
  if (dataset === "devices") {
    const name = dev3Cell(row, "Name", "Device");
    const protocol = dev3Protocol(dev3Cell(row, "Protocol"));
    const host = dev3Cell(row, "Host", "IP", "HostIP", "EndpointUrl");
    const defaultPort = protocol === "Siemens S7 TCP" ? 102 : protocol === "MQTT" ? 8883 : protocol === "OPC UA" ? 4840 : 502;
    const port = dev3Number(dev3Cell(row, "Port"), defaultPort);
    const unitId = dev3Number(dev3Cell(row, "UnitId", "Unit ID", "SlaveId"), protocol === "Modbus TCP" ? 1 : 0);
    const pollIntervalMs = dev3Number(dev3Cell(row, "PollIntervalMs", "Poll", "PollMs"), 1000);
    if (!name) errors.push("Name is required");
    if (state.tags.some(t => t.name.toLowerCase() === name.toLowerCase())) errors.push("Name is already used by a Tag");
    if (!protocol) errors.push("Protocol must be Modbus TCP, Siemens S7 TCP, MQTT or OPC UA");
    if (protocol) {
      if (!host) errors.push("Host / IP is required");
      if (!Number.isInteger(port) || port < 1 || port > 65535) errors.push("Port must be 1..65535");
      if (!Number.isInteger(pollIntervalMs) || pollIntervalMs < 100 || pollIntervalMs > 3600000) errors.push("Poll interval must be 100..3600000 ms");
      if (protocol === "Modbus TCP") {
        if (!Number.isInteger(unitId) || unitId < 0 || unitId > 255) errors.push("Unit ID must be 0..255");
        payload = { kind:"modbus", body:{name,host,port,unitId,pollIntervalMs} };
        preview = `${name || "?"} • ${host || "?"}:${port} • Unit ${unitId}`;
      } else if (protocol === "Siemens S7 TCP") {
        const rackRaw = dev3Cell(row, "Rack");
        const slotRaw = dev3Cell(row, "Slot");
        const rack = rackRaw === "" ? Math.floor(unitId / 32) : dev3Number(rackRaw, NaN);
        const slot = slotRaw === "" ? unitId % 32 : dev3Number(slotRaw, NaN);
        if (!Number.isInteger(rack) || rack < 0 || rack > 7) errors.push("S7 Rack must be 0..7");
        if (!Number.isInteger(slot) || slot < 0 || slot > 31) errors.push("S7 Slot must be 0..31");
        payload = { kind:"s7", body:{name,host,port,rack,slot,pollIntervalMs} };
        preview = `${name || "?"} • ${host || "?"}:${port} • Rack ${rack} / Slot ${slot}`;
      } else if (protocol === "MQTT") {
        const broker = host.includes("://") ? host : `mqtts://${host}`;
        try {
          const uri = new URL(broker);
          if (!["mqtt:","mqtts:"].includes(uri.protocol) || !uri.hostname || uri.username || uri.password || uri.port || uri.pathname !== "" && uri.pathname !== "/" || uri.search || uri.hash)
            throw new Error("invalid broker");
        } catch { errors.push("MQTT Host must be a broker name or mqtt:// / mqtts:// URL without credentials, port or path"); }
        payload = { kind:"mqtt", body:{name,host,port,pollIntervalMs} };
        preview = `${name || "?"} • ${host || "?"}:${port} • MQTT`;
      } else if (protocol === "OPC UA") {
        try {
          const uri = new URL(host);
          if (uri.protocol !== "opc.tcp:" || !uri.hostname || !uri.port || uri.username || uri.password || uri.search || uri.hash || Number(uri.port) !== port)
            throw new Error("invalid endpoint");
        } catch { errors.push("OPC UA Host must be opc.tcp://host:port/path and Port must match the URL"); }
        payload = { kind:"opcua", body:{name,endpointUrl:host,pollIntervalMs} };
        preview = `${name || "?"} • ${host || "?"} • OPC UA`;
      }
    }
  } else if (dataset === "tags") {
    const deviceNameValue = dev3Cell(row, "Device", "DeviceName");
    const device = state.devices.find(d => d.name.toLowerCase() === deviceNameValue.toLowerCase());
    const name = dev3Cell(row, "Name", "Tag", "TagName");
    const address = dev3Cell(row, "Address", "Register");
    const dataType = dev3Datatype(dev3Cell(row, "DataType", "Datatype", "Type"));
    const bitRaw = dev3Cell(row, "BitIndex", "Bit");
    const bitIndex = bitRaw === "" ? null : dev3Number(bitRaw, NaN);
    const byteOrder = (dev3Cell(row, "ByteOrder", "WordOrder") || "ABCD").toUpperCase();
    const unit = dev3Cell(row, "Unit");
    const offset = dev3Number(dev3Cell(row, "Offset"), 0);
    const decimalPlaces = dev3Number(dev3Cell(row, "DecimalPlaces", "DecimalPoint", "Decimals"), 0);
    if (!device) errors.push(`Unknown device: ${deviceNameValue || "(blank)"}`);
    if (!name) errors.push("Tag name is required");
    if (state.devices.some(d => d.name.toLowerCase() === name.toLowerCase())) errors.push("Name is already used by a Device");
    if (!dataType) errors.push("Unsupported datatype");
    if (!["ABCD","CDAB","BADC","DCBA"].includes(byteOrder)) errors.push("ByteOrder must be ABCD/CDAB/BADC/DCBA");
    let effectiveBitIndex = null;
    if (device?.protocol === "Modbus TCP") {
      const addressInfo = modbusAddressInfo(address);
      if (!addressInfo) errors.push("Address must be in 00001/10001/30001/40001 Modbus read ranges");
      if (addressInfo?.bitArea && dataType && dataType !== "Bool") errors.push("Coil / Discrete Input requires BOOL datatype");
      if (addressInfo?.area === "Input Register" && dataType === "Bool") errors.push("Input Register (3xxxx) does not support BOOL bit access");
      if (dataType === "Bool" && addressInfo && !addressInfo.bitArea && addressInfo.area !== "Input Register" && (bitIndex === null || !Number.isInteger(bitIndex) || bitIndex < 0 || bitIndex > 15)) errors.push("Register BOOL requires BitIndex 0..15");
      if (addressInfo && !addressInfo.bitArea && dataType && Number(address) + tagRegisterWidth(dataType) - 1 > addressInfo.last) errors.push("Datatype width exceeds Modbus area boundary");
      effectiveBitIndex = dataType === "Bool" && addressInfo && !addressInfo.bitArea ? bitIndex : null;
      if (bitIndex !== null && effectiveBitIndex === null) errors.push("BitIndex is only valid for Holding Register BOOL");
    } else if (device?.protocol === "Siemens S7 TCP") {
      if (!dataType || !s7AddressValid(address, dataType)) errors.push("S7 address must match datatype: DB1.DBX0.0 / DB1.DBW2 / DB1.DBD4");
      if (bitIndex !== null) errors.push("S7 bit index is part of the DBX address; leave BitIndex blank");
    } else if (device?.protocol === "MQTT") {
      if (!address || new TextEncoder().encode(address).length > 512 || /[+#\x00-\x1f\x7f]/.test(address)) errors.push("MQTT address must be an exact topic without + or # (max 512 bytes)");
      if (bitIndex !== null) errors.push("MQTT BitIndex must be blank");
    } else if (device?.protocol === "OPC UA") {
      if (!opcUaNodeAddressValid(address)) errors.push("OPC UA address must be a NodeId such as ns=2;i=13 or the Siemens namespace URI;i=13");
      if (bitIndex !== null) errors.push("OPC UA BitIndex must be blank");
    } else if (device) {
      errors.push(`Unsupported device protocol: ${device.protocol}`);
    }
    if (!Number.isInteger(decimalPlaces) || decimalPlaces < 0 || decimalPlaces > 6) errors.push("DecimalPlaces must be 0..6");
    payload = device ? { body:{ deviceId:device.id, name, address, dataType, bitIndex:effectiveBitIndex, byteOrder, unit, offset, decimalPlaces } } : null;
    preview = `${deviceNameValue || "?"} • ${name || "?"} • ${address || "?"} • ${dataType || "?"}${effectiveBitIndex !== null ? ` bit ${effectiveBitIndex}` : ""}`;
  } else if (dataset === "alarms") {
    const deviceNameValue = dev3Cell(row, "Device", "DeviceName");
    const tagNameValue = dev3Cell(row, "Tag", "TagName");
    const device = state.devices.find(d => d.name.toLowerCase() === deviceNameValue.toLowerCase());
    const tag = device ? state.tags.find(tg => tg.deviceId === device.id && tg.name.toLowerCase() === tagNameValue.toLowerCase()) : null;
    const text = dev3Cell(row, "AlarmText", "Text", "Message");
    const priority = dev3Priority(dev3Cell(row, "Priority"));
    const bitRaw = dev3Cell(row, "BitIndex", "Bit");
    const bitIndex = bitRaw === "" ? null : dev3Number(bitRaw, NaN);
    const triggerValue = dev3Bool(dev3Cell(row, "TriggerValue"), true);
    const conditionRaw = dev3Cell(row, "Condition") || (tag && ["Bool","Word"].includes(tag.dataType) ? "DigitalEquals" : "GreaterThanOrEqual");
    const alarmConditions = ["DigitalEquals","GreaterThan","GreaterThanOrEqual","LessThan","LessThanOrEqual"];
    const condition = alarmConditions.includes(conditionRaw) ? conditionRaw : null;
    const thresholdRaw = dev3Cell(row, "Threshold");
    const threshold = thresholdRaw === "" ? null : dev3Number(thresholdRaw, NaN);
    const deadband = dev3Number(dev3Cell(row, "Deadband", "Hysteresis"), 0);
    const delayOnMs = dev3Number(dev3Cell(row, "DelayOnMs", "DelayOn"), 0);
    const delayOffMs = dev3Number(dev3Cell(row, "DelayOffMs", "DelayOff"), 0);
    const notifyOnActive = dev3Bool(dev3Cell(row, "NotifyOnActive", "NotifyActive"), true);
    const notifyOnCleared = dev3Bool(dev3Cell(row, "NotifyOnCleared", "NotifyCleared"), false);
    const notificationModeRaw = dev3Cell(row, "NotificationMode", "NotifyMode") || "NotifyOnce";
    const notificationMode = ["NotifyOnce","RepeatUntilAcknowledged"].includes(notificationModeRaw) ? notificationModeRaw : null;
    const repeatIntervalSeconds = dev3Number(dev3Cell(row, "RepeatIntervalSeconds", "RepeatSeconds", "RepeatEvery"), 60);
    const continueAfterClearUntilAcknowledged = dev3Bool(dev3Cell(row, "ContinueAfterClearUntilAck", "ContinueAfterClear"), false);
    const requiresAcknowledgement = dev3Bool(dev3Cell(row, "RequiresAcknowledgement", "RequireAck", "AckRequired"), ALARM_DEFAULT_REQUIRES_ACK);
    if (!device) errors.push(`Unknown device: ${deviceNameValue || "(blank)"}`);
    if (!tag) errors.push(`Unknown tag: ${tagNameValue || "(blank)"}`);
    if (tag && !["Bool","Word","UInt16","Int16","UInt32","Int32","Float32"].includes(tag.dataType)) errors.push("Unsupported alarm source datatype");
    if (!text) errors.push("AlarmText is required");
    if (!priority) errors.push("Priority must be Low/Medium/High/Critical");
    const digitalAlarm = tag && ["Bool","Word"].includes(tag.dataType);
    if (!condition) errors.push("Unsupported Condition");
    if (digitalAlarm && condition && condition !== "DigitalEquals") errors.push("BOOL / WORD alarms require DigitalEquals");
    if (!digitalAlarm && tag && condition === "DigitalEquals") errors.push("Numeric alarms require a numeric comparison Condition");
    if (tag?.dataType === "Word" && (bitIndex === null || !Number.isInteger(bitIndex) || bitIndex < 0 || bitIndex > 15)) errors.push("WORD alarm requires BitIndex 0..15");
    if (!digitalAlarm && tag && (!Number.isFinite(threshold))) errors.push("Numeric alarm Threshold is required");
    if (!Number.isFinite(deadband) || deadband < 0) errors.push("Deadband must be >= 0");
    if (!Number.isFinite(delayOnMs) || delayOnMs < 0 || !Number.isFinite(delayOffMs) || delayOffMs < 0) errors.push("Delays must be >= 0");
    if (!notificationMode) errors.push("NotificationMode must be NotifyOnce or RepeatUntilAcknowledged");
    if (!Number.isFinite(repeatIntervalSeconds) || repeatIntervalSeconds < 15) errors.push("RepeatIntervalSeconds must be >= 15");
    payload = tag ? { body:{ tagId:tag.id, text, priority, bitIndex:tag.dataType === "Word" ? bitIndex : null, triggerValue, condition:condition || "DigitalEquals", threshold:digitalAlarm ? null : threshold, deadband:digitalAlarm ? 0 : deadband, delayOnMs, delayOffMs, notifyOnActive, notifyOnCleared, notificationMode:notificationMode || "NotifyOnce", repeatIntervalSeconds, continueAfterClearUntilAcknowledged, requiresAcknowledgement } } : null;
    preview = `${deviceNameValue || "?"} • ${tagNameValue || "?"} • ${text || "?"}`;
  } else if (dataset === "historian") {
    const deviceNameValue = dev3Cell(row, "Device", "DeviceName");
    const tagNameValue = dev3Cell(row, "Tag", "TagName");
    const device = state.devices.find(d => d.name.toLowerCase() === deviceNameValue.toLowerCase());
    const tag = device ? state.tags.find(tg => tg.deviceId === device.id && tg.name.toLowerCase() === tagNameValue.toLowerCase()) : null;
    const sampleIntervalSeconds = dev3Number(dev3Cell(row, "SampleIntervalSeconds", "Sample", "Interval"), 30);
    const retentionDays = dev3Number(dev3Cell(row, "RetentionDays", "Retention"), 365);
    if (!device) errors.push(`Unknown device: ${deviceNameValue || "(blank)"}`);
    if (!tag) errors.push(`Unknown tag: ${tagNameValue || "(blank)"}`);
    if (![10,30,60,300].includes(sampleIntervalSeconds)) errors.push("Sample must be 10, 30, 60 or 300 seconds");
    if (![30,90,365,1825].includes(retentionDays)) errors.push("Retention must be 30, 90, 365 or 1825 days");
    payload = tag ? { body:{ tagId:tag.id, sampleIntervalSeconds, retentionDays } } : null;
    preview = `${deviceNameValue || "?"} • ${tagNameValue || "?"} • ${sampleIntervalSeconds}s • ${retentionDays}d`;
  }
  const existing=dev3ImportIdentity(dataset,payload),mode=dev3ImportExistingMode();
  const action=existing?(mode==='skip'?'skip':'update'):'create';
  if(existing&&dataset==='tags'&&existing.deviceId!==payload?.body?.deviceId)errors.push('Tag name belongs to another Device; rename it before import');
  if(existing&&dataset==='devices'&&existing.protocol!==({'modbus':'Modbus TCP','s7':'Siemens S7 TCP','mqtt':'MQTT','opcua':'OPC UA'}[payload.kind]))errors.push('Device protocol cannot be changed in place');
  return { row:row.__row, dataset, source:row, payload, preview, errors,
    action,existingId:existing?.id||null,
    valid:errors.length === 0 && action!=='skip' };
}

function renderImportPreview() {
  const items = state.importPreview || [];
  const valid = items.filter(x => x.valid).length;
  const imported = items.filter(x => x.imported).length;
  const errors = items.filter(x => !x.valid && !x.imported && x.action!=='skip').length;
  const has = items.length > 0;
  $("importSummary").classList.toggle("hidden", !has);
  $("importPreviewWrap").classList.toggle("hidden", !has);
  $("importEmpty").classList.toggle("hidden", has);
  $("importValidCount").textContent = dev3Count(valid);
  $("importErrorCount").textContent = dev3Count(errors);
  $("importTotalCount").textContent = dev3Count(items.length);
  $("executeImport").disabled = valid === 0 || !canMutateConfiguration();
  $("importPreviewBody").innerHTML = items.map(x => `<tr>
    <td>${x.row}</td>
    <td><span class="import-row-status ${x.valid || x.imported || x.action==='skip' ? "valid" : "error"}">${x.imported ? (x.action==='update'?'UPDATED':'IMPORTED') : (x.action==='skip'?'SKIPPED':x.valid ? (x.action==='update'?'UPDATE':'CREATE') : "ERROR")}</span></td>
    <td>${escapeHtml(x.preview)}</td>
    <td>${x.imported ? "—" : x.action==='skip' ? (state.language==='tr'?'Mevcut kayıt korundu':'Existing record kept') : (x.errors.length ? escapeHtml(x.errors.join(" • ")) : "—")}</td>
  </tr>`).join("");
}

async function previewImportFile(file) {
  if (!file) { state.importPreview = []; renderImportPreview(); return; }
  try {
    const bytes = new Uint8Array(await file.arrayBuffer());
    let text;
    if (bytes[0] === 0xFF && bytes[1] === 0xFE) text = new TextDecoder('utf-16le').decode(bytes.subarray(2));
    else if (bytes[0] === 0xFE && bytes[1] === 0xFF) text = new TextDecoder('utf-16be').decode(bytes.subarray(2));
    else if (bytes[0] === 0xEF && bytes[1] === 0xBB && bytes[2] === 0xBF) text = new TextDecoder('utf-8').decode(bytes.subarray(3));
    else {
      try { text = new TextDecoder('utf-8',{fatal:true}).decode(bytes); }
      catch { text = new TextDecoder('windows-1254').decode(bytes); }
    }
    const dataset = $("importDataset").value;
    const rows = dev3ParseCsv(text);
    if (!rows.length) throw new Error(state.language === "tr" ? "CSV içinde veri satırı bulunamadı veya ayraç/header algılanamadı." : "No data rows found or the delimiter/header could not be detected.");
    const seen=new Set();
    state.importPreview = rows.map(row => {const item=dev3PreviewRow(row,dataset),key=dev3ImportRowKey(dataset,item.payload);if(key){if(seen.has(key)){item.errors.push('Duplicate row in this CSV');item.valid=false}else seen.add(key)}return item});
    renderImportPreview();
  } catch (error) {
    state.importPreview = [];
    renderImportPreview();
    showToast(error.message);
  }
}

async function executeCsvImport() {
  if(!requireConfigurationAccess()||!canMutateConfiguration())return;
  const validItems = (state.importPreview || []).filter(x => x.valid);
  if (!validItems.length) return;
  const updateCount=validItems.filter(x=>x.action==='update').length;
  if(updateCount&&!confirm(state.language==='tr'?`${updateCount} mevcut kayıt yerinde güncellenecek. Devam edilsin mi?`:`Update ${updateCount} existing records in place?`))return;
  $("executeImport").disabled = true;
  let imported = 0;
  for (const item of validItems) {
    try {
      if(item.action==='update'){
        const route={devices:'/api/devices',tags:'/api/tags',alarms:'/api/alarms/definitions',historian:'/api/historian/configurations'}[item.dataset];
        const body=item.dataset==='devices'?{name:item.payload.body.name,
          host:item.payload.body.endpointUrl||item.payload.body.host,
          port:item.payload.body.port,
          unitId:item.payload.kind==='s7'?item.payload.body.rack*32+item.payload.body.slot:item.payload.body.unitId,
          pollIntervalMs:item.payload.body.pollIntervalMs}:item.payload.body;
        await api(`${route}/${encodeURIComponent(item.existingId)}`,{method:'PUT',body:JSON.stringify(body)});
      } else if (item.dataset === "devices") {
        const devicePaths = {modbus:"/api/devices/modbus-tcp",s7:"/api/devices/siemens-s7-tcp",mqtt:"/api/devices/mqtt",opcua:"/api/devices/opc-ua"};
        await api(devicePaths[item.payload.kind], { method:"POST", body:JSON.stringify(item.payload.body) });
      } else if (item.dataset === "tags") {
        await api("/api/tags", { method:"POST", body:JSON.stringify(item.payload.body) });
      } else if (item.dataset === "alarms") {
        await api("/api/alarms/definitions", { method:"POST", body:JSON.stringify(item.payload.body) });
      } else if (item.dataset === "historian") {
        await api("/api/historian/configurations", { method:"POST", body:JSON.stringify(item.payload.body) });
      }
      imported++;
      item.valid = false;
      item.errors = [state.language === "tr" ? "Aktarıldı" : "Imported"];
      item.imported = true;
    } catch (error) {
      item.valid = false;
      item.errors = [error.message];
    }
  }
  await Promise.all([loadDevices(), loadTags(), loadAlarms(), loadHistorianStats()]);
  renderImportPreview();
  showToast(state.language === "tr" ? `${imported} satır aktarıldı.` : `${imported} rows imported.`);
}

const dev3Templates = {
  devices: ["Name;Protocol;Host;Port;UnitId;PollIntervalMs;Rack;Slot", "Boiler PLC;Modbus TCP;192.168.1.10;502;1;1000;;", "Packing PLC;Siemens S7 TCP;192.168.1.20;102;1;1000;0;1", "Plant Broker;MQTT;broker.example.com;8883;;1000;;", "OPC Server;OPC UA;opc.tcp://192.168.1.30:4840;4840;;1000;;"].join("\r\n"),
  tags: ["Device;Name;Address;DataType;BitIndex;ByteOrder;Unit;Offset;DecimalPlaces", "Boiler PLC;Tank Level PV;30001;Float32;;ABCD;%;0;1", "Boiler PLC;Pump Running;00001;Bool;;ABCD;;0;0", "Packing PLC;Line Running;DB1.DBX0.0;Bool;;ABCD;;0;0", "Packing PLC;Motor Speed;DB1.DBD4;Float32;;ABCD;rpm;0;1", "Plant Broker;Temperature;plant/temperature;Float32;;ABCD;C;0;1", "OPC Server;Pressure;\"ns=2;s=Pressure\";Float32;;ABCD;bar;0;1"].join("\r\n"),
  alarms: ["Device;Tag;AlarmText;Priority;BitIndex;TriggerValue;Condition;Threshold;Deadband;DelayOnMs;DelayOffMs;NotifyOnActive;NotifyOnCleared;NotificationMode;RepeatIntervalSeconds;ContinueAfterClearUntilAck;RequiresAcknowledgement", "Boiler PLC;Pump Running;Pump stopped;High;;false;DigitalEquals;;;1000;500;true;false;RepeatUntilAcknowledged;60;true;true", "Boiler PLC;Tank Level PV;High level;High;;true;GreaterThanOrEqual;80;2;1000;2000;true;true;NotifyOnce;60;false;false"].join("\r\n"),
  historian: ["Device;Tag;SampleIntervalSeconds;RetentionDays", "Boiler PLC;Tank Level PV;10;365"].join("\r\n")
};

function downloadCsvText(filename, text) {
  const normalized = String(text || "").startsWith("sep=;") ? String(text || "") : `sep=;\n${String(text || "")}`;
  const blob = new Blob(["\uFEFF", normalized], {type:"text/csv;charset=utf-8"});
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url; a.download = filename; document.body.appendChild(a); a.click(); a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 500);
}

function downloadTemplate(dataset = $("importDataset").value) {
  const names = {devices:"PROGNODE_Devices_Template.csv",tags:"PROGNODE_Tags_Template.csv",alarms:"PROGNODE_AlarmDefinitions_Template.csv",historian:"PROGNODE_Historian_Template.csv"};
  downloadCsvText(names[dataset], dev3Templates[dataset]);
}

function exportDataset(dataset) {
  const paths = {
    devices:"/api/data-exchange/devices.csv",
    tags:"/api/data-exchange/tags.csv",
    alarms:"/api/data-exchange/alarm-definitions.csv",
    "alarm-history":"/api/data-exchange/alarm-history.xlsx",
    "historian-config":"/api/data-exchange/historian-configurations.csv"
  };
  if (dataset === "alarm-history") {
    const from = $("alarmExportFrom")?.value || "";
    const to = $("alarmExportTo")?.value || "";
    if (from && to && from > to) {
      alert(state.language === "tr" ? "Başlangıç tarihi bitiş tarihinden sonra olamaz." : "The start date cannot be after the end date.");
      return;
    }
    const query = new URLSearchParams();
    if (from) query.set("fromUtc", from);
    if (to) query.set("toUtc", to);
    window.location.assign(paths[dataset] + (query.size ? `?${query}` : ""));
    return;
  }
  if (paths[dataset]) window.location.assign(paths[dataset]);
}

function setImportDataset(dataset) {
  const select = $("importDataset");
  if (!select) return;
  select.value = dataset;
  if($("importExistingMode"))$("importExistingMode").value='update';
  state.importPreview = [];
  if ($("importFile")) $("importFile").value = "";
  renderImportPreview();
  const titles = state.language === "tr" ? {
    devices: ["Cihazları İçe Aktar", "Cihaz tanımları ve bağlantı ayarları"],
    tags: ["Tagları İçe Aktar", "Adresler, veri tipleri, byte order ve mühendislik gösterimi"],
    alarms: ["Alarm Tanımlarını İçe Aktar", "Alarm kuralları, öncelikler, gecikmeler ve bildirim seçenekleri"],
    historian: ["Historian Kaydını İçe Aktar", "Tag kayıt aralığı ve retention konfigürasyonu"]
  } : {
    devices: ["Import Devices", "Device definitions and connection settings"],
    tags: ["Import Tags", "Addresses, datatypes, byte order and engineering display"],
    alarms: ["Import Alarm Definitions", "Alarm rules, priorities, delays and notification flags"],
    historian: ["Import Historian Recording", "Tag recording interval and retention configuration"]
  };
  const copy = titles[dataset] || titles.devices;
  if ($("csvImportTitle")) $("csvImportTitle").textContent = copy[0];
  if ($("csvImportIntro")) $("csvImportIntro").textContent = copy[1] + (state.language === "tr" ? ". Her satır içe aktarmadan önce doğrulanır." : ". Every row is validated before import.");
  if($("importExistingModeLabel"))$("importExistingModeLabel").textContent=state.language==='tr'?'Mevcut kayıtlar':'Existing records';
  if($("importExistingModeHelp"))$("importExistingModeHelp").textContent=state.language==='tr'?'Aynı adlı kayıtlar yerinde güncellenir; dosyada olmayan kayıtlar korunur. Silme yapılmaz.':'Matching records are updated in place; records absent from the file are kept. Nothing is deleted.';
  const mode=$("importExistingMode");if(mode){mode.options[0].textContent=state.language==='tr'?'Eşleşenleri güncelle, diğerlerini koru':'Update matching, keep others';mode.options[1].textContent=state.language==='tr'?'Eşleşenleri atla, yenileri ekle':'Skip matching, add new'}
}

function openDatasetImport(dataset) {
  if (!requireConfigurationAccess()) return;
  setImportDataset(dataset);
  $("csvImportModal")?.classList.remove("hidden");
}

function closeDatasetImport() {
  $("csvImportModal")?.classList.add("hidden");
}

function wireDev3Events() {
  $('notificationEvents')?.addEventListener('click',event=>{
    if(event.target.closest('[data-open-notification-alarm]'))openAlarmFromNotification();
  });
  $('overviewNotificationList')?.addEventListener('click',event=>{
    if(event.target.closest('[data-open-notification-alarm]'))openAlarmFromNotification();
  });
  $('notificationGlobalEnabled')?.addEventListener('change',async event=>{
    const input=event.currentTarget;input.disabled=true;
    try{await updateNotificationDelivery(null,input.checked)}
    catch(error){input.checked=!input.checked;showToast(error.message);await loadNotificationDelivery().catch(()=>{})}
  });
  $('notificationClientControls')?.addEventListener('change',async event=>{
    const input=event.target.closest('[data-notification-client]');if(!input)return;input.disabled=true;
    try{await updateNotificationDelivery(input.dataset.notificationClient,input.checked)}
    catch(error){input.checked=!input.checked;showToast(error.message);await loadNotificationDelivery().catch(()=>{})}
  });
  $("opAddDevice")?.addEventListener("click", openDeviceModal);
  $("opAddTag")?.addEventListener("click", openAddTagModal);
  $("opAddAlarm")?.addEventListener("click", openAddAlarmModal);
  $("opHistorian")?.addEventListener("click", () => navigate("historian"));
  $("opTrend")?.addEventListener("click", () => navigate("trends"));
  $("opImport")?.addEventListener("click", () => { navigate("devices"); setTimeout(() => openDatasetImport("devices"), 60); });

  $("devicesImport")?.addEventListener("click", () => openDatasetImport("devices"));
  $("devicesExport")?.addEventListener("click", () => exportDataset("devices"));
  $("tagsImport")?.addEventListener("click", () => openDatasetImport("tags"));
  $("tagsExport")?.addEventListener("click", () => exportDataset("tags"));
  $("alarmsImport")?.addEventListener("click", () => openDatasetImport("alarms"));
  $("alarmsExport")?.addEventListener("click", () => exportDataset("alarms"));
  $("alarmHistoryExport")?.addEventListener("click", () => exportDataset("alarm-history"));
  $("diagnosticsRefresh")?.addEventListener("click", async () => {
    try {
      await Promise.all([loadHealthAndLicense(), loadDevices(), loadAgentStatus()]);
      await loadTags();
      await loadHistorianStats();
      await loadTagValues();
      renderDiagnostics();
    } catch (error) { console.warn("Diagnostics refresh failed", error); }
  });
  $("historianImport")?.addEventListener("click", () => openDatasetImport("historian"));
  $("historianConfigExport")?.addEventListener("click", () => exportDataset("historian-config"));

  $("overviewOpenAlarms")?.addEventListener("click", () => navigate("alarms"));
  $("overviewOpenNotifications")?.addEventListener("click", () => navigate("notifications"));
  $("overviewOpenDevices")?.addEventListener("click", () => navigate("devices"));
  $("notificationFilter")?.addEventListener("change", renderNotifications);
  $("importFile")?.addEventListener("change", e => previewImportFile(e.target.files?.[0]));
  $("importExistingMode")?.addEventListener("change",()=>{const file=$("importFile")?.files?.[0];if(file)void previewImportFile(file)});
  $("executeImport")?.addEventListener("click", executeCsvImport);
  $("downloadSelectedTemplate")?.addEventListener("click", () => downloadTemplate());
  $("closeCsvImportModal")?.addEventListener("click", closeDatasetImport);
  $("cancelCsvImport")?.addEventListener("click", closeDatasetImport);
  document.querySelectorAll("[data-template]").forEach(button => button.addEventListener("click", () => downloadTemplate(button.dataset.template)));
  document.querySelectorAll("[data-export]").forEach(button => button.addEventListener("click", () => exportDataset(button.dataset.export)));
}

function wireEvents() {
  document.querySelectorAll(".nav-item")
    .forEach(button =>
      button.addEventListener(
        "click",
        () => navigate(button.dataset.page)));

  ["devicesAddDevice", "emptyAddDevice"].forEach(id =>
    $(id)?.addEventListener("click", openDeviceModal));

  ["tagsAddTag", "emptyAddTag"].forEach(id =>
    $(id)?.addEventListener("click", openAddTagModal));

  ["alarmsAddAlarm"].forEach(id => $(id)?.addEventListener("click", openAddAlarmModal));
  $("historianAddTag").addEventListener("click", openAddHistorianModal);
  $("closeHistorianModal").addEventListener("click", closeHistorianModal);
  $("saveHistorianConfig").addEventListener("click", saveHistorianConfiguration);
  $("trendsAddTrend").addEventListener("click", openAddTrendModal);

  $("firstRunStart")?.addEventListener("click", startFirstRunQuickSetup);
  $("firstRunLater")?.addEventListener("click", () => closeFirstRunQuickStart({
    snooze:true,
    permanent:Boolean($("firstRunDontShowAgain")?.checked)
  }));
  $("closeFirstRunQuickStart")?.addEventListener("click", () => closeFirstRunQuickStart({
    snooze:true,
    permanent:Boolean($("firstRunDontShowAgain")?.checked)
  }));
  $("setupAssistantButton")?.addEventListener("click", () => openFirstRunQuickStart(true));
  $("overviewSetupAssistant")?.addEventListener("click", () => openFirstRunQuickStart(true));
  $("overviewSetupSummary")?.addEventListener("click", () => openFirstRunQuickStart(true));

  $("closeDeviceModal")
    .addEventListener("click", closeDeviceModal);

  $("wizardBack")
    .addEventListener(
      "click",
      () => showWizardStep(1));

  $("saveDevice")
    .addEventListener("click", saveDevice);

  $("pingModbusHost")
    .addEventListener("click", pingModbusHost);

  $("testModbusConnection")
    .addEventListener("click", testModbus);
  $("testS7Connection").addEventListener("click",testS7Connection);
  $("inspectOpcUaCertificate").addEventListener("click",inspectOpcUaCertificate);
  $("opcUaUrl").addEventListener("input",()=>{
    state.opcUaCertificate = null;
    state.opcUaCertificateInput = "";
    $("opcUaCertificateResult").classList.add("hidden");
  });

  $("closeTagModal")
    .addEventListener("click", closeTagModal);

  $("saveTag")
    .addEventListener("click", saveTag);

  $("closeAlarmModal").addEventListener("click", closeAlarmModal);
  $("saveAlarm").addEventListener("click", saveAlarm);
  $("alarmTag").addEventListener("change", updateAlarmFormVisibility);
  $("closeTrendModal").addEventListener("click", closeTrendModal);
  $("saveTrend").addEventListener("click", saveTrend);
  $("trendRange").addEventListener("change", async () => {
    $("trendRange").dataset.userChanged = "1";
    updateTrendCustomRangeVisibility();

    if ($("trendRange").value !== "custom")
      await refreshTrendPoints();
  });

  $("trendApplyRange")
    .addEventListener("click", refreshTrendPoints);

  $("trendExportCsv")
    .addEventListener("click", exportTrendCsv);

  $("trendExportArchive")
    .addEventListener("click", exportTrendArchive);

  $("trendExportPng")
    .addEventListener("click", exportTrendPng);

  $("trendCanvas")
    .addEventListener("mousemove", handleTrendMouseMove);

  $("trendCanvas")
    .addEventListener("mouseleave", handleTrendMouseLeave);

  $("alarmCondition")?.addEventListener("change", updateAlarmFormVisibility);

  $("tagDataType")
    .addEventListener("change", () => {
      refreshTagAddressExample();
    });

  $("tagDevice").addEventListener("change", refreshTagAddressExample);

  ["tagAddress","tagBitIndex"]
    .forEach(id =>
      $(id).addEventListener("input", () => {
        if (id === "tagAddress") {
          state.tagSuggestedAddress = null;
          updateTagFormVisibility();
        }
        validateTagAddressClient();
      }));

  ["modbusHost","modbusPort","modbusUnitId"]
    .forEach(id =>
      $(id).addEventListener("input", () => {
        if (state.editingDeviceId && deviceCommunicationChanged())
          state.modbusTestPassed = false;
      }));

  $("deviceSearch")
    .addEventListener("input", () => { state.devicePage = 1; renderDevices(); });

  ["deviceProtocolFilter","deviceStatusFilter","deviceSort","devicePageSize"]
    .forEach(id => $(id).addEventListener("change", () => { state.devicePage = 1; renderDevices(); }));
  $("devicePrevPage").addEventListener("click", () => { state.devicePage--; renderDevices(); });
  $("deviceNextPage").addEventListener("click", () => { state.devicePage++; renderDevices(); });

  $("tagSearch")
    .addEventListener("input", () => { state.tagPage = 1; renderTags(); });

  ["tagDeviceFilter","tagTypeFilter","tagSort","tagPageSize"]
    .forEach(id => $(id).addEventListener("change", () => { state.tagPage = 1; renderTags(); }));
  $("tagPrevPage").addEventListener("click", () => { state.tagPage--; renderTags(); });
  $("tagNextPage").addEventListener("click", () => { state.tagPage++; renderTags(); });

  [["alarmActiveSearch","input","alarmActivePage"],["alarmActivePriority","change","alarmActivePage"],["alarmActiveState","change","alarmActivePage"],["alarmActivePageSize","change","alarmActivePage"],
   ["alarmDefinitionSearch","input","alarmDefinitionPage"],["alarmDefinitionPriority","change","alarmDefinitionPage"],["alarmDefinitionPageSize","change","alarmDefinitionPage"],
   ["alarmHistorySearch","input","alarmHistoryPage"],["alarmHistoryPriority","change","alarmHistoryPage"],["alarmHistoryPageSize","change","alarmHistoryPage"]]
    .forEach(([id,event,page]) => $(id).addEventListener(event, () => { state[page] = 1; if(id.startsWith("alarmHistory")) refreshAlarmHistory(); else renderAlarms(); }));
  [["alarmActive","alarmActivePage"],["alarmDefinition","alarmDefinitionPage"],["alarmHistory","alarmHistoryPage"]]
    .forEach(([prefix,page]) => {
      $(`${prefix}PrevPage`).addEventListener("click", () => { state[page]--; if(prefix==="alarmHistory") refreshAlarmHistory(); else renderAlarms(); });
      $(`${prefix}NextPage`).addEventListener("click", () => { state[page]++; if(prefix==="alarmHistory") refreshAlarmHistory(); else renderAlarms(); });
    });
  document.querySelectorAll("[data-alarm-group]").forEach(header => {
    const toggleGroup = () => {
      const key = header.dataset.alarmGroup;
      const prefix = key === "active" ? "alarmActive" : key === "definition" ? "alarmDefinition" : "alarmHistory";
      const collapsed = $(`${prefix}GroupBody`).classList.toggle("collapsed");
      header.setAttribute("aria-expanded",String(!collapsed));
    };
    header.addEventListener("click", toggleGroup);
    header.addEventListener("keydown", event => {
      if (event.key === "Enter" || event.key === " ") {
        event.preventDefault();
        toggleGroup();
      }
    });
  });

  $("mobileMenu")
    .addEventListener(
      "click",
      () => { const open = $("sidebar").classList.toggle("open"); $("mobileMenu")?.setAttribute("aria-expanded", String(open)); });

  $("langEN")
    .addEventListener(
      "click",
      () => setLanguage("en"));

  $("langTR")
    .addEventListener(
      "click",
      () => setLanguage("tr"));

  $("themeToggle")
    .addEventListener("click", toggleTheme);

  document.querySelectorAll("[data-range-value]")
    .forEach(button => button.addEventListener("click", async () => {
      $("trendRange").value = button.dataset.rangeValue;
      $("trendRange").dataset.userChanged = "1";
      syncTrendRangePills();
      updateTrendCustomRangeVisibility();
      if (button.dataset.rangeValue !== "custom") await refreshTrendPoints();
    }));

  $("trendAutoFit")
    ?.addEventListener("click", trendAutoFit);

  $("accountButton")
    ?.addEventListener("click", () => openAccountModal());

  $("closeAccountModal")
    ?.addEventListener("click", closeAccountModal);

  $("cancelAccountModal")
    ?.addEventListener("click", closeAccountModal);

  $("accountModal")
    ?.addEventListener("click", event => { if (event.target === $("accountModal")) closeAccountModal(); });

  document.addEventListener("keydown", event => {
    if (event.key === "Escape" && !$("accountModal")?.classList.contains("hidden")) closeAccountModal();
  });

  $("accountImportLicense")
    ?.addEventListener("click", async () => {
      try {
        await importLicenseFromFile($("accountLicenseFile")?.files?.[0]);
        renderAccount();
      } catch (error) { showToast(error.message); }
    });

  $("accountSignIn")
    ?.addEventListener("click", signInLocalAccess);

  $("accountSignOut")
    ?.addEventListener("click", signOutLocalAccess);

  $("accountPassword")
    ?.addEventListener("keydown", event => { if (event.key === 'Enter') signInLocalAccess(); });

  $("bannerOpenLicense")
    ?.addEventListener("click", () => navigate("license"));



  $("openLicensePage")
    ?.addEventListener("click", () => { closeAccountModal(); navigate("license"); });

  $("licenseImportButton")
    ?.addEventListener("click", importLicenseFile);

  $("licenseSignInButton")
    ?.addEventListener("click", openAccountModal);


  $("sendTestNotification")
    .addEventListener("click", sendTestNotification);

  $("remoteAccessBind")?.addEventListener("click", bindRemoteAccess);
  $("remoteAccessSync")?.addEventListener("click", syncRemoteAccess);

  $("alarmBell")
    .addEventListener("click", () => navigate("alarms"));

  // Intentionally no backdrop-click or Escape-to-close handlers.
  // Configuration dialogs close manually with X, or automatically after a successful Save.
}

async function refreshLiveValues() {
  if (!state.tags.length)
    return;

  try {
    await loadTagValues();
    renderTags();
    updateOverview();
  }
  catch(error) {
    console.warn(error);
  }
}

Object.assign(translations.en, {
  qrPairTitle:"Pair a phone by QR", qrPairIntro:"Scan on the Core PC. No IP, code or fingerprint to type.",
  qrPairStart:"+ Pair by QR", qrLanLabel:"LAN address reachable by phone", qrPairRenew:"Renew QR",
  qrPairCancel:"Cancel", qrPairWarning:"QR is valid once for 120 seconds. Only display it on the authorized Core screen.",
  qrPairWait:"Ready to pair. Scan this QR in PROGNODE Mobile.", qrPairDone:"Device paired successfully.",
  qrPairExpired:"QR expired. Renew to continue.", qrPairLocal:"Open PROGNODE on this Core PC and sign in as an administrator.",
  qrPairTls:"LAN HTTPS is not ready. Open Settings → Mobile LAN and run Verify / Repair with local administrator approval. Your current certificate will be retained; manual pairing remains available.",
  qrTlsGuideTitle:"LAN HTTPS is required for a secure QR",
  qrTlsGuideText:"Open Mobile LAN Access in Settings. Check HTTPS and repair it with the local Windows Agent if necessary. Restart the Core only when prompted. The working certificate and existing paired devices are preserved across updates.",
  qrTlsRecheck:"Recheck HTTPS",
  qrPairSelect:"Select an accessible network adapter first.", qrPairSeconds:"seconds left"
});
Object.assign(translations.tr, {
  qrPairTitle:"QR ile telefon eşleştir", qrPairIntro:"Core bilgisayarında QR okutun; IP, kod veya sertifika parmak izi girmeyin.",
  qrPairStart:"+ QR ile Cihaz Ekle", qrLanLabel:"Telefonun erişebildiği LAN adresi", qrPairRenew:"QR Yenile",
  qrPairCancel:"İptal", qrPairWarning:"QR yalnızca bir kez, 120 saniye geçerlidir. Yalnızca yetkili Core ekranında gösterin.",
  qrPairWait:"Hazır. PROGNODE Mobile ile QR'ı tarayın.", qrPairDone:"Cihaz başarıyla eşleştirildi.",
  qrPairExpired:"QR süresi doldu. Devam etmek için yenileyin.", qrPairLocal:"PROGNODE'u bu Core PC'de açın ve yönetici olarak giriş yapın.",
  qrPairTls:"LAN HTTPS hazır değil. Ayarlar → Mobil LAN üzerinden Kontrol Et / Onar işlemini yerel yönetici onayıyla tamamlayın. Mevcut sertifika korunur; manuel eşleştirme kullanılabilir.",
  qrTlsGuideTitle:"Güvenli QR için LAN HTTPS gerekli",
  qrTlsGuideText:"Ayarlar içindeki Mobil LAN Erişimi sayfasını açın. HTTPS durumunu kontrol edin; gerekirse yerel Windows Agent ile yönetici onaylı onarım yapın. Yalnızca istenirse Core'u yeniden başlatın. Güncellemeler mevcut sertifikayı ve eşleşmeleri korur.",
  qrTlsRecheck:"HTTPS’i yeniden kontrol et",
  qrPairSelect:"Önce telefonun erişebildiği ağ adaptörünü seçin.", qrPairSeconds:"saniye kaldı"
});

Object.assign(translations.en, {
  tagCapacity:"Tag Capacity", subscriptionPeriod:"Subscription", licenseState:"License Status",
  offlineFirst:"Offline-first entitlement", renewalRequired:"License renewal required",
  firstRunDontShowAgain:"Don't show this again"
});
Object.assign(translations.tr, {
  tagCapacity:"Tag Kapasitesi", subscriptionPeriod:"Abonelik", licenseState:"Lisans Durumu",
  offlineFirst:"Offline-first lisans", renewalRequired:"Lisans yenileme gerekli",
  firstRunDontShowAgain:"Bir daha gösterme"
});

async function start() {
  try {
    const importedAccountRaw = sessionStorage.getItem('prognode.importedAccount');
    if (importedAccountRaw) state.importedAccount = JSON.parse(importedAccountRaw);
  } catch {
    sessionStorage.removeItem('prognode.importedAccount');
  }
  applyTheme();
  wireEvents();
  wireDev3Events();
  setImportDataset($("importDataset")?.value || "devices");

  try {
    await loadHealthAndLicense();
    await loadServerAccess();
    await loadProtocols();
    await loadDevices();
    await loadTags();
    await loadAlarms();
    // Trend Studio needs this enrollment list before its first series query.
    await loadHistorianStats();
    await loadTrends();
    await loadNotifications();
    await loadAgentStatus();
    await syncServerClock();

    applyLanguage();
    if(window.location.hash.replace(/^#/,'').split('?')[0]==='alarms')openAlarmFromNotification();
    renderAccount();
    syncTrendRangePills();
    // Customer V2 presents the same guided steps directly on Overview.
    // The existing first-run modal remains available from "Guided setup".
    if (!document.body.classList.contains("customer-v2")) openFirstRunQuickStart();

    // First-install onboarding is always discoverable until commissioning is complete.
    // Without a local authenticated session it guides the user to sign in first.

    setInterval(
      refreshLiveValues,
      1000);

    setInterval(loadNotifications, 3000);
    setInterval(loadAgentStatus, 3000);
    setInterval(renderDiagnostics, 3000);
    setInterval(renderServerClock, 1000);
    setInterval(syncServerClock, 60000);
    setInterval(refreshActiveAlarms, 1000);
    setInterval(refreshAlarmHistory, 3000);
    setInterval(refreshTrendPoints, 5000);
    setInterval(loadHistorianStats, 10000);
    setInterval(loadServerAccess, 30000);
    setInterval(refreshLicenseForSession, 10000);
    window.addEventListener("resize", drawTrendChart);
  }
  catch(error) {
    console.error(error);
    showToast(`Startup error: ${error.message}`);
  }
}

start();

// Alarm notification policy
$("alarmNotificationMode")?.addEventListener("change", updateAlarmNotificationPolicyVisibility);
$("alarmRequiresAcknowledgement")?.addEventListener("change", updateAlarmNotificationPolicyVisibility);


/* HF4 local LAN wizard: browser PREPARES only. Windows tray + UAC owns mutation. */
Object.assign(translations.en,{
  lanWizardKicker:"MOBILE ACCESS",lanWizardTitle:"Enable LAN Access",lanWizardIntro:"Approve TCP 5443 on a selected factory LAN only. No Windows profile change or remote entitlement required.",
  lanWizardAdapter:"Plant / LAN interface (never Tailscale)",lanWizardChoose:"Choose interface",lanWizardScope:"Windows firewall access scope",lanWizardDevices:"Selected mobile device IPs",lanWizardSubnet:"Approved plant LAN subnet",
  lanWizardIps:"Approved mobile IPs (comma-separated, up to 8)",lanWizardSubnetWarning:"Every host in this subnet can reach the HTTPS port until authenticated. Prefer a dedicated plant VLAN.",
  lanWizardPublicConfirm:"This is a Public Windows network. I explicitly approve only this interface and source range.",lanWizardPrepare:"Prepare LAN access",lanWizardVerify:"Verify / refresh",lanWizardApprove:"On this PC, right-click the PROGNODE Windows tray Agent → Complete LAN Access Setup (UAC); review the rule and approve Windows UAC once.",
  lanWizardProbe:"Test from phone",lanWizardRepair:"Repair rule",lanWizardDisable:"Disable LAN access",lanWizardQr:"Continue to QR pairing",lanWizardIt:"If guest VLAN/client isolation or enterprise GPO blocks access, request TCP 5443 from the approved sources to the selected Core IP from IT. PROGNODE never bypasses network policy."
});
Object.assign(translations.tr,{
  lanWizardKicker:"MOBİL ERİŞİM",lanWizardTitle:"LAN Erişimini Etkinleştir",lanWizardIntro:"Yalnızca seçilen tesis ağı için TCP 5443 izni. Windows ağ profilini değiştirmeye ve ücretli Remote lisansına gerek yok.",
  lanWizardAdapter:"Tesis / LAN arayüzü (Tailscale hariç)",lanWizardChoose:"Ağ arayüzü seçin",lanWizardScope:"Windows güvenlik duvarı erişim kapsamı",lanWizardDevices:"Seçilen telefonların IP adresleri",lanWizardSubnet:"Onaylı tesis LAN alt ağı",
  lanWizardIps:"İzin verilen mobil IP'ler (virgülle, en fazla 8)",lanWizardSubnetWarning:"Bu alt ağdaki tüm cihazlar, uygulama doğrulamasından önce HTTPS portuna erişebilir. Tesis VLAN'ını tercih edin.",
  lanWizardPublicConfirm:"Bu ağ Windows'ta Public. YALNIZ seçilen arayüz ve kaynak aralığına izin vermeyi onaylıyorum.",lanWizardPrepare:"LAN erişimini hazırla",lanWizardVerify:"Doğrula / yenile",lanWizardApprove:"Bu PC'de PROGNODE Windows Agent tepsi simgesine sağ tıkla → Complete LAN Access Setup (UAC). Kuralı kontrol edip yönetici onayını ver.",
  lanWizardProbe:"Telefondan test et",lanWizardRepair:"Kuralı onar",lanWizardDisable:"LAN erişimini kapat",lanWizardQr:"QR eşleştirmeye geç",lanWizardIt:"Konuk VLAN, istemci izolasyonu veya GPO bağlantıyı engelliyorsa IT'den belirtilen telefon/alt ağdan Core IP'sine TCP 5443 izni isteyin. PROGNODE ağ politikasını aşmaz."
});
let lanWizardStatus=null;
function lanWizardSelected(){return (lanWizardStatus?.interfaces||[]).find(n=>String(n.interfaceIndex)===($('lanWizardNic')?.value||''));}
function lanWizardUpdateScope(){
  const nic=lanWizardSelected(),isSubnet=$('lanWizardScope')?.value==='SUBNET';
  $('lanWizardDevicesRow')?.classList.toggle('hidden',isSubnet);
  $('lanWizardSubnetWarning')?.classList.toggle('hidden',!isSubnet);
  $('lanWizardPublicRow')?.classList.toggle('hidden',nic?.profile!=='Public');
  if($('lanWizardPublicConfirm'))$('lanWizardPublicConfirm').checked=false;
  const detail=$('lanWizardNicDetails');
  if(detail)detail.textContent=nic
    ? `${nic.name} · ${nic.profile} · ${(nic.ipv4||[]).join(', ')} · ${nic.cidr||'?'} · IPv6: ${(nic.ipv6||[]).join(', ')||'—'}${nic.note?' · '+nic.note:''}`
    :t('lanWizardChoose');
}
async function lanWizardLoad(){
  const out=$('lanWizardStatus');if(!out)return;
  try{
    const data=await api('/api/mobile-access/network-status');lanWizardStatus=data;
    const select=$('lanWizardNic'),old=select.value;select.replaceChildren();
    const choose=document.createElement('option');choose.value='';choose.textContent=t('lanWizardChoose');select.appendChild(choose);
    for(const nic of data.interfaces||[]){const op=document.createElement('option');op.value=String(nic.interfaceIndex);op.disabled=!nic.eligible;
      op.textContent=`${nic.name} · ${(nic.ipv4||[]).join(',')} · ${nic.profile}${nic.eligible?'':' [unavailable]'}`;select.appendChild(op);}
    if((data.interfaces||[]).some(n=>n.eligible&&String(n.interfaceIndex)===old))select.value=old;
    else if(data.selected && (data.interfaces||[]).some(n=>n.eligible&&n.interfaceIndex===data.selected.interfaceIndex))select.value=String(data.selected.interfaceIndex);
    lanWizardUpdateScope();
    const healthy=data.firewallRuleHealth==='HEALTHY',https=data.httpsReady;
    $('lanWizardBadge').textContent=healthy&&https?(data.phoneVerified?'PHONE VERIFIED':'TLS READY · TEST PHONE'):data.selected?'CHECK LAN':'NOT CONFIGURED';
    out.textContent=`Firewall: ${data.firewallRuleHealth} · HTTPS: ${https?'TLS VERIFIED':(data.tlsDiagnosticCode||'NOT READY')} · Phone: ${data.phoneVerified?'VERIFIED '+data.phoneProbeIp:'NOT VERIFIED'}${data.pendingRequest?' · UAC action pending':''}${data.tlsDiagnosticMessage?' · '+data.tlsDiagnosticMessage:''}`;
    $('lanWizardProbe').disabled=!(data.selected&&https);
    $('lanWizardRepair').disabled=!data.selected;
    $('lanWizardDisable').disabled=!data.selected;
    $('lanWizardQr').disabled=!(healthy&&https);
  }catch(e){out.textContent=e.message||String(e);$('lanWizardBadge').textContent='SIGN IN REQUIRED';}
}
async function lanWizardPrepare(action='ENABLE'){
  const out=$('lanWizardStatus'),nic=lanWizardSelected();
  try{
    if(!nic||!nic.eligible)throw Error(t('lanWizardChoose'));
    const publicConfirmed=Boolean($('lanWizardPublicConfirm')?.checked);
    if(nic.profile==='Public'&&action!=='DISABLE'&&!publicConfirmed)throw Error(t('lanWizardPublicConfirm'));
    if(action==='DISABLE' && !window.confirm(state.language==='tr'?'Yalnız PROGNODE tarafından oluşturulmuş LAN firewall kuralını kaldırmayı onaylıyor musunuz?':'Remove only the PROGNODE-managed LAN firewall rule?'))return;
    const request={interfaceIndex:nic.interfaceIndex,selectedHost:nic.ipv4[0],scopeType:$('lanWizardScope').value,
      deviceIps:($('lanWizardDeviceIps').value||'').split(',').map(x=>x.trim()).filter(Boolean),
      confirmPublic:publicConfirmed,action};
    const p=await api('/api/mobile-access/prepare',{method:'POST',body:JSON.stringify(request)});
    $('lanWizardPreview').textContent=`${p.action} · ${p.interfaceName} · ${p.profile} · ${p.selectedHost}:5443\n${p.scopeType}: ${p.remoteAddresses.join(', ')}\n${p.warning||''}\nExpires ${new Date(p.expiresAtUtc).toLocaleTimeString()}`;
    $('lanWizardApproval').classList.remove('hidden');
    out.textContent=state.language==='tr'?'Ayarlar hazır. 3 dakika içinde BU PC’deki Windows Agent üzerinden onayla.':'Prepared. Approve through the Windows tray Agent ON THIS PC within three minutes.';
  }catch(e){out.textContent=e.message||String(e);}
}
async function lanWizardPhoneProbe(){
  const nic=lanWizardSelected(),out=$('lanWizardPhoneUrl');
  try{
    if(!nic)throw Error(t('lanWizardChoose'));
    const p=await api('/api/mobile-access/phone-probe',{method:'POST',body:JSON.stringify({interfaceIndex:nic.interfaceIndex})});
    out.replaceChildren();const label=document.createElement('span');label.textContent=state.language==='tr'?'Telefonda AYNI LAN üzerinden bu URL’yi aç; 5 dakika geçerli. Chrome sertifika uyarısı gerçek mobil uygulama sertifika kontrolünün yerini tutmaz: ':'On phone on the SAME LAN open this URL (valid 5 min). Browser trust warnings are not a substitute for Mobile certificate pinning: ';
    const link=document.createElement('a');link.href=p.url;link.textContent=p.url;link.target='_blank';link.rel='noopener';out.append(label,link);
    const frame=$('lanWizardPhoneQr');
    if(frame&&typeof window.PrognodeQrSvg==='function'){
      frame.innerHTML=window.PrognodeQrSvg(p.url);frame.classList.remove('hidden');
    }
  }catch(e){out.textContent=e.message||String(e);}
}
$('lanWizardNic')?.addEventListener('change',lanWizardUpdateScope);
$('lanWizardScope')?.addEventListener('change',lanWizardUpdateScope);
$('lanWizardPrepare')?.addEventListener('click',()=>lanWizardPrepare());
$('lanWizardVerify')?.addEventListener('click',lanWizardLoad);
$('lanWizardProbe')?.addEventListener('click',lanWizardPhoneProbe);
$('lanWizardRepair')?.addEventListener('click',()=>lanWizardPrepare('REPAIR'));
$('lanWizardDisable')?.addEventListener('click',()=>lanWizardPrepare('DISABLE'));
$('lanWizardQr')?.addEventListener('click',()=>{if($('qrPairStart')){$('qrPairStart').scrollIntoView({behavior:'smooth',block:'center'});qrStart(true);}});
document.querySelector('[data-page="settings"]')?.addEventListener('click',()=>{void lanWizardLoad();});

/* QR enrollment: independent of Trends and Remote entitlement; login/localhost checked by Core. */
let pairingQrExpiresAt=0, pairingQrPoll=null;
async function qrLoadNetworkOptions(){
  const data=await api('/api/client/pairing-qr/network-options');
  const select=$('qrLanHost');if(!select)return;
  const old=select.value; select.replaceChildren();
  const addresses=data.addresses||[];
  if(addresses.length>1){const option=document.createElement('option');option.value='';option.textContent=t('qrPairSelect');select.appendChild(option);}
  for(const nic of addresses){const option=document.createElement('option');option.value=nic.address;option.textContent=`${nic.interfaceName} · ${nic.address}`;select.appendChild(option);}
  if(addresses.some(n=>n.address===old))select.value=old;
  else if(lanWizardStatus?.selected?.selectedHost&&addresses.some(n=>n.address===lanWizardStatus.selected.selectedHost))select.value=lanWizardStatus.selected.selectedHost;
  return addresses;
}
function qrStopPolling(){if(pairingQrPoll){clearInterval(pairingQrPoll);pairingQrPoll=null;}}
async function qrStart(showNetworkSelector=true){
  const box=$('qrPairBox'); if(!box)return;
  box.classList.remove('hidden');
  $('qrTlsGuide')?.classList.add('hidden');
  $('qrGraphic')?.classList.add('hidden');
  if(!state.accessSession?.authenticated){$('qrPairStatus').textContent=t('qrPairLocal');return;}
  try{
    if(showNetworkSelector)await qrLoadNetworkOptions();
    // Refresh live status instead of trusting identity cached before a Core restart.
    // A TCP listener alone is not TLS_OK; the admin endpoint checks a real pinned handshake.
    state.serverIdentity=await api('/api/server/identity');
    let tlsHealth=null;
    try{tlsHealth=await api('/api/mobile-access/network-status');}catch(err){console.warn('TLS diagnostics unavailable:',err);}
    if(!state.serverIdentity?.secureApiPort || tlsHealth?.httpsReady===false){
      $('qrPairStatus').textContent=t('qrPairTls')+(tlsHealth?.tlsDiagnosticCode?` [${tlsHealth.tlsDiagnosticCode}]`:'');
      $('qrTlsGuide')?.classList.remove('hidden');
      if($('qrPairRenew'))$('qrPairRenew').disabled=true;
      return;
    }
    if($('qrPairRenew'))$('qrPairRenew').disabled=false;
    const host=$('qrLanHost')?.value||'';
    if(!host){$('qrPairStatus').textContent=t('qrPairSelect');return;}
    const result=await api('/api/client/pairing-qr',{method:'POST',body:JSON.stringify({selectedHost:host})});
    pairingQrExpiresAt=Date.parse(result.expiresAtUtc);
    if(typeof window.PrognodeQrSvg!=='function')throw Error('Offline QR renderer missing; Ctrl+F5 then try again.');
    $('qrGraphic').innerHTML=window.PrognodeQrSvg(result.qrPayload);
    $('qrGraphic').classList.remove('hidden');
    $('qrPairStatus').textContent=t('qrPairWait');
    qrStopPolling();qrUpdateCountdown();
    pairingQrPoll=setInterval(qrPollStatus,2000);
  }catch(e){$('qrPairStatus').textContent=e.message||String(e);$('qrGraphic')?.classList.add('hidden');}
}
function qrUpdateCountdown(){
  const el=$('qrPairCountdown');if(!el)return;
  const left=Math.max(0,Math.ceil((pairingQrExpiresAt-Date.now())/1000));
  el.textContent=pairingQrExpiresAt?`${left} ${t('qrPairSeconds')}`:'';
  if(pairingQrExpiresAt&&left===0){$('qrGraphic')?.classList.add('hidden');el.textContent=t('qrPairExpired');qrStopPolling();}
}
async function qrPollStatus(){
  try{
    const status=await api('/api/client/pairing-qr/status');
    if(status.status==='PAIRED'){$('qrGraphic')?.classList.add('hidden');$('qrPairCountdown').textContent='';$('qrPairStatus').textContent=t('qrPairDone');qrStopPolling();return;}
    if(status.status==='EXPIRED'||status.status==='REVOKED'){$('qrGraphic')?.classList.add('hidden');$('qrPairStatus').textContent=t('qrPairExpired');qrStopPolling();return;}
    qrUpdateCountdown();
  }catch(e){$('qrPairStatus').textContent=e.message||String(e);qrStopPolling();}
}
async function qrCancel(){
 try{await api('/api/client/pairing-qr',{method:'DELETE'});}catch(e){console.warn('QR cancel:',e);}
 qrStopPolling();pairingQrExpiresAt=0;$('qrPairBox')?.classList.add('hidden');
 $('qrGraphic')?.classList.add('hidden');if($('qrGraphic'))$('qrGraphic').replaceChildren();
}
$('qrPairStart')?.addEventListener('click',()=>qrStart(true));
$('qrPairRenew')?.addEventListener('click',()=>qrStart(false));
$('qrTlsOpenLan')?.addEventListener('click',()=>{document.querySelector('[data-open-settings="lan"]')?.click();document.querySelector('[data-settings-tab="lan"]')?.click();void lanWizardLoad();});
$('qrTlsRecheck')?.addEventListener('click',async()=>{
  try { state.serverIdentity=await api('/api/server/identity'); await qrStart(true); }
  catch(e) { $('qrPairStatus').textContent=e.message||String(e); }
});
$('qrPairCancel')?.addEventListener('click',qrCancel);
$('qrLanHost')?.addEventListener('change',()=>{if($('qrGraphic'))$('qrGraphic').classList.add('hidden');});
setInterval(()=>{if(!$('qrPairBox')?.classList.contains('hidden'))qrUpdateCountdown();},1000);


/* HF3 Backup Center - no passphrase is stored in browser, Core, or logs. */
async function loadBackupCenter(){
  const status=$('backupStatusLine');if(!status)return;
  try{
    const [data,audit]=await Promise.all([api('/api/backup/status'),api('/api/backup/audit')]);
    const tr=state.language==='tr';
    $('backupScheduleNote').textContent=data.autoEnabled
      ? (tr?`Otomatik: her gün ${String(data.dailyHourLocal).padStart(2,'0')}:00 sonrası · ${data.retentionDays} gün saklanır.`:`Automatic: daily after ${String(data.dailyHourLocal).padStart(2,'0')}:00 · keeps ${data.retentionDays} days.`)
      : (tr?'Otomatik yedek kapalı: Windows servis ortamına PROGNODE_BACKUP_PASSWORD eklenmeli (min. 12 karakter).':'Automatic backup disabled: set PROGNODE_BACKUP_PASSWORD (12+ characters) for the Windows Core service.');
    window.__pgnBackupDirectory=data.backupDirectory;
    const list=$('backupList');list.replaceChildren();
    if(!data.backups?.length){list.textContent=tr?'Henüz yedek yok.':'No snapshots yet.';}
    for(const b of data.backups||[]){
      const row=document.createElement('div');row.className='backup-row';
      const meta=document.createElement('div');const title=document.createElement('strong');title.textContent=b.fileName;
      const small=document.createElement('small');small.textContent=`${new Date(b.createdAtUtc).toLocaleString()} · ${b.sizeBytes < 1048576 ? Math.max(1,Math.round(b.sizeBytes/1024)) + ' KiB' : (b.sizeBytes/1048576).toFixed(1) + ' MiB'} ${b.automated?'· AUTO':''}`;
      meta.append(title,small);const download=document.createElement('button');download.className='secondary';download.type='button';download.textContent=tr?'İndir':'Download';
      download.addEventListener('click',()=>backupDownload(b.fileName));row.append(meta,download);list.appendChild(row);
    }
    const aud=$('backupAuditList');aud.replaceChildren();
    for(const item of (audit||[]).slice().reverse().slice(0,20)){
      try{const a=JSON.parse(item);const div=document.createElement('div');div.className='backup-row';
        const line=document.createElement('div');const strong=document.createElement('strong');strong.textContent=`${a.operation} · ${a.route}`;
        const detail=document.createElement('small');detail.textContent=`${new Date(a.utc).toLocaleString()} · ${a.actor} · ${a.success?'OK':'FAILED'}`;line.append(strong,detail);div.appendChild(line);aud.appendChild(div);
      }catch{}
    }
    if(!aud.children.length)aud.textContent=tr?'Henüz işlem kaydı yok.':'No changes logged yet.';
    status.classList.remove('error');if(!status.textContent)status.textContent=tr?'Şifreli yedek hazır.':'Ready for encrypted backup.';
  }catch(error){status.classList.add('error');status.textContent=error.message||String(error)}
}
async function backupDownload(fileName){
  const status=$('backupStatusLine');
  try{
    status.textContent=state.language==='tr'?'Şifreli dosya indiriliyor…':'Downloading encrypted file…';
    const token=sessionStorage.getItem('prognode.accessSession')||'';
    const resp=await fetch('/api/backup/download/'+encodeURIComponent(fileName),{headers:{'X-PROGNODE-Session':token},cache:'no-store'});
    if(!resp.ok){const error=await resp.json().catch(()=>({}));throw Error(error.message||`HTTP ${resp.status}`)}
    if(window.showSaveFilePicker){
      const handle=await window.showSaveFilePicker({suggestedName:fileName,types:[{description:'PROGNODE backup',accept:{'application/octet-stream':['.pgnbackup']}}]});
      const stream=await handle.createWritable();await resp.body.pipeTo(stream);
    }else{
      if(Number(resp.headers.get('content-length'))>512*1048576)throw Error('Large backups require Chrome/Edge Save File support.');
      const url=URL.createObjectURL(await resp.blob());const a=document.createElement('a');a.href=url;a.download=fileName;document.body.appendChild(a);a.click();a.remove();setTimeout(()=>URL.revokeObjectURL(url),30000);
    }
    status.textContent=state.language==='tr'?'Şifreli yedek indirildi. Harici diskte de saklayın.':'Encrypted file downloaded. Keep an external copy.';
  }catch(e){status.textContent=e.message||String(e);status.classList.add('error')}
}
$('backupRefresh')?.addEventListener('click',()=>{ $('backupStatusLine').textContent='';void loadBackupCenter();});
$('backupCreate')?.addEventListener('click',async()=>{
  const button=$('backupCreate'),status=$('backupStatusLine');const password=$('backupPassword').value;
  if(password.length<12 || password!==$('backupPasswordConfirm').value){status.classList.add('error');status.textContent=state.language==='tr'?'Parolalar aynı olmalı ve en az 12 karakter içermeli.':'Passphrases must match and have 12+ characters.';return;}
  button.disabled=true;status.classList.remove('error');status.textContent=state.language==='tr'?'SQLite ve proje yedeği hazırlanıyor; bu işlem uzun sürebilir…':'Creating encrypted SQLite snapshot; this may take time…';
  try{const result=await api('/api/backup/create',{method:'POST',body:JSON.stringify({passphrase:password})});
    status.classList.add('success');status.textContent=(state.language==='tr'?'Yedek hazır: ':'Snapshot ready: ')+result.fileName+(result.overview?` · ${result.overview.devices} PLC · ${result.overview.tags} Tags · ${result.overview.historianSamples} Historian samples`: '');
    await loadBackupCenter();await backupDownload(result.fileName);
  }catch(e){status.classList.add('error');status.textContent=e.message||String(e)}
  finally{button.disabled=false;$('backupPassword').value='';$('backupPasswordConfirm').value='';}
});
$('backupInspect')?.addEventListener('click',async()=>{
  const input=$('backupImportFile'),password=$('backupImportPassword').value,out=$('backupImportResult'),button=$('backupInspect');
  if(!input.files?.length || !password){out.textContent=state.language==='tr'?'Dosya ve parolayı seçin.':'Select file and passphrase.';return;}
  const form=new FormData();form.append('backup',input.files[0]);form.append('passphrase',password);
  button.disabled=true;out.textContent=state.language==='tr'?'Yedek şifresi ve dosyalar doğrulanıyor…':'Verifying encrypted backup and file hashes…';
  try{
    const token=sessionStorage.getItem('prognode.accessSession')||'';
    const response=await fetch('/api/backup/inspect',{method:'POST',headers:{'X-PROGNODE-Session':token},body:form});
    const result=await response.json().catch(()=>({}));if(!response.ok)throw Error(result.message||`HTTP ${response.status}`);
    const different = Boolean(result.crossServer);
    const t=state.language==='tr';
    out.classList.toggle('backup-cross-server',different);
    out.textContent=(t?'Şifreli dosya doğrulandı ve güvenle içe aktarıldı; henüz geri yüklenmedi. ':'Encrypted archive verified and staged; NO data has been restored. ')+
      `${result.fileName} • ${result.files} files • schema ${result.databaseSchema} • ${result.overview?.devices??'—'} PLC • ${result.overview?.tags??'—'} Tags • ${result.overview?.historianSamples??'—'} samples. `+
      (different ? (t?'Bilgi: Bu yedek başka bir Server ID ile oluşturulmuş. Bu bir yedek doğrulama hatası DEĞİL. Başka Core üzerine geri yükleme için çevrimdışı araçta ayrı kimlik aktarımı onayı gerekir; lisans, HTTPS sertifikası ve cihaz eşleşmeleri kontrol edilir. ':'Notice: This backup is from a different Server ID. Verification PASSED. An offline identity-migration confirmation is required to restore; review license, HTTPS certificate and paired devices. ') : '')+
      (t?'Core durdurulduktan sonra RESTORE_PROGNODE_BACKUP.cmd ile açık onay vererek geri yükleyebilirsiniz. ':'To restore, stop Core and approve the offline RESTORE_PROGNODE_BACKUP.cmd operation. ')+
      (window.__pgnBackupDirectory?window.__pgnBackupDirectory+'\\'+result.fileName:'');
    await loadBackupCenter();
  }catch(error){out.textContent=error.message||String(error)}
  finally{button.disabled=false;$('backupImportPassword').value='';}
});

Object.assign(translations.en,{
  dataResetKicker:"DATA MANAGEMENT",dataResetTitle:"Delete selected data",dataResetIntro:"Choose the records to remove. License, server identity, security certificates and backups are kept.",
  dataResetSelectAll:"Select all categories",dataResetAll:"Delete all data",dataResetDevices:"Devices",dataResetTags:"Tags",dataResetAlarms:"Alarm rules",dataResetAlarmHistory:"Alarm history",dataResetHistorian:"Historian samples and settings",dataResetTrends:"Saved trends",dataResetBatch:"Batch history",dataResetNotifications:"Notifications",
  dataResetCascade:"Deleting devices also removes their Tags and related recording/alarm configuration. Deleting Tags removes their recording data, alarm rules and trend links.",dataResetReview:"Review selected data",dataResetReviewTitle:"Review before deletion",dataResetContinue:"Continue to password",dataResetCancel:"Cancel",dataResetPassword:"Current PROGNODE account password",dataResetFinalAck:"I understand these records will be permanently deleted.",dataResetExecute:"Delete selected records",dataResetBack:"Back"
});
Object.assign(translations.tr,{
  dataResetKicker:"VERİ YÖNETİMİ",dataResetTitle:"Seçili verileri sil",dataResetIntro:"Silinecek kayıtları seçin. Lisans, sunucu kimliği, güvenlik sertifikaları ve yedekler korunur.",
  dataResetSelectAll:"Tüm kategorileri seç",dataResetAll:"Tüm verileri sil",dataResetDevices:"Cihazlar",dataResetTags:"Taglar",dataResetAlarms:"Alarm kuralları",dataResetAlarmHistory:"Alarm geçmişi",dataResetHistorian:"Historian kayıtları ve ayarları",dataResetTrends:"Kayıtlı trendler",dataResetBatch:"Batch geçmişi",dataResetNotifications:"Bildirimler",
  dataResetCascade:"Cihazları silmek, Taglarını ve ilişkili kayıt/alarm yapılandırmalarını da siler. Tagları silmek, kayıt verilerini, alarm kurallarını ve trend bağlantılarını siler.",dataResetReview:"Seçimi gözden geçir",dataResetReviewTitle:"Silmeden önce kontrol edin",dataResetContinue:"Şifre adımına geç",dataResetCancel:"Vazgeç",dataResetPassword:"Mevcut PROGNODE hesap şifresi",dataResetFinalAck:"Bu kayıtların kalıcı olarak silineceğini anlıyorum.",dataResetExecute:"Seçili kayıtları sil",dataResetBack:"Geri"
});

const resetChecks=[...document.querySelectorAll('[data-reset-category]')];
const resetReview=$('dataResetReview'),resetPasswordStage=$('dataResetPasswordStage');
function resetSelection(){return resetChecks.filter(x=>x.checked).map(x=>x.dataset.resetCategory)}
function updateResetButtons(){
  $('dataResetReviewButton').disabled=resetSelection().length===0;
  $('dataResetExecute').disabled=!$('dataResetPassword').value||!$('dataResetFinalAck').checked;
}
resetChecks.forEach(x=>x.addEventListener('change',updateResetButtons));
$('dataResetSelectAll')?.addEventListener('click',()=>{
  const selectAll=resetChecks.some(x=>!x.checked);resetChecks.forEach(x=>x.checked=selectAll);
  $('dataResetSelectAll').textContent=selectAll?(state.language==='tr'?'Seçimi kaldır':'Clear selection'):t('dataResetSelectAll');updateResetButtons();
});
$('dataResetAllButton')?.addEventListener('click',()=>{
  resetChecks.forEach(x=>x.checked=true);updateResetButtons();
  $('dataResetReviewButton')?.click();
});
$('dataResetReviewButton')?.addEventListener('click',()=>{
  const selected=resetChecks.filter(x=>x.checked);
  $('dataResetSummary').textContent=selected.map(x=>x.parentElement.textContent.trim()).join(' · ');
  resetReview.hidden=false;resetPasswordStage.hidden=true;$('dataResetStatus').textContent='';
});
$('dataResetContinue')?.addEventListener('click',()=>{resetReview.hidden=true;resetPasswordStage.hidden=false;$('dataResetPassword').focus()});
$('dataResetBack')?.addEventListener('click',()=>{resetPasswordStage.hidden=true;resetReview.hidden=false});
$('dataResetCancel')?.addEventListener('click',()=>{resetReview.hidden=true;resetPasswordStage.hidden=true;$('dataResetPassword').value='';$('dataResetFinalAck').checked=false;updateResetButtons()});
$('dataResetPassword')?.addEventListener('input',updateResetButtons);
$('dataResetFinalAck')?.addEventListener('change',updateResetButtons);
$('dataResetExecute')?.addEventListener('click',async()=>{
  const button=$('dataResetExecute'),status=$('dataResetStatus'),password=$('dataResetPassword').value;
  button.disabled=true;status.classList.remove('error');status.textContent=state.language==='tr'?'Seçili kayıtlar siliniyor…':'Deleting selected records…';
  try{
    const result=await api('/api/system/data-reset',{method:'POST',body:JSON.stringify({categories:resetSelection(),password})});
    const count=Object.values(result.deleted||{}).reduce((sum,n)=>sum+Number(n||0),0);
    status.textContent=state.language==='tr'?`${count} kayıt silindi. Sayfa yenileniyor…`:`${count} records deleted. Reloading…`;
    setTimeout(()=>location.reload(),900);
  }catch(error){status.classList.add('error');status.textContent=error.message||String(error);button.disabled=false}
  finally{$('dataResetPassword').value='';updateResetButtons()}
});
