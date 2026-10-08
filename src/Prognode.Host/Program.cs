using Prognode.Backup;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Http;
using System.Net;
using System.Text.Json.Serialization;
using Prognode.Alarm;
using Prognode.Contracts.Licensing;
using Prognode.Core;
using Prognode.Core.Devices;
using Prognode.Core.Connectivity;
using Prognode.Core.Diagnostics;
using Prognode.Core.Protocols;
using Prognode.Core.Tags;
using Prognode.Core.Batches;
using Prognode.Data.Sqlite;
using Prognode.Host.Services;
using Prognode.Historian;
using Prognode.Licensing;
using Prognode.Notifications;
using Prognode.Protocols.Abstractions;
using Prognode.Protocols.Modbus;
using Prognode.Protocols.S7;
using Prognode.Protocols.Mqtt;
using Prognode.Protocols.OpcUa;
using Prognode.RemoteAccess;
using Prognode.Trends;
using Prognode.Web;

var builder = WebApplication.CreateBuilder(args);

// HF6.2: LAN TLS settings are installation identity, NOT release-specific settings.
// The elevated local wizard/installer writes this stable admin-owned file once.
// A new ZIP may contain its own blank default appsettings.json without losing
// the existing SHA-256 certificate pin or the user's mobile pairings.
var stableLanDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
    "PROGNODE", "config");
var stableLanFile = Path.Combine(stableLanDir, "lan-https.json");
if (File.Exists(stableLanFile))
{
    using var stableDocument = System.Text.Json.JsonDocument.Parse(File.ReadAllText(stableLanFile));
    if (!stableDocument.RootElement.TryGetProperty("Prognode", out var stablePrognode) ||
        !stablePrognode.TryGetProperty("LanHttps", out var stableSettings) ||
        !stableSettings.TryGetProperty("Enabled", out var enabledProp) ||
        enabledProp.ValueKind != System.Text.Json.JsonValueKind.True ||
        !stableSettings.TryGetProperty("Thumbprint", out var stableThumbProp) ||
        string.IsNullOrWhiteSpace(stableThumbProp.GetString()))
        throw new InvalidOperationException("PROGNODE_STABLE_TLS_INVALID: trusted installation HTTPS settings must be repaired by the local administrator; refusing an HTTP fallback.");
    var priorThumb = builder.Configuration["Prognode:LanHttps:Thumbprint"]?.Replace(" ", string.Empty);
    var priorPfxPath = builder.Configuration["Prognode:LanHttps:PfxPath"];
    if (builder.Configuration.GetValue<bool>("Prognode:LanHttps:Enabled") && !string.IsNullOrWhiteSpace(priorPfxPath))
        throw new InvalidOperationException("PROGNODE_TLS_PFX_CONFLICT: installed certificate config and an active PFX source require explicit administrator migration.");
    var stableThumb = stableThumbProp.GetString()?.Replace(" ", string.Empty);
    if (builder.Configuration.GetValue<bool>("Prognode:LanHttps:Enabled") &&
        !string.IsNullOrWhiteSpace(priorThumb) &&
        !string.Equals(priorThumb, stableThumb, StringComparison.OrdinalIgnoreCase))
        throw new InvalidOperationException("PROGNODE_TLS_PIN_CONFLICT: source configuration differs from installed certificate. Use the local LAN repair wizard; never rotate silently.");
    builder.Configuration.AddJsonFile(stableLanFile, optional: false, reloadOnChange: false);
}


// V0.7.2: PROGNODE Core can run as a real Windows Service. Browser/UI is not required.
builder.Host.UseWindowsService(options => options.ServiceName = "PROGNODE Core");

builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{ o.MultipartBodyLengthLimit = 2L * 1024 * 1024 * 1024; });
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddSingleton<SystemStatusService>();
builder.Services.AddSingleton<ProtocolCatalog>();

var configuredDataRoot = builder.Configuration["Prognode:DataRoot"];
var dataRoot = !string.IsNullOrWhiteSpace(configuredDataRoot)
    ? configuredDataRoot
    : OperatingSystem.IsWindows() && !builder.Environment.IsDevelopment()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "PROGNODE", "data")
        : Path.Combine(builder.Environment.ContentRootPath, "data");
Directory.CreateDirectory(dataRoot);
// Held for the whole Core lifetime. Offline restore refuses to touch a live dataRoot,
// regardless of HTTP port or Windows-service process name.
using var coreDataLock = DataRootLock.Acquire(dataRoot);

var apiPort = builder.Configuration.GetValue<int?>("Prognode:LocalPort") ?? 5080;
var discoveryPort = builder.Configuration.GetValue<int?>("Prognode:DiscoveryPort") ?? 5081;
var serverName = builder.Configuration["Prognode:ServerName"];
// Secure LAN: bind plain HTTP only to localhost. To enable LAN clients,
// configure LanHttps with an on-site certificate and a password from an
// environment variable (never store the password in appsettings.json).
var lanTlsSection=builder.Configuration.GetSection("Prognode:LanHttps");
var lanPfx=lanTlsSection["PfxPath"];
var lanThumbprint=lanTlsSection["Thumbprint"]?.Replace(" ",string.Empty).ToUpperInvariant();
var lanEnv=lanTlsSection["PasswordEnvironmentVariable"] ?? "PROGNODE_LAN_PFX_PASSWORD";
var lanPass=Environment.GetEnvironmentVariable(lanEnv);
var lanHttpsPort=lanTlsSection.GetValue<int?>("Port") ?? 5443;
System.Security.Cryptography.X509Certificates.X509Certificate2? lanCertificate=null;
if(lanTlsSection.GetValue<bool>("Enabled"))
{
    if(!string.IsNullOrWhiteSpace(lanThumbprint))
    {
        using var store=new System.Security.Cryptography.X509Certificates.X509Store(
            System.Security.Cryptography.X509Certificates.StoreName.My,
            System.Security.Cryptography.X509Certificates.StoreLocation.LocalMachine);
        store.Open(System.Security.Cryptography.X509Certificates.OpenFlags.ReadOnly);
        lanCertificate=store.Certificates.Find(
            System.Security.Cryptography.X509Certificates.X509FindType.FindByThumbprint,
            lanThumbprint,false).OfType<System.Security.Cryptography.X509Certificates.X509Certificate2>()
            .FirstOrDefault(cert=>cert.HasPrivateKey && cert.NotAfter>DateTime.UtcNow);
    }
    else if(!string.IsNullOrWhiteSpace(lanPfx) && File.Exists(lanPfx) && !string.IsNullOrWhiteSpace(lanPass))
        lanCertificate=System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12FromFile(lanPfx,lanPass);
    if(lanCertificate is null)
        throw new InvalidOperationException("LAN HTTPS enabled but the LocalMachine certificate/private key or PFX is missing. HTTP LAN is never substituted.");
}
var lanTlsEnabled=lanCertificate is not null;
var certificateFingerprint=lanCertificate is null ? null :
    Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(lanCertificate.RawData));
builder.WebHost.ConfigureKestrel(server =>
{
    server.ListenLocalhost(apiPort);
    if(lanCertificate is not null)
        server.ListenAnyIP(lanHttpsPort,listen=>listen.UseHttps(lanCertificate));
});
var serverAccess = new ServerAccessService(dataRoot,apiPort,discoveryPort,serverName,
    lanTlsEnabled ? lanHttpsPort : null,certificateFingerprint);
builder.Services.AddSingleton(serverAccess);
builder.Services.AddSingleton<QrPairingService>();
builder.Services.AddSingleton(new LanAccessWizardService(serverAccess,
    Path.Combine(builder.Environment.ContentRootPath, "appsettings.json"), lanCertificate));
builder.Services.AddHostedService<ServerDiscoveryHostedService>();
builder.Services.AddHostedService<LanAutoRestartHostedService>();

var databasePath = Path.Combine(dataRoot, "prognode.db");
builder.Services.AddSingleton(new SqliteDatabaseOptions(databasePath));
builder.Services.AddSingleton<DatabaseInitializer>();
builder.Services.AddSingleton(new ProjectBackupService(dataRoot,
    Path.Combine(builder.Environment.ContentRootPath, "appsettings.json"),
    builder.Configuration.GetValue<int?>("Prognode:Backup:DailyHourLocal") ?? 2,
    builder.Configuration.GetValue<int?>("Prognode:Backup:RetentionDays") ?? 30));
builder.Services.AddHostedService<ProjectBackupHostedService>();

builder.Services.AddSingleton<IDeviceRepository, SqliteDeviceRepository>();
builder.Services.AddSingleton<DeviceService>();
builder.Services.AddSingleton<NetworkPingService>();

builder.Services.AddSingleton<ITagRepository, SqliteTagRepository>();
builder.Services.AddSingleton<CurrentTagValueStore>();
builder.Services.AddSingleton<TagService>();

builder.Services.AddSingleton<ModbusTcpProbe>();
builder.Services.AddSingleton<ModbusTcpRegisterClient>();
builder.Services.AddSingleton<ITagReader, ModbusTagReader>();
builder.Services.AddSingleton<ITagDefinitionValidator, ModbusTagDefinitionValidator>();
builder.Services.AddSingleton<IDeviceHealthProbe, ModbusTcpHealthProbe>();
builder.Services.AddSingleton<S7SessionPool>();
builder.Services.AddSingleton<ITagReader>(sp => new S7TagReader(sp.GetRequiredService<S7SessionPool>()));
builder.Services.AddSingleton<ITagDefinitionValidator, S7TagValidator>();
builder.Services.AddSingleton<IDeviceHealthProbe, S7HealthProbe>();
builder.Services.AddSingleton<MqttTagReader>();
builder.Services.AddSingleton<ITagReader>(sp => sp.GetRequiredService<MqttTagReader>());
builder.Services.AddSingleton<ITagDefinitionValidator, MqttTagValidator>();
builder.Services.AddSingleton<IDeviceHealthProbe, MqttHealthProbe>();
builder.Services.AddSingleton(new OpcUaConnectionFactory(dataRoot));
builder.Services.AddSingleton<IOpcUaCertificateApproval>(sp => sp.GetRequiredService<OpcUaConnectionFactory>());
builder.Services.AddSingleton<OpcUaTagReader>();
builder.Services.AddSingleton<ITagReader>(sp => sp.GetRequiredService<OpcUaTagReader>());
builder.Services.AddSingleton<ITagDefinitionValidator, OpcUaTagValidator>();
builder.Services.AddSingleton<IDeviceHealthProbe, OpcUaHealthProbe>();

builder.Services.AddSingleton(new NotificationEventStore(databasePath));
builder.Services.AddSingleton(new Prognode.Web.MobileAckAuditStore(databasePath));
builder.Services.AddSingleton<AgentPresenceService>();

builder.Services.AddSingleton<IBatchRepository, SqliteBatchRepository>();
builder.Services.AddSingleton<BatchService>();

builder.Services.AddSingleton<IAlarmDefinitionRepository, SqliteAlarmDefinitionRepository>();
builder.Services.AddSingleton<IAlarmEventRepository, SqliteAlarmEventRepository>();
builder.Services.AddSingleton(new AlarmRuntimeStore(databasePath));
builder.Services.AddSingleton<AlarmBatchLinkService>();
builder.Services.AddSingleton<AlarmService>();
builder.Services.AddSingleton<AlarmEngine>();
builder.Services.AddSingleton<DeviceCommunicationMonitor>();

builder.Services.AddSingleton<ITrendRepository, SqliteTrendRepository>();
builder.Services.AddSingleton<TrendService>();

builder.Services.AddSingleton<IHistorianRepository, SqliteHistorianRepository>();
builder.Services.AddSingleton<IHistorianConfigurationRepository, SqliteHistorianConfigurationRepository>();
builder.Services.AddSingleton<HistorianService>();
builder.Services.AddSingleton<DataMaintenanceService>();

builder.Services.AddHostedService<TagPollingHostedService>();
builder.Services.AddHostedService<AlarmEngineHostedService>();
builder.Services.AddHostedService<DeviceHealthHostedService>();
builder.Services.AddHostedService<HistorianSamplingHostedService>();

var licensePath = Path.Combine(dataRoot, "license", "current.pgnlicense");
// Production trust root is compiled into Core. appsettings cannot replace the signing key.
// Only the public Ed25519 key is present here; the private signing key remains cloud-side only.
var licenseVerificationOptions = PrognodeTrustedLicenseKeys.CreateVerificationOptions(builder.Environment.ContentRootPath);
var licenseSignatureVerifier = new LicenseSignatureVerifier(licenseVerificationOptions);
var localLicenseStore = new LocalLicenseStore(licensePath);
var licenseEntitlementService = new LicenseEntitlementService();
var offlineCredentialVerifier = new OfflineCredentialVerifier();
var fileLicenseProvider = new FileBackedLicenseProvider(
    localLicenseStore,
    licenseSignatureVerifier,
    licenseEntitlementService);
builder.Services.AddSingleton(licenseVerificationOptions);
builder.Services.AddSingleton(licenseSignatureVerifier);
builder.Services.AddSingleton(localLicenseStore);
builder.Services.AddSingleton(licenseEntitlementService);
builder.Services.AddSingleton(offlineCredentialVerifier);
builder.Services.AddSingleton(fileLicenseProvider);
builder.Services.AddSingleton<ILicenseProvider>(fileLicenseProvider);
builder.Services.AddSingleton<LicenseImportService>();
builder.Services.AddSingleton<LicenseRefreshCoordinator>();
builder.Services.AddSingleton<LicenseService>();
builder.Services.AddSingleton<IDeviceCapacityPolicy>(sp => sp.GetRequiredService<LicenseService>());
builder.Services.AddSingleton<ITagCapacityPolicy>(sp => sp.GetRequiredService<LicenseService>());
builder.Services.AddSingleton<LocalAccessSessionService>();

// Web V1.8 cloud activation/heartbeat. The signed local .pgnlicense remains the offline entitlement
// source, but a successful Cloud REVOKED response for the exact licenseId is persisted and removes
// paid runtime authority. Cloud unavailability never invents or clears a revocation state.
var cloudLicenseSection = builder.Configuration.GetSection("Prognode:CloudLicense");
var cloudLicenseOptions = new CoreCloudLicenseOptions
{
    BaseUrl = cloudLicenseSection["BaseUrl"] ?? string.Empty,
    FallbackBaseUrls = cloudLicenseSection
        .GetSection("FallbackBaseUrls")
        .GetChildren()
        .Select(x => x.Value)
        .Where(x => !string.IsNullOrWhiteSpace(x))
        .Cast<string>()
        .ToArray(),
    ActivatePath = cloudLicenseSection["ActivatePath"] ?? "/api/core/activate",
    HeartbeatPath = cloudLicenseSection["HeartbeatPath"] ?? "/api/core/heartbeat",
    HeartbeatIntervalSeconds = cloudLicenseSection.GetValue<int?>("HeartbeatIntervalSeconds") ?? 60
};
builder.Services.AddSingleton(cloudLicenseOptions);
builder.Services.AddSingleton<CoreCloudLicenseClient>();
builder.Services.AddSingleton(new CoreCloudLicenseStateStore(dataRoot));
builder.Services.AddHostedService<CoreCloudLicenseHostedService>();

// REMOTE_ACCESS is an optional paid add-on. It never participates in the local runtime/login decision.
// When BaseUrl is empty, all local/LAN functionality continues normally and no cloud calls are attempted.
var remoteSection = builder.Configuration.GetSection("Prognode:RemoteAccess");
var remoteAccessOptions = new RemoteAccessOptions
{
    BaseUrl = remoteSection["BaseUrl"] ?? string.Empty,
    StatusPath = remoteSection["StatusPath"] ?? "/api/v1/remote-access/status",
    BindServerPath = remoteSection["BindServerPath"] ?? "/api/v1/remote-access/bind-server",
    RegisterClientPath = remoteSection["RegisterClientPath"] ?? "/api/v1/remote-access/register-client",
    PushTokenPath = remoteSection["PushTokenPath"] ?? "/api/v1/remote-access/push-token",
    RevokeClientPath = remoteSection["RevokeClientPath"] ?? "/api/v1/remote-access/revoke-client",
    PublishNotificationPath = remoteSection["PublishNotificationPath"] ?? "/api/v1/remote-access/notifications",
    CommandsPath = remoteSection["CommandsPath"] ?? "/api/v1/remote-access/commands",
    CompleteCommandPath = remoteSection["CompleteCommandPath"] ?? "/api/v1/remote-access/commands/complete",
    SyncIntervalSeconds = remoteSection.GetValue<int?>("SyncIntervalSeconds") ?? 300
};
builder.Services.AddSingleton(remoteAccessOptions);
var remoteHttpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
if (remoteAccessOptions.IsConfigured)
    remoteHttpClient.BaseAddress = new Uri(remoteAccessOptions.BaseUrl.TrimEnd('/') + "/");
builder.Services.AddSingleton(remoteHttpClient);
builder.Services.AddSingleton<RemoteAccessCloudClient>();
builder.Services.AddSingleton(new RemoteAccessStateStore(dataRoot));
builder.Services.AddSingleton(new RemoteNotificationOutboxStore(databasePath,dataRoot));
builder.Services.AddSingleton(new RemoteAckAuditStore(dataRoot));
builder.Services.AddSingleton<RemoteAccessService>();
builder.Services.AddHostedService<RemoteAccessSyncHostedService>();
builder.Services.AddHostedService<RemoteNotificationRelayHostedService>();
builder.Services.AddHostedService<RemoteAckCommandHostedService>();
builder.Services.AddHostedService<NotificationRetentionHostedService>();

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var database = scope.ServiceProvider.GetRequiredService<DatabaseInitializer>();
    await database.InitializeAsync();
}

// Queue remote rows only for an already active paid subscription. The event and
// outbox row are committed in one SQLite transaction on the producer thread.
var durableNotifications=app.Services.GetRequiredService<NotificationEventStore>();
var accessForNotifications=app.Services.GetRequiredService<RemoteAccessService>();
var deliveryForNotifications=app.Services.GetRequiredService<ServerAccessService>();
durableNotifications.SetRemoteEligibility(() =>
{
    var status=accessForNotifications.GetStatus();
    return deliveryForNotifications.NotificationDeliveryEnabled &&
        status.Entitled && status.CloudConfigured && status.ServerBound &&
        string.Equals(status.SubscriptionStatus,"ACTIVE",StringComparison.OrdinalIgnoreCase);
});

// Remote API clients are paired once and then authenticate with a stable token.
// Local browser/Agent traffic on loopback remains frictionless.
app.Use(async (context, next) =>
{
    var remote = context.Connection.RemoteIpAddress;
    var isLoopback = remote is null || IPAddress.IsLoopback(remote);
    var path = context.Request.Path;

    var publicClientRoute =
        path.StartsWithSegments("/api/server/identity") ||
        (string.Equals(path.Value, "/api/client/pair", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(path.Value, "/api/client/pair-qr", StringComparison.OrdinalIgnoreCase) ||
         (path.StartsWithSegments("/api/mobile-access/phone-probe") && HttpMethods.IsGet(context.Request.Method)));

    // When LAN HTTPS is configured, the public HTTP port is loopback only.
    if (!isLoopback && path.StartsWithSegments("/api") && !context.Request.IsHttps)
    {
        context.Response.StatusCode=StatusCodes.Status426UpgradeRequired;
        await context.Response.WriteAsJsonAsync(new {
            code="LAN_HTTPS_REQUIRED",
            message="Pairing and authentication on a non-loopback network require HTTPS."
        });
        return;
    }

    if (!isLoopback && path.StartsWithSegments("/api") && !publicClientRoute)
    {
        var authorization = context.Request.Headers.Authorization.ToString();
        var token = authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authorization[7..].Trim()
            : string.Empty;

        if (!serverAccess.TryValidateToken(token, out var pairedClient) || pairedClient is null)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { message = "PROGNODE client pairing required." });
            return;
        }

        // Device view grant must also cover the legacy read API. Otherwise a device
        // denied /api/mobile/v2 could bypass via /api/alarms or /api/notifications.
        if(!pairedClient.CanViewAlarms &&
           (path.StartsWithSegments("/api/alarms") ||
            path.StartsWithSegments("/api/notifications")))
        {
            context.Response.StatusCode=StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new {code="ALARM_VIEW_FORBIDDEN"});
            return;
        }
        context.Items["PROGNODE_REMOTE_PAIRED"] = true;
        context.Items["PROGNODE_CLIENT_ID"] = pairedClient.ClientId;
    }

    await next();
});

// Unlicensed / unassigned installations remain fully observable but read-only.
// DEV12: a verified offline license account session is the current authentication gate.
// Portal roles are deliberately NOT mapped to industrial Administrator/Engineer/Operator roles;
// endpoint/action-level industrial authorization remains a separate runtime-role milestone.
app.Use(async (context, next) =>
{
    var method = context.Request.Method;
    var path = context.Request.Path;
    var mutating = !(HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method));
    var exempt =
        path.StartsWithSegments("/api/license/import") ||
        path.StartsWithSegments("/api/access/login") ||
        path.StartsWithSegments("/api/access/logout") ||
        (string.Equals(path.Value, "/api/client/pair", StringComparison.OrdinalIgnoreCase) ||
         string.Equals(path.Value, "/api/client/pair-qr", StringComparison.OrdinalIgnoreCase)) ||
        path.StartsWithSegments("/api/agent/heartbeat") ||
        (string.Equals(path.Value, "/api/mobile-access/agent/claim", StringComparison.OrdinalIgnoreCase) &&
         context.Connection.RemoteIpAddress is { } agentIp && IPAddress.IsLoopback(agentIp));

    if (mutating && path.StartsWithSegments("/api") && !exempt)
    {
        var snapshot = fileLicenseProvider.GetCurrent();
        var assigned = !string.IsNullOrWhiteSpace(snapshot.AssignedUserId);
        if (!snapshot.IsValid || !assigned)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                message = "PROGNODE is in View Only mode. Import an ACTIVE, EXPIRING_SOON or GRACE .pgnlicense and sign in to enable configuration changes."
            });
            return;
        }

        var sessions = context.RequestServices.GetRequiredService<LocalAccessSessionService>();
        var sessionToken = context.Request.Headers["X-PROGNODE-Session"].ToString();
        var occurrenceAckRoute = path.Value is { } requestPath &&
            requestPath.StartsWith("/api/mobile/v2/alarms/", StringComparison.OrdinalIgnoreCase) &&
            requestPath.EndsWith("/ack", StringComparison.OrdinalIgnoreCase) &&
            HttpMethods.IsPost(method);
        // Only this endpoint can authenticate using an explicit, server-stored device grant.
        // It performs its own bearer/session check; all other mutations still require login.
        if (!sessions.Validate(sessionToken) && !occurrenceAckRoute)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new
            {
                message = "Sign in with the user assigned to this PROGNODE license before changing configuration."
            });
            return;
        }
    }

    await next();
});

// V1.8 capacity downgrade protection. If the signed license is below the already configured
// unique process Tag count, preserve the configuration but block new/edited configuration
// until Tags are reduced or a higher-capacity license is imported. DELETE remains available
// so the customer can recover from OVER_CAPACITY without deleting data automatically.
app.Use(async (context, next) =>
{
    var method = context.Request.Method;
    var path = context.Request.Path;
    var isConfigurationRoute =
        path.StartsWithSegments("/api/devices") ||
        path.StartsWithSegments("/api/tags") ||
        path.StartsWithSegments("/api/alarms/definitions") ||
        path.StartsWithSegments("/api/historian/configurations") ||
        path.StartsWithSegments("/api/trends");

    var changesConfiguration = isConfigurationRoute &&
        (HttpMethods.IsPost(method) || HttpMethods.IsPut(method) || HttpMethods.IsPatch(method));

    if (changesConfiguration)
    {
        var license = context.RequestServices.GetRequiredService<LicenseService>();
        var tags = context.RequestServices.GetRequiredService<ITagRepository>();
        var usedTags = await tags.CountAsync(context.RequestAborted);
        if (license.IsOverCapacity(usedTags))
        {
            var limit = license.UniqueTagLimit.Value ?? 0;
            context.Response.StatusCode = StatusCodes.Status409Conflict;
            await context.Response.WriteAsJsonAsync(new
            {
                code = "LICENSE_OVER_CAPACITY",
                message = $"License capacity exceeded. {usedTags} / {limit} Tags configured. Reduce Tags or import a higher-capacity license before changing configuration.",
                usedTags,
                maxTags = limit,
                capacityStatus = "OVER_CAPACITY"
            });
            return;
        }
    }

    await next();
});

// Local administrator backup console. No remote/LAN export or password-less backups.
static IResult? BackupAdmin(HttpContext context, LocalAccessSessionService sessions)
{
    var ip = context.Connection.RemoteIpAddress;
    if (ip is null || !IPAddress.IsLoopback(ip))
        return Results.Json(new { message="Backup Center is local Core administrator only." },statusCode:403);
    var status = sessions.GetStatus(context.Request.Headers["X-PROGNODE-Session"].ToString());
    if (!status.Authenticated) return Results.Json(new { message="Sign in on Core to use Backup Center." },statusCode:401);
    if (status.PortalRole is not ("OWNER" or "ORGANIZATION_ADMIN"))
        return Results.Json(new { message="An administrator account is required." },statusCode:403);
    return null;
}
app.MapGet("/api/backup/status", (HttpContext ctx, LocalAccessSessionService sessions, ProjectBackupService backup) =>
{
    var denied=BackupAdmin(ctx,sessions);ctx.Response.Headers.CacheControl="no-store";
    return denied ?? Results.Ok(backup.GetStatus());
});
app.MapGet("/api/backup/audit", (HttpContext ctx, LocalAccessSessionService sessions, ProjectBackupService backup) =>
{
    var denied=BackupAdmin(ctx,sessions);ctx.Response.Headers.CacheControl="no-store";
    return denied ?? Results.Ok(backup.ReadAudit());
});
app.MapPost("/api/system/data-reset", async (HttpContext ctx, DataResetRequest request,
    LocalAccessSessionService sessions, DataMaintenanceService maintenance, ProjectBackupService backup) =>
{
    var denied = BackupAdmin(ctx, sessions);
    if (denied is not null) return denied;
    var token = ctx.Request.Headers["X-PROGNODE-Session"].ToString();
    if (!sessions.VerifyCurrentPassword(token, request.Password ?? string.Empty))
        return Results.Json(new { message = "The current account password is incorrect." }, statusCode: 401);
    try
    {
        var result = await maintenance.DeleteAsync(request.Categories ?? [], ctx.RequestAborted);
        backup.Audit(sessions.GetStatus(token).UserEmail ?? "LOCAL_ADMIN", "DATA_RESET",
            string.Join(',', request.Categories ?? []), true);
        ctx.Response.Headers.CacheControl = "no-store";
        return Results.Ok(new { deleted = result });
    }
    catch (ArgumentException error)
    {
        return Results.BadRequest(new { message = error.Message });
    }
});
app.MapPost("/api/backup/create", async (HttpContext ctx, BackupCreateRequest request,
    LocalAccessSessionService sessions, ProjectBackupService backup) =>
{
    var denied=BackupAdmin(ctx,sessions);if(denied is not null)return denied;
    if(request.Passphrase is null || request.Passphrase.Length is <12 or >1024)
        return Results.BadRequest(new {message="Use a backup password of 12–1024 characters."});
    try
    {
        var actor=sessions.GetStatus(ctx.Request.Headers["X-PROGNODE-Session"].ToString()).UserEmail ?? "LOCAL_ADMIN";
        var result=await backup.CreateManualAsync(request.Passphrase,actor,ctx.RequestAborted);
        ctx.Response.Headers.CacheControl="no-store";
        return Results.Ok(new { result.FileName,result.SizeBytes,result.Manifest.CreatedAtUtc,
            result.Manifest.DatabaseSchema, files=result.Manifest.Files.Count, overview=result.Manifest.Overview,
            downloadUrl="/api/backup/download/"+Uri.EscapeDataString(result.FileName) });
    }
    catch(ArgumentException error){return Results.BadRequest(new {message=error.Message});}
    catch(InvalidOperationException error){return Results.Json(new {message=error.Message},statusCode:409);}
});
app.MapPost("/api/backup/inspect", async (HttpContext ctx, LocalAccessSessionService sessions,
    ProjectBackupService backup, ServerAccessService server) =>
{
    var denied=BackupAdmin(ctx,sessions);if(denied is not null)return denied;
    if(!ctx.Request.HasFormContentType) return Results.BadRequest(new {message="Multipart file upload required."});
    string? stage=null;
    try
    {
        var form=await ctx.Request.ReadFormAsync(ctx.RequestAborted);
        var file=form.Files.GetFile("backup");var password=form["passphrase"].ToString();
        if(file is null || file.Length is <=0 or > 2L*1024*1024*1024 || password.Length is <12 or >1024)
            return Results.BadRequest(new {message="Select a backup file (max 2 GiB) and its passphrase."});
        stage=Path.Combine(backup.BackupRoot,"INCOMING-"+Guid.NewGuid().ToString("N")+".part");
        await using(var output=new FileStream(stage,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            await file.CopyToAsync(output,ctx.RequestAborted);
        // Verify every AES-GCM record, final seal, manifest entry and SHA256 before retaining it.
        var manifest=await Task.Run(()=>BackupArchive.Verify(stage,password),ctx.RequestAborted);
        if(manifest.DatabaseSchema>9)
            return Results.BadRequest(new {message="Backup schema is newer than this Core; upgrade first."});
        var name="IMPORTED-"+DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+
            Guid.NewGuid().ToString("N")+".pgnbackup";
        File.Move(stage,Path.Combine(backup.BackupRoot,name));stage=null;
        backup.Audit(sessions.GetStatus(ctx.Request.Headers["X-PROGNODE-Session"].ToString()).UserEmail??"LOCAL_ADMIN",
            "BACKUP_IMPORTED_VERIFIED","backup/inspect",true);
        ctx.Response.Headers.CacheControl="no-store";
        return Results.Ok(new {fileName=name,files=manifest.Files.Count,
            databaseSchema=manifest.DatabaseSchema,createdAtUtc=manifest.CreatedAtUtc,
            coreVersion=manifest.CoreVersion, overview=manifest.Overview,
            sourceServerId=manifest.ServerId,crossServer=!string.Equals(manifest.ServerId,
                server.Identity.ServerId.ToString(),StringComparison.OrdinalIgnoreCase)});
    }
    catch(Exception e) when(e is System.Security.Cryptography.CryptographicException or InvalidDataException or EndOfStreamException)
    {return Results.BadRequest(new {message="Backup failed integrity verification or passphrase incorrect."});}
    finally {if(stage is not null){try{File.Delete(stage);}catch{}}}
}).WithMetadata(new RequestSizeLimitAttribute(2L*1024*1024*1024)).DisableAntiforgery();
app.MapGet("/api/backup/download/{fileName}", (string fileName, HttpContext ctx,
    LocalAccessSessionService sessions, ProjectBackupService backup) =>
{
    var denied=BackupAdmin(ctx,sessions);if(denied is not null)return denied;
    var path=backup.GetFile(fileName);
    if(path is null)return Results.NotFound(new {message="Backup not found"});
    ctx.Response.Headers.CacheControl="no-store";
    backup.Audit(sessions.GetStatus(ctx.Request.Headers["X-PROGNODE-Session"].ToString()).UserEmail ?? "LOCAL_ADMIN",
        "BACKUP_DOWNLOADED","backup/download",true);
    return Results.File(path,"application/octet-stream",fileName,enableRangeProcessing:true);
});
app.MapGet("/api/backup/layout", (HttpContext ctx, LocalAccessSessionService sessions, ProjectBackupService backup) =>
{
    var denied=BackupAdmin(ctx,sessions);ctx.Response.Headers.CacheControl="no-store";
    return denied ?? Results.Ok(backup.GetLayout());
});
app.MapPost("/api/backup/layout", (HttpContext ctx, System.Text.Json.JsonElement layout,
    LocalAccessSessionService sessions,ProjectBackupService backup) =>
{
    var denied=BackupAdmin(ctx,sessions);if(denied is not null)return denied;
    try { backup.SaveLayout(layout);
        backup.Audit(sessions.GetStatus(ctx.Request.Headers["X-PROGNODE-Session"].ToString()).UserEmail??"LOCAL_ADMIN",
            "TREND_LAYOUT_SAVED","backup/layout",true);
        return Results.Ok(new { saved=true }); }
    catch(ArgumentException e){return Results.BadRequest(new {message=e.Message});}
});
app.MapDelete("/api/backup/layout", (HttpContext ctx, LocalAccessSessionService sessions, ProjectBackupService backup) =>
{
    var denied=BackupAdmin(ctx,sessions);if(denied is not null)return denied;
    backup.DeleteLayout();
    backup.Audit(sessions.GetStatus(ctx.Request.Headers["X-PROGNODE-Session"].ToString()).UserEmail??"LOCAL_ADMIN",
        "TREND_LAYOUT_RESET","backup/layout",true);
    return Results.Ok(new { reset=true });
});
app.Use(async (ctx,next)=>
{
    await next();
    var method=ctx.Request.Method;
    if((HttpMethods.IsPost(method)||HttpMethods.IsPut(method)||HttpMethods.IsPatch(method)||HttpMethods.IsDelete(method))
       && ctx.Request.Path.StartsWithSegments("/api")
       && !ctx.Request.Path.StartsWithSegments("/api/backup")
       && !ctx.Request.Path.StartsWithSegments("/api/access")
       && ctx.Response.StatusCode is >=200 and <300)
    {
        try
        {
            var session=ctx.RequestServices.GetRequiredService<LocalAccessSessionService>()
                .GetStatus(ctx.Request.Headers["X-PROGNODE-Session"].ToString());
            if(session.Authenticated)
                ctx.RequestServices.GetRequiredService<ProjectBackupService>().Audit(session.UserEmail??"LOCAL_ADMIN",
                    method,ctx.Request.Path.Value??"",true);
        }
        catch(Exception ex){ctx.RequestServices.GetRequiredService<ILoggerFactory>()
            .CreateLogger("BackupAudit").LogError(ex,"Audit log write failed");}
    }
});

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    // O10: browsers must never keep running an old UI against a newer Core. HTML, JS and CSS
    // are always revalidated (ETag/Last-Modified make this a cheap 304 when nothing changed),
    // so stale manual ?v= parameters can no longer pin an outdated app.js.
    OnPrepareResponse = static context =>
    {
        var extension = Path.GetExtension(context.File.Name);
        if (extension.Equals(".html", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".js", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".css", StringComparison.OrdinalIgnoreCase))
        {
            context.Context.Response.Headers.CacheControl = "no-cache";
        }
    }
});
app.MapPrognodeEndpoints();
app.Run();

internal sealed record BackupCreateRequest(string Passphrase);
