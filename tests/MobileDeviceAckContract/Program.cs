using Prognode.Core.Connectivity;
using Prognode.Alarm;
using Prognode.Web;
using System.Text.Json;
using System.Collections;
using System.Reflection;
using Prognode.Contracts.Alarms;
using Prognode.Data.Sqlite;
using Prognode.Notifications;
using Prognode.RemoteAccess;
using Microsoft.Data.Sqlite;

var root=Path.Combine(Path.GetTempPath(),"prognode-mobile-v057-test-"+Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(root);
try
{
    var server=new ServerAccessService(root,5080,5081,"TEST");
    var paired=server.Pair("Shift Phone",server.GetPairingCode(),"ANDROID","publicKeyForUnitTest");
    var initial=server.GetClient(paired.ClientId)!;
    Require(initial.CanViewAlarms && !initial.CanAcknowledge && initial.AckAuthMode=="USER_SESSION",
        "Default QR must NOT grant device ACK.");

    // A failure to rewrite non-critical last-seen telemetry must not reject a valid mobile bearer.
    var failingSaveServer=new ServerAccessService(Path.Combine(root,"failing-last-seen"),5080,5081,"SAVE TEST");
    var failingSavePair=failingSaveServer.Pair("Save Failure Phone",failingSaveServer.GetPairingCode(),"ANDROID");
    var documentField=typeof(ServerAccessService).GetField("_document",BindingFlags.Instance|BindingFlags.NonPublic)!;
    var document=documentField.GetValue(failingSaveServer)!;
    var clients=(IEnumerable)document.GetType().GetProperty("Clients")!.GetValue(document)!;
    var storedClient=clients.Cast<object>().Single();
    storedClient.GetType().GetProperty("LastSeenAtUtc")!.SetValue(storedClient,DateTime.UtcNow.AddMinutes(-5));
    var accessPath=Path.Combine(root,"failing-last-seen","server-access.json");
    File.Delete(accessPath);
    Directory.CreateDirectory(accessPath); // Force the atomic replace to fail without touching user data.
    try
    {
        Require(failingSaveServer.TryValidateToken(failingSavePair.AccessToken,out _),
            "Valid mobile bearer must not fail when last-seen telemetry cannot be persisted.");
    }
    finally { Directory.Delete(accessPath,true); }

    var updated=server.SetDeviceAccess(paired.ClientId,"Shift Phone A",true,true,"DEVICE","qa@example.invalid")!;
    Require(updated.CanAcknowledge && updated.AckAuthMode=="DEVICE","Admin DEVICE grant failed.");
    var reloaded=new ServerAccessService(root,5080,5081);
    Require(reloaded.TryValidateToken(paired.AccessToken,out var restored) && restored?.CanAcknowledge==true,
        "Device grant must survive restart.");
    Require(reloaded.NotificationDeliveryEnabled && reloaded.DeliveryEnabledFor(paired.ClientId),
        "Legacy pairing must default to notification delivery enabled.");
    Require(reloaded.SetClientNotifications(paired.ClientId,false)?.NotificationsEnabled==false &&
        !reloaded.DeliveryEnabledFor(paired.ClientId),"Per-client mute failed.");
    reloaded.SetNotificationDelivery(false);
    var mutedRestart=new ServerAccessService(root,5080,5081);
    Require(!mutedRestart.NotificationDeliveryEnabled && !mutedRestart.DeliveryEnabledFor(paired.ClientId) &&
        mutedRestart.GetClient(paired.ClientId)?.NotificationsEnabled==false,
        "Global and client mute must survive restart.");
    mutedRestart.SetNotificationDelivery(true);
    Require(!mutedRestart.DeliveryEnabledFor(paired.ClientId),"Global resume must preserve client mute.");
    mutedRestart.SetClientNotifications(paired.ClientId,true);
    Require(mutedRestart.DeliveryEnabledFor(paired.ClientId),"Client resume failed.");
    var notificationDb=Path.Combine(root,"notification-test.db");
    await new DatabaseInitializer(new SqliteDatabaseOptions(notificationDb)).InitializeAsync();
    var notifications=new NotificationEventStore(notificationDb);
    notifications.SetRemoteEligibility(()=>true);
    var recorded=notifications.Publish("High","Test alarm","Stored even when delivery pauses");
    var outbox=new RemoteNotificationOutboxStore(notificationDb,root);
    Require(outbox.Due().Count==1,"Eligible notification was not queued.");
    Require(outbox.SuppressPending()==1 && outbox.Due().Count==0,
        "Pausing must suppress queued remote delivery.");
    Require(notifications.GetRecent().Any(x=>x.Id==recorded.Id),
        "Pausing delivery must not remove notification history.");
    var db=Path.Combine(root,"runtime-test.db");
    var id=Guid.NewGuid();
    var key="alarm:"+Guid.NewGuid().ToString("N");
    var active=new AlarmRuntimeSnapshot(key,Guid.NewGuid(),Guid.NewGuid(),Guid.NewGuid(),false,
        "Boiler 1","Overtemperature",AlarmPriority.High,AlarmRuntimeState.Active,
        DateTimeOffset.UtcNow,DateTimeOffset.UtcNow,null,id);
    var store=new AlarmRuntimeStore(db);
    store.SetActive(active);
    Require(new AlarmRuntimeStore(db).Get(key)?.OccurrenceId==id,"Active occurrence not restored.");
    store.Clear(key,true);
    var pending=new AlarmRuntimeStore(db);
    Require(pending.GetPendingAcknowledgement(key)?.OccurrenceId==id,"Pending after CLEAR not restored.");
    Require(pending.AcknowledgeOccurrence(id,out var already)?.OccurrenceId==id && !already,
        "Pending ACK cannot be committed.");
    Require(new AlarmRuntimeStore(db).GetPendingAcknowledgement(key) is null,
        "ACKed pending occurrence survived restart.");
    var audit=new MobileAckAuditStore(db);
    audit.Record(server.Identity.ServerId,id,paired.ClientId,"DEVICE:"+paired.ClientId,"ACK_FORBIDDEN");
    audit.Record(server.Identity.ServerId,id,paired.ClientId,"DEVICE:"+paired.ClientId,"ACKNOWLEDGED","Shift Phone");
    var userOccurrenceId=Guid.NewGuid();
    audit.Record(server.Identity.ServerId,userOccurrenceId,paired.ClientId,"USER:later@example.invalid",
        "ACKNOWLEDGED","Ilkcan Tanriverdi");
    var attribution=audit.GetAcknowledgements(server.Identity.ServerId,new[]{id});
    Require(attribution.TryGetValue(id,out var ack) && ack.DeviceId==paired.ClientId &&
        ack.Principal=="DEVICE:"+paired.ClientId && ack.ActorDisplayName=="Shift Phone" &&
        ack.TimestampUtc<=DateTimeOffset.UtcNow,
        "Core must resolve the first successful mobile ACK actor and timestamp.");
    var userAttribution=audit.GetAcknowledgements(server.Identity.ServerId,new[]{userOccurrenceId});
    Require(userAttribution.TryGetValue(userOccurrenceId,out var userAck) &&
        userAck.ActorDisplayName=="Ilkcan Tanriverdi",
        "Core must persist and resolve the signed-in user's display name for mobile ACK.");

    var legacyAuditDbPath=Path.Combine(root,"legacy-ack-audit.db");
    using(var legacyDb=new SqliteConnection(new SqliteConnectionStringBuilder {DataSource=legacyAuditDbPath}.ToString()))
    {
        legacyDb.Open();
        using var createLegacyTable=legacyDb.CreateCommand();
        createLegacyTable.CommandText="""
            CREATE TABLE mobile_ack_audit (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                server_id TEXT NOT NULL, occurrence_id TEXT NOT NULL, device_id TEXT,
                principal TEXT NOT NULL, result TEXT NOT NULL, timestamp_utc TEXT NOT NULL
            );
            """;
        createLegacyTable.ExecuteNonQuery();
    }
    var migratedAudit=new MobileAckAuditStore(legacyAuditDbPath);
    var migratedOccurrenceId=Guid.NewGuid();
    migratedAudit.Record(server.Identity.ServerId,migratedOccurrenceId,paired.ClientId,
        "USER:legacy@example.invalid","ACKNOWLEDGED","Legacy User");
    var migratedAttribution=migratedAudit.GetAcknowledgements(server.Identity.ServerId,new[]{migratedOccurrenceId});
    Require(migratedAttribution.TryGetValue(migratedOccurrenceId,out var migratedAck) &&
        migratedAck.ActorDisplayName=="Legacy User",
        "Existing ACK audit databases must migrate without losing new display-name attribution.");
    Require(JsonSerializer.Serialize(audit.GetRecent()).Contains("ACKNOWLEDGED"),
        "ACK audit not persisted.");
    Require(mutedRestart.RevokeClient(paired.ClientId),"Revoke failed.");
    Require(!mutedRestart.TryValidateToken(paired.AccessToken,out _),"Revoked bearer must fail immediately.");
    Console.WriteLine("PASS: default deny, explicit grant, persistent notification mute, active/pending restore, ACK, revoke");
}
finally
{
    try{Directory.Delete(root,true);}catch{ /* Windows SQLite pooling can keep file handles briefly. */ }
}
static void Require(bool condition,string message){if(!condition)throw new Exception(message);}
