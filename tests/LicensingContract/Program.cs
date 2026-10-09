// Licensing contract checks that need no network, PLC, real license or production signing key.
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NSec.Cryptography;
using Prognode.Licensing;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

// --- Update check version order -------------------------------------------------------------
{
    var ordered = new[] { "1.0.0-beta.2", "1.0.0-beta.3", "1.0.0-beta.10", "1.0.0-rc.1", "1.0.0", "1.0.1", "1.1.0", "2.0.0" };
    for (var i = 0; i < ordered.Length - 1; i++)
    {
        Check(Prognode.Contracts.ProductVersion.Compare(ordered[i], ordered[i + 1]) < 0, $"{ordered[i]} must sort before {ordered[i + 1]}.");
        Check(Prognode.Contracts.ProductVersion.Compare(ordered[i + 1], ordered[i]) > 0, $"{ordered[i + 1]} must sort after {ordered[i]}.");
    }
    Check(Prognode.Contracts.ProductVersion.Compare("1.0.0-beta.3", "1.0.0-beta.3+abc123") == 0, "Build metadata is ignored.");
    Check(Prognode.Contracts.ProductVersion.Compare("latest", "1.0.0") is null, "Unparseable versions never announce an update.");
    Check(Prognode.Contracts.ProductVersion.IsDevelopmentBuild("1.0.0-dev"), "Development builds do not check for updates.");
    Check(CoreCloudLicenseOptions.IsAllowedBaseUrl("https://account.prognode.io/downloads") &&
          !CoreCloudLicenseOptions.IsAllowedBaseUrl("https://prognode.io.evil.example/downloads"), "Update links only point at PROGNODE.");
    Console.WriteLine("PASS update check: semantic version order, dev builds skipped, PROGNODE-only download link");
}

// --- Cloud license host selection (Release build) -------------------------------------------
{
    var options = new CoreCloudLicenseOptions
    {
        BaseUrl = "http://127.0.0.1:5000",
        FallbackBaseUrls = ["https://evil.example", "https://account.prognode.io.evil.example", "https://account.prognode.io/"],
    };
    var hosts = options.CandidateBaseUrls;
#if DEBUG
    Check(hosts.Contains("http://127.0.0.1:5000"), "Debug builds allow a loopback license API for development.");
#else
    Check(!hosts.Any(h => h.StartsWith("http://", StringComparison.OrdinalIgnoreCase)), "Release builds never use plain HTTP / loopback license APIs.");
#endif
    Check(!hosts.Any(h => h.Contains("evil", StringComparison.OrdinalIgnoreCase)), "Non-PROGNODE hosts are ignored.");
    Check(hosts.Contains(CoreCloudLicenseOptions.ProductionBaseUrl), "The production license API is always a candidate.");
    Check(hosts.Count(h => h.Equals(CoreCloudLicenseOptions.ProductionBaseUrl, StringComparison.OrdinalIgnoreCase)) == 1, "Hosts are de-duplicated.");
    Console.WriteLine("PASS license API host allow-list (PROGNODE HTTPS only in Release)");
}

// --- Offline sign-in attempt limiting ---------------------------------------------------------
{
    var now = new DateTimeOffset(2026, 10, 8, 12, 0, 0, TimeSpan.Zero);
    var limiter = new LoginAttemptLimiter(() => now);
    for (var i = 0; i < LoginAttemptLimiter.MaxFailuresPerSource; i++)
    {
        limiter.EnsureAllowed("10.0.0.5");
        limiter.RecordFailure("10.0.0.5");
    }
    var blocked = false;
    try { limiter.EnsureAllowed("10.0.0.5"); } catch (LoginThrottledException) { blocked = true; }
    Check(blocked, "A source is blocked after the per-source failure limit.");
    limiter.EnsureAllowed("10.0.0.6"); // another source is still allowed
    now = now + LoginAttemptLimiter.Window + TimeSpan.FromSeconds(1);
    limiter.EnsureAllowed("10.0.0.5"); // window expired
    Console.WriteLine("PASS per-source sign-in lockout and expiry");

    var global = new LoginAttemptLimiter(() => now);
    for (var i = 0; i < LoginAttemptLimiter.MaxFailuresTotal; i++)
        global.RecordFailure($"10.0.1.{i}");
    var globalBlocked = false;
    try { global.EnsureAllowed("10.0.2.1"); } catch (LoginThrottledException) { globalBlocked = true; }
    Check(globalBlocked, "Rotating source addresses cannot bypass the global failure limit.");

    var reset = new LoginAttemptLimiter(() => now);
    for (var i = 0; i < LoginAttemptLimiter.MaxFailuresPerSource - 1; i++) reset.RecordFailure("10.0.0.9");
    reset.RecordSuccess("10.0.0.9");
    reset.RecordFailure("10.0.0.9");
    reset.EnsureAllowed("10.0.0.9");
    Console.WriteLine("PASS global sign-in limit and reset after success");
}

// --- Signed activation certificate (license ↔ Core ↔ machine binding) ------------------------
{
    var algorithm = SignatureAlgorithm.Ed25519;
    using var trustedKey = Key.Create(algorithm, new KeyCreationParameters { ExportPolicy = KeyExportPolicies.AllowPlaintextExport });
    using var rogueKey = Key.Create(algorithm);
    const string keyId = "contract-test-key";
    var verifier = new LicenseSignatureVerifier(new LicenseVerificationOptions(
        new Dictionary<string, string> { [keyId] = Encoding.ASCII.GetString(trustedKey.PublicKey.Export(KeyBlobFormat.PkixPublicKeyText)) }));

    var serverId = Guid.Parse("8f0b6f1e-2c3d-4e5f-8a9b-0c1d2e3f4a5b");
    var thisMachine = MachineFingerprint.FromMachineId("4C4C4544-0042-3510-8051-B4C04F4E3732");
    var otherMachine = MachineFingerprint.FromMachineId("11111111-2222-3333-4444-555555555555");

    byte[] Sign(Key key, Action<JsonObject>? edit = null, Action<JsonObject>? afterSign = null)
    {
        var payload = new JsonObject
        {
            ["schema"] = "prognode.activation/v1", ["issuer"] = "PROGNODE",
            ["licenseId"] = "lic-1", ["licenseKey"] = "PGN-L_TEST",
            ["installationId"] = "inst-1", ["serverId"] = serverId.ToString("D"),
            ["machineFingerprint"] = thisMachine,
            ["activatedAtUtc"] = "2026-10-08T10:00:00.000Z", ["issuedAtUtc"] = "2026-10-08T10:00:00.000Z",
        };
        edit?.Invoke(payload);
        var envelope = new JsonObject
        {
            ["formatVersion"] = "2.0", ["keyId"] = keyId, ["signatureAlgorithm"] = "Ed25519",
            ["canonicalization"] = "PGN_CANONICAL_JSON_1", ["payload"] = payload,
        };
        using var document = JsonDocument.Parse(envelope.ToJsonString());
        var signature = algorithm.Sign(key, PgnCanonicalJsonV1.CanonicalizeSignedEnvelope(document.RootElement));
        envelope["signature"] = Base64UrlNoPadding.Encode(signature);
        afterSign?.Invoke((JsonObject)envelope["payload"]!);
        return Encoding.UTF8.GetBytes(envelope.ToJsonString());
    }

    var folder = Path.Combine(Path.GetTempPath(), "prognode-activation-" + Guid.NewGuid().ToString("N"));
    var store = new LicenseActivationStore(Path.Combine(folder, "activation.pgnact"));
    var machine = new FixedMachine(thisMachine);
    var service = new LicenseActivationService(verifier, store, machine, () => serverId);
    try
    {
        Check(service.Evaluate("lic-1", "PGN-L_TEST").Reason == "not_activated", "No certificate means not activated.");

        var certificate = service.Import(Sign(trustedKey), "lic-1", "PGN-L_TEST");
        Check(certificate.ServerId == serverId && certificate.MachineFingerprint == thisMachine, "Certificate claims are parsed after verification.");
        Check(service.Evaluate("lic-1", "PGN-L_TEST").Activated, "A certificate for this license, Core and machine activates.");
        Console.WriteLine("PASS signed activation for this license, Core and machine");

        Check(service.Evaluate("lic-2", "PGN-L_TEST").Reason == "license_mismatch", "A certificate for another license does not activate.");
        Check(new LicenseActivationService(verifier, store, machine, () => Guid.NewGuid()).Evaluate("lic-1", "PGN-L_TEST").Reason == "server_mismatch",
            "A copied data folder on a Core with another identity is not activated.");
        Check(new LicenseActivationService(verifier, store, new FixedMachine(otherMachine), () => serverId).Evaluate("lic-1", "PGN-L_TEST").Reason == "machine_mismatch",
            "The same Core identity on another computer (copied data folder) is not activated.");
        Console.WriteLine("PASS copies to another license, Core identity or computer stay unactivated");

        File.WriteAllBytes(store.Path, Sign(trustedKey, afterSign: p => p["machineFingerprint"] = otherMachine));
        Check(service.Evaluate("lic-1", "PGN-L_TEST").Reason == "activation_invalid", "Editing a signed certificate breaks it.");
        File.WriteAllBytes(store.Path, Sign(rogueKey));
        Check(service.Evaluate("lic-1", "PGN-L_TEST").Reason == "activation_invalid", "A certificate signed by any other key is rejected.");
        File.WriteAllBytes(store.Path, Sign(trustedKey, p => p["schema"] = "prognode.license.payload/v2"));
        Check(service.Evaluate("lic-1", "PGN-L_TEST").Reason == "activation_invalid", "A signed document of another type is not an activation.");
        Console.WriteLine("PASS tampered, foreign-key and wrong-type certificates are rejected");

        store.Clear();
        var refused = false;
        try { service.Import(Sign(trustedKey, p => p["machineFingerprint"] = otherMachine), "lic-1", "PGN-L_TEST"); }
        catch (InvalidOperationException ex) { refused = ex.Message.Contains("different computer", StringComparison.Ordinal); }
        Check(refused && !store.Exists, "Importing a certificate for another computer is refused and nothing is stored.");
        Console.WriteLine("PASS import refuses certificates issued for another computer");

        var request = service.CreateRequest("lic-1", "PGN-L_TEST", "PLANT-HMI", "1.0.0");
        using var requestJson = JsonDocument.Parse(LicenseActivationService.SerializeRequest(request));
        Check(requestJson.RootElement.GetProperty("schema").GetString() == ActivationRequestV1.SchemaName &&
              requestJson.RootElement.GetProperty("machineFingerprint").GetString() == thisMachine &&
              requestJson.RootElement.GetProperty("serverId").GetString() == serverId.ToString("D"),
              "The offline activation request names this license, Core and machine.");
        Console.WriteLine("PASS offline activation request file");

        Check(MachineFingerprint.FromMachineId(" 4c4c4544-0042-3510-8051-b4c04f4e3732 ") == thisMachine, "Fingerprints are case/whitespace insensitive.");
        Check(MachineFingerprint.IsWellFormed(thisMachine) && thisMachine != otherMachine, "Fingerprints are 64 hex and machine specific.");
        Console.WriteLine("PASS machine fingerprint normalisation");
    }
    finally
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }
}

// --- Cross-language: a certificate signed by the real Control code (TypeScript) verifies in Core ----
{
    using var fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", "activation-interop.json")));
    var root = fixture.RootElement;
    var interopVerifier = new LicenseSignatureVerifier(new LicenseVerificationOptions(
        new Dictionary<string, string> { [root.GetProperty("keyId").GetString()!] = root.GetProperty("publicKeyPem").GetString()! }));
    var bytes = Encoding.UTF8.GetBytes(root.GetProperty("certificate").GetString()!);
    var cert = interopVerifier.VerifyActivation(bytes);
    Check(cert.LicenseId == "lic-interop" && cert.LicenseKey == "PGN-L_INTEROP" &&
          cert.ServerId == Guid.Parse("8f0b6f1e-2c3d-4e5f-8a9b-0c1d2e3f4a5b") && cert.MachineFingerprint == new string('a', 64) &&
          cert.ActivatedAtUtc == DateTimeOffset.Parse("2026-10-08T10:00:00Z"),
          "A Control-signed activation certificate verifies in Core with identical claims.");
    var tampered = Encoding.UTF8.GetBytes(root.GetProperty("certificate").GetString()!.Replace("lic-interop", "lic-other"));
    var rejected = false;
    try { interopVerifier.VerifyActivation(tampered); } catch (InvalidOperationException) { rejected = true; }
    Check(rejected, "Changing any claim of the Control-signed certificate breaks the signature.");
    Console.WriteLine("PASS TypeScript (Control) signed activation verifies in C# (Core)");
}

// --- Trusted clock: turning the PC clock back cannot extend a license --------------------------
{
    var folder = Path.Combine(Path.GetTempPath(), "prognode-clock-" + Guid.NewGuid().ToString("N"));
    var paths = new[] { Path.Combine(folder, "a", "clock.dat"), Path.Combine(folder, "b", "clock.dat") };
    var machine = new FixedMachine(MachineFingerprint.FromMachineId("clock-test-machine"));
    var system = new DateTimeOffset(2027, 3, 1, 12, 0, 0, TimeSpan.Zero);
    try
    {
        var clock = new TrustedClock(paths, machine, () => system);
        var fresh = new TrustedClock([Path.Combine(folder, "fresh", "clock.dat")], machine, () => system);
        Check(!fresh.RollbackDetected, "A machine with no clock history reports no rollback (and does not throw).");
        Check(clock.UtcNow == system, "The clock follows the system clock while it moves forward.");
        system = system.AddDays(-30); // user sets Windows back a month
        Check(clock.UtcNow == system.AddDays(30), "A clock set back never moves license time backwards.");
        Check(clock.RollbackDetected, "A rollback beyond the tolerance is reported.");
        Console.WriteLine("PASS license time never moves backwards; rollback is detected");

        clock.ObserveSigned(system.AddDays(40)); // signed activation issued later
        var restarted = new TrustedClock(paths, machine, () => system);
        Check(restarted.HighWaterMark == system.AddDays(40), "The high-water mark survives a restart.");
        Check(new TrustedClock(paths, new FixedMachine(MachineFingerprint.FromMachineId("another-pc")), () => system).HighWaterMark == DateTimeOffset.MinValue,
            "A clock file copied from another machine is ignored.");
        File.WriteAllText(paths[0], File.ReadAllText(paths[0]).Replace("2027", "2026"));
        File.Delete(paths[1]);
        Check(new TrustedClock(paths, machine, () => system).HighWaterMark == DateTimeOffset.MinValue, "An edited clock file is ignored.");
        Console.WriteLine("PASS clock mark persists, and copied or edited clock files are ignored");

        var cloudClock = new TrustedClock([Path.Combine(folder, "c", "clock.dat")], machine, () => system);
        cloudClock.ObserveSigned(system.AddYears(3)); // PC was set to 2030 by mistake
        cloudClock.SynchronizeFromCloud(system.AddMinutes(1));
        Check(cloudClock.UtcNow == system.AddMinutes(1), "PROGNODE Cloud time recovers a clock that was set too far ahead.");
        cloudClock.ObserveSigned(system.AddDays(-5));
        Check(cloudClock.HighWaterMark == system.AddMinutes(1), "Signed timestamps only raise the mark.");
        Console.WriteLine("PASS Cloud time recovers a mistaken future clock; signed times only raise it");

        // An expired license stays expired after the clock is turned back.
        var issued = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var payload = new LicensePayloadV2("prognode.license.payload/v2", "PROGNODE", "lic-clock", "PGN-CLOCK", 1, "org", "Org",
            new LicenseSubscriptionV2("ALARM_HISTORIAN", "MONTHLY", issued, issued.AddDays(30), issued.AddDays(37), 100, false, "V1.8"),
            new LicenseAccountV2("u", "u@example.test", "U", "OWNER"),
            new OfflineAuthClaimV1(1, "ARGON2ID", 19, new byte[16], new byte[32], 65536, 3, 1, 32, "BASE64URL_NOPAD", 1),
            new LicenseEntitlementClaimsV2(true, true, null, null, null, null, 100),
            issued);
        var now = issued.AddDays(60);
        var lifecycleClock = new TrustedClock([Path.Combine(folder, "d", "clock.dat")], machine, () => now);
        var entitlements = new LicenseEntitlementService(() => lifecycleClock.UtcNow);
        Check(entitlements.GetLicenseStatus(payload) == "EXPIRED", "A license is expired after its grace period.");
        now = issued.AddDays(10); // clock turned back into the paid period
        Check(entitlements.GetLicenseStatus(payload) == "EXPIRED", "Turning the clock back does not revive an expired license.");
        var beforeIssue = new LicenseEntitlementService(() => issued.AddYears(-1));
        Check(beforeIssue.GetLicenseStatus(payload) == "ACTIVE", "Lifecycle time is never earlier than the signed issue time.");
        Console.WriteLine("PASS an expired license stays expired after the clock is turned back");
    }
    finally
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }
}

if (OperatingSystem.IsWindows())
{
    var real = new OsMachineFingerprintProvider().GetFingerprint();
    Check(MachineFingerprint.IsWellFormed(real) && real == new OsMachineFingerprintProvider().GetFingerprint(), "Windows MachineGuid yields a stable fingerprint.");
    Console.WriteLine("PASS Windows MachineGuid fingerprint is readable and stable");
}

// --- Remote Access relay client: speaks the PROGNODE Cloud contract with the activation token ---------
{
    var handler = new RecordingHandler();
    var relay = new Prognode.RemoteAccess.RemoteAccessCloudClient(
        new HttpClient(handler),
        new CoreCloudLicenseOptions { BaseUrl = CoreCloudLicenseOptions.ProductionBaseUrl });
    var occurrence = Guid.NewGuid();

    handler.Reply = _ => ("{\"ok\":true,\"eventId\":\"" + occurrence + "\"}", 200);
    var published = await relay.PublishNotificationAsync("activation-token", new Prognode.Contracts.Notifications.NotificationEvent(
        42, "HIGH", "Tank 3 high level", "Level 97.4 % at 10:15", DateTimeOffset.Parse("2026-10-08T10:15:00Z"),
        RequiresAcknowledgement: true, RepeatSequence: 2, OccurrenceId: occurrence, EventType: "ALARM_ACTIVE", SourceName: "Tank 3 high level"));
    var sent = handler.Requests[^1];
    Check(published, "A relay ACK for the same event id counts as delivered.");
    Check(sent.Url == "https://account.prognode.io/api/remote-access/alarms/events" && sent.Auth == "Bearer activation-token",
        "Alarm events go to PROGNODE Cloud with the Core activation token.");
    var eventBody = JsonNode.Parse(sent.Body!)!.AsObject();
    Check(eventBody["eventId"]!.GetValue<string>() == occurrence.ToString() && eventBody["state"]!.GetValue<string>() == "ACTIVE" &&
          eventBody["severity"]!.GetValue<string>() == "HIGH" && eventBody["displayMetadata"]!["repeatSequence"]!.GetValue<int>() == 2,
        "The event carries the occurrence id, state, severity and reminder number.");
    Check(!sent.Body!.Contains("97.4") && !eventBody.ContainsKey("message"), "Process values in the message text never leave the site.");

    handler.Reply = _ => ("<html>login</html>", 200);
    Check(!await relay.PublishNotificationAsync("activation-token", new Prognode.Contracts.Notifications.NotificationEvent(1, "LOW", "x", "", DateTimeOffset.UtcNow)),
        "An HTML page is never mistaken for a relay ACK.");

    var commandId = Guid.NewGuid();
    var remoteClientId = Guid.NewGuid();
    handler.Reply = _ => ($"{{\"commands\":[{{\"commandId\":\"{commandId}\",\"type\":\"ACK_ALARM\",\"alarmEventId\":\"{occurrence}\",\"requestedAtUtc\":\"2026-10-08T10:16:00Z\",\"remoteClient\":{{\"id\":\"{remoteClientId}\",\"userId\":\"user-7\",\"deviceName\":\"Pixel\"}}}}]}}", 200);
    var commands = await relay.GetCommandsAsync("activation-token");
    Check(handler.Requests[^1].Url == "https://account.prognode.io/api/remote-access/commands?limit=25", "Core polls the cloud command queue.");
    Check(commands.Count == 1 && commands[0].CommandId == commandId.ToString() && commands[0].OccurrenceId == occurrence &&
          commands[0].UserId == "user-7" && commands[0].RemoteClientId == remoteClientId && commands[0].IssuedAtUtc == DateTimeOffset.Parse("2026-10-08T10:16:00Z"),
        "Remote ACK commands map to the occurrence, user and device that sent them.");

    handler.Reply = _ => ("{\"ok\":true}", 200);
    Check(await relay.CompleteCommandAsync("activation-token", commandId.ToString(), false, "STALE_OCCURRENCE"), "A completed command is reported.");
    var complete = JsonNode.Parse(handler.Requests[^1].Body!)!;
    Check(handler.Requests[^1].Url.EndsWith($"/api/remote-access/commands/{commandId}/complete") &&
          complete["status"]!.GetValue<string>() == "REJECTED" && complete["result"]!["code"]!.GetValue<string>() == "STALE_OCCURRENCE",
        "A refused ACK is reported as REJECTED with its reason.");
    handler.Reply = _ => ("{\"error\":\"command_not_found\"}", 404);
    Check(await relay.CompleteCommandAsync("activation-token", commandId.ToString(), true, "ACKNOWLEDGED"), "An already completed command is not retried forever.");
    Check(!await relay.CompleteCommandAsync("activation-token", "not-a-guid", true, "ACKNOWLEDGED"), "Malformed command ids are never sent.");

    var challengeId = Guid.NewGuid();
    handler.Reply = _ => ($"{{\"challengeId\":\"{challengeId}\",\"challenge\":\"prognode-remote-challenge\",\"expiresAtUtc\":\"2026-10-08T10:20:00Z\"}}", 200);
    var challenge = await relay.CreateChallengeAsync("activation-token");
    Check(challenge.ChallengeId == challengeId && challenge.Challenge == "prognode-remote-challenge", "A registration challenge is parsed.");

    var rawKey = Convert.ToBase64String(new byte[32].Select((_, i) => (byte)(i + 1)).ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    var newId = Guid.NewGuid();
    handler.Reply = _ => ($"{{\"ok\":true,\"remoteClientId\":\"{newId}\",\"remoteClientToken\":\"device-token\",\"tokenExpiresAtUtc\":\"2026-11-07T10:15:00Z\"}}", 200);
    var registered = await relay.RegisterClientAsync("activation-token", "Pixel 9", "ANDROID_PHONE", rawKey, challengeId, "c2lnbmF0dXJl", null);
    var register = JsonNode.Parse(handler.Requests[^1].Body!)!;
    var spkiText = register["devicePublicKey"]!.GetValue<string>().Replace('-', '+').Replace('_', '/');
    var spki = Convert.FromBase64String(spkiText.PadRight(spkiText.Length + (4 - spkiText.Length % 4) % 4, '='));
    Check(spki.Length == 44 && spki[0] == 0x30 && spki[^1] == 32, "A raw 32-byte Ed25519 device key is sent as SPKI.");
    Check(register["platform"]!.GetValue<string>() == "ANDROID" && register["challengeId"]!.GetValue<string>() == challengeId.ToString() &&
          register["deviceSignature"]!.GetValue<string>() == "c2lnbmF0dXJl", "Registration sends the platform and the signed challenge.");
    Check(registered.Success && registered.RemoteClientId == newId && registered.RemoteClientToken == "device-token" &&
          registered.RemoteClientTokenExpiresAtUtc == DateTimeOffset.Parse("2026-11-07T10:15:00Z"), "The device credential is returned for the paired device.");

    handler.Reply = _ => ("{\"error\":\"remote_client_limit_reached\"}", 409);
    var full = await relay.RegisterClientAsync("activation-token", "Pixel 9", "IOS", rawKey, challengeId, "c2lnbmF0dXJl", null);
    Check(!full.Success && full.Code == "REMOTE_CLIENT_LIMIT_REACHED" && full.Message.Contains("seats"), "A full Remote Access plan is reported clearly.");
    Check(Prognode.RemoteAccess.RemoteAccessCloudClient.CloudPlatform("iPhone") == "IOS" &&
          Prognode.RemoteAccess.RemoteAccessCloudClient.CloudPlatform("Windows client") == "WINDOWS" &&
          Prognode.RemoteAccess.RemoteAccessCloudClient.CloudPlatform("") == "OTHER", "Device platforms map to the cloud set.");
    Console.WriteLine("PASS Remote Access relay: activation-token auth, alarm metadata only, remote ACK commands, signed device registration");
}

// --- License auto-refresh: a higher licenseRevision from PROGNODE Cloud is downloaded ----------------
{
    var folder = Path.Combine(Path.GetTempPath(), "pgn-refresh-" + Guid.NewGuid().ToString("N"));
    try
    {
        var handler = new RecordingHandler();
        var options = new CoreCloudLicenseOptions { BaseUrl = CoreCloudLicenseOptions.ProductionBaseUrl };
        var client = new CoreCloudLicenseClient(options, new HttpClient(handler));
        var state = new CoreCloudLicenseStateStore(folder);
        var source = new CloudLicenseRefreshSource(client, state);

        Check(await source.TryGetNewerLicenseAsync("lic-1", 1) is null, "No activation token means no download.");
        Check(handler.Requests.Count == 0, "Nothing is requested before activation.");

        state.Save(new CoreCloudLicenseStatus(true, "ACTIVE", null, null, null, "activation-token", DateTimeOffset.UtcNow, null, "lic-1"));
        handler.Reply = _ => ("{\"ok\":true,\"licenseId\":\"lic-1\",\"licenseRevision\":3,\"signedLicenseDocument\":\"{\\\"signed\\\":true}\"}", 200);
        var newer = await source.TryGetNewerLicenseAsync("lic-1", 2);
        var sent = handler.Requests[^1];
        Check(sent.Url == "https://account.prognode.io/api/core/license" && sent.Auth == "Bearer activation-token" &&
              JsonNode.Parse(sent.Body!)!["licenseId"]!.GetValue<string>() == "lic-1",
            "The current license is requested with the activation token.");
        Check(newer is not null && Encoding.UTF8.GetString(newer) == "{\"signed\":true}", "A higher revision returns the exact signed document.");
        Check(await source.TryGetNewerLicenseAsync("lic-1", 3) is null, "The same revision is not re-imported.");

        handler.Reply = _ => ("{\"ok\":true,\"licenseId\":\"another\",\"licenseRevision\":9,\"signedLicenseDocument\":\"{}\"}", 200);
        Check(await source.TryGetNewerLicenseAsync("lic-1", 1) is null, "A document for another license is ignored.");
        handler.Reply = _ => ("{\"error\":\"installation_revoked\"}", 403);
        Check(await source.TryGetNewerLicenseAsync("lic-1", 1) is null, "A refused download leaves the installed license untouched.");

        handler.Reply = _ => ("{\"ok\":true,\"licenseId\":\"lic-1\",\"licenseStatus\":\"ACTIVE\",\"licenseRevision\":4,\"serverTime\":\"2026-10-08T10:00:00Z\"}", 200);
        var heartbeat = await client.HeartbeatAsync("activation-token", "lic-1", Guid.NewGuid());
        Check(heartbeat.LicenseRevision == 4, "The heartbeat reports the cloud licenseRevision.");
        Console.WriteLine("PASS license auto-refresh: heartbeat revision, authenticated download, only newer revisions");
    }
    finally
    {
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
    }
}

Console.WriteLine("Licensing contract checks passed.");

sealed class FixedMachine(string fingerprint) : IMachineFingerprintProvider
{
    public string GetFingerprint() => fingerprint;
}

sealed class RecordingHandler : HttpMessageHandler
{
    public List<(string Url, string? Auth, string? Body)> Requests { get; } = [];
    public Func<HttpRequestMessage, (string Body, int Status)> Reply { get; set; } = _ => ("{}", 200);

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add((request.RequestUri!.ToString(), request.Headers.Authorization?.ToString(), body));
        var (text, status) = Reply(request);
        var mediaType = text.TrimStart().StartsWith('<') ? "text/html" : "application/json";
        return new HttpResponseMessage((System.Net.HttpStatusCode)status) { Content = new StringContent(text, Encoding.UTF8, mediaType) };
    }
}
