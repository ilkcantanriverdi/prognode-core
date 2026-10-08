// Licensing contract checks that need no network, PLC, real license or signing key.
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

Console.WriteLine("Licensing contract checks passed.");
