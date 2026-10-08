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

if (OperatingSystem.IsWindows())
{
    var real = new OsMachineFingerprintProvider().GetFingerprint();
    Check(MachineFingerprint.IsWellFormed(real) && real == new OsMachineFingerprintProvider().GetFingerprint(), "Windows MachineGuid yields a stable fingerprint.");
    Console.WriteLine("PASS Windows MachineGuid fingerprint is readable and stable");
}

Console.WriteLine("Licensing contract checks passed.");

sealed class FixedMachine(string fingerprint) : IMachineFingerprintProvider
{
    public string GetFingerprint() => fingerprint;
}
