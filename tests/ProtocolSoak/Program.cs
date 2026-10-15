// PROGNODE protocol soak: reads Modbus TCP, Siemens S7, OPC UA and MQTT for a long time with the
// same readers Core uses, checks every value, injects outages and reports what happened.
//
//   dotnet run --project tests/ProtocolSoak -c Release -- --minutes 60
//   dotnet run --project tests/ProtocolSoak -c Release -- --config my-plcs.json --minutes 480
//
// Without --config it starts the simulators in tools/protocol-soak (Python venv there) and an
// in-process MQTT broker, and takes one of them down every --fault-every minutes for
// --fault-down seconds. With --config it reads real devices and only measures; pull a cable or
// restart a PLC yourself and the report shows detection and recovery times.
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using MQTTnet.Server;
using Prognode.Contracts.Devices;
using Prognode.Contracts.Tags;
using Prognode.Protocols.Abstractions;
using Prognode.Protocols.Modbus;
using Prognode.Protocols.Mqtt;
using Prognode.Protocols.OpcUa;
using Prognode.Protocols.S7;

var options = SoakOptions.Parse(args);
var root = FindRepoRoot();
var work = Path.Combine(Path.GetTempPath(), "prognode-soak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
Directory.CreateDirectory(work);
Console.WriteLine($"PROGNODE protocol soak · {options.Minutes} min · work folder {work}");

var targets = options.ConfigPath is null ? SimTargets.Build(options) : ConfigTargets.Load(options.ConfigPath);
var sim = options.ConfigPath is null ? new Simulators(root, work, options) : null;
// Simulators are child processes: stop them however the soak ends (Ctrl+C, error, normal end).
Console.CancelKeyPress += (_, _) => sim?.StopAllAsync().GetAwaiter().GetResult();
var opc = new OpcUaConnectionFactory(Path.Combine(work, "core-data"));
try
{
if (sim is not null) await sim.StartAllAsync();

foreach (var t in targets.Where(t => t.Device.Protocol == "OPC UA"))
{
    // Core asks an administrator to approve the server certificate once; the soak approves it here,
    // retrying while a just-started server is still coming up.
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            var info = await opc.InspectAsync(t.Device.Host!, CancellationToken.None);
            await opc.TrustAsync(t.Device.Host!, info.Sha256, CancellationToken.None);
            Console.WriteLine($"OPC UA certificate approved: {info.Subject}");
            break;
        }
        catch (Exception ex) when (attempt < 6)
        {
            Console.WriteLine($"OPC UA not ready ({ex.Message}); retrying");
            await Task.Delay(TimeSpan.FromSeconds(10));
        }
    }
}

var stats = targets.ToDictionary(t => t.Name, t => new TargetStats(t.Name, t.Device.Protocol, t.Tags.Count));
var resources = new List<ResourceSample>();
using var stop = new CancellationTokenSource(TimeSpan.FromMinutes(options.Minutes));
var started = DateTimeOffset.UtcNow;
TargetStats.CountFrom = started.AddSeconds(20);
TargetStats.AutoOutages = options.ConfigPath is not null;

var loops = targets.Select(t => Task.Run(() => PollAsync(t, stats[t.Name], CreateReader(t), stop.Token))).ToList();
loops.Add(Task.Run(async () =>
{
    var process = Process.GetCurrentProcess();
    while (!stop.IsCancellationRequested)
    {
        process.Refresh();
        resources.Add(new ResourceSample(DateTimeOffset.UtcNow, GC.GetTotalMemory(false), process.PrivateMemorySize64,
            process.Threads.Count, process.HandleCount));
        try { await Task.Delay(10_000, stop.Token); } catch (OperationCanceledException) { }
    }
}));
if (sim is not null) loops.Add(Task.Run(() => sim.FaultLoopAsync(stats, stop.Token)));
loops.Add(Task.Run(async () =>
{
    while (!stop.IsCancellationRequested)
    {
        try { await Task.Delay(60_000, stop.Token); } catch (OperationCanceledException) { break; }
        var line = string.Join(" | ", stats.Values.Select(s => $"{s.Protocol}: {s.GoodPercent:0.00}% good, {s.Mismatches} wrong"));
        Console.WriteLine($"[{(DateTimeOffset.UtcNow - started).TotalMinutes:0} min] {line}");
    }
}));
await Task.WhenAll(loops);
if (sim is not null) await sim.StopAllAsync();

var reportPath = options.ReportPath ?? Path.Combine(root, "tools", "protocol-soak", "reports",
    $"soak-{DateTime.Now:yyyyMMdd-HHmm}.md");
Directory.CreateDirectory(Path.GetDirectoryName(reportPath)!);
var (report, passed) = Report.Write(options, stats.Values.ToList(), resources, started, DateTimeOffset.UtcNow);
File.WriteAllText(reportPath, report);
File.WriteAllText(Path.ChangeExtension(reportPath, ".json"), JsonSerializer.Serialize(
    new { options, targets = stats.Values.Select(s => s.ToSummary()), resources }, new JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine(report);
Console.WriteLine($"Report: {reportPath}");
return passed ? 0 : 1;
}
finally
{
    if (sim is not null) await sim.StopAllAsync();
}

ITagReader CreateReader(SoakTarget t) => t.Device.Protocol switch
{
    "Modbus TCP" => new ModbusTagReader(new ModbusTcpRegisterClient()),
    "Siemens S7" => new S7TagReader(),
    "OPC UA" => new OpcUaTagReader(opc, SoakCredentials.Store),
    "MQTT" => new MqttTagReader(SoakCredentials.Store),
    _ => throw new ArgumentException($"Unknown protocol {t.Device.Protocol}")
};

static async Task PollAsync(SoakTarget target, TargetStats s, ITagReader reader, CancellationToken stop)
{
    var interval = TimeSpan.FromMilliseconds(target.Device.PollIntervalMs);
    while (!stop.IsCancellationRequested)
    {
        var begin = DateTimeOffset.UtcNow;
        var watch = Stopwatch.StartNew();
        IReadOnlyList<TagValueSnapshot>? values = null;
        string? error = null;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(stop);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            values = await reader.ReadAsync(target.Device, target.Tags.Select(t => t.Tag).ToList(), timeout.Token);
        }
        catch (OperationCanceledException) when (stop.IsCancellationRequested) { break; }
        catch (Exception ex) { error = ex.GetType().Name + ": " + ex.Message; }
        watch.Stop();
        s.Record(begin, watch.Elapsed.TotalMilliseconds, values, error, target.Tags);
        var wait = interval - watch.Elapsed;
        if (wait > TimeSpan.Zero)
        {
            try { await Task.Delay(wait, stop); } catch (OperationCanceledException) { break; }
        }
    }
    (reader as IDisposable)?.Dispose();
    if (reader is IAsyncDisposable a) await a.DisposeAsync();
}

static string FindRepoRoot()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "PROGNODE.sln"))) dir = dir.Parent;
    return dir?.FullName ?? throw new InvalidOperationException("Run inside the prognode-core repository.");
}

// ----------------------------------------------------------------------------- model

sealed record SoakOptions(double Minutes, int Counters, double FaultEveryMinutes, int FaultDownSeconds,
    string? ConfigPath, string? ReportPath, string? Python, string[] Faults)
{
    public static SoakOptions Parse(string[] args)
    {
        string? Get(string name) { var i = Array.IndexOf(args, name); return i >= 0 && i + 1 < args.Length ? args[i + 1] : null; }
        double D(string name, double fallback) => double.TryParse(Get(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : fallback;
        return new SoakOptions(D("--minutes", 10), (int)D("--tags", 200), D("--fault-every", 3), (int)D("--fault-down", 20),
            Get("--config"), Get("--report"), Get("--python"),
            (Get("--faults") ?? "modbus,s7,opcua,mqtt-broker,mqtt").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }
}

/// <summary>What a Tag must read: a counter (k), a fixed number, or anything Good (real devices).</summary>
sealed record Expectation(int? Counter, double? Fixed)
{
    public static readonly Expectation Any = new(null, null);
    public bool Matches(double value, DateTimeOffset readAt)
    {
        if (Counter is { } k)
        {
            // Allow for the poll latency and the simulator's update cycle: up to 3 s behind, 1 s ahead.
            var expected = (readAt.ToUnixTimeSeconds() + k) % 30000;
            var diff = ((long)Math.Round(value) - expected + 30000 * 2) % 30000;
            return diff <= 1 || diff >= 30000 - 3;
        }
        if (Fixed is { } f) return Math.Abs(value - f) <= Math.Max(1e-6, Math.Abs(f) * 1e-6);
        return true;
    }
}

sealed record SoakTag(TagDefinition Tag, Expectation Expect);

sealed record SoakTarget(string Name, DeviceDefinition Device, List<SoakTag> Tags);

/// <summary>User names and passwords of the soak targets (simulators always ask for one).</summary>
static class SoakCredentials
{
    public const string SimUser = "soak", SimPassword = "soak-pass";
    public static readonly MemoryStore Store = new();

    public sealed class MemoryStore : IDeviceCredentialStore
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, DeviceCredential> _items = new();
        public DeviceCredential? Get(Guid deviceId) => _items.TryGetValue(deviceId, out var c) ? c : null;
        public string? GetUsername(Guid deviceId) => Get(deviceId)?.Username;
        public void Save(Guid deviceId, DeviceCredential? credential)
        {
            if (credential is null) _items.TryRemove(deviceId, out _); else _items[deviceId] = credential;
        }
    }
}

static class SimTargets
{
    public const int ModbusPort = 15020, S7Port = 11102, OpcPort = 14841, MqttPort = 11883;

    public static List<SoakTarget> Build(SoakOptions o)
    {
        var now = DateTimeOffset.UtcNow;
        SoakTarget Target(string name, string protocol, string host, int port, Func<Guid, List<SoakTag>> tags)
        {
            var id = Guid.NewGuid();
            if (protocol is "OPC UA" or "MQTT")
                SoakCredentials.Store.Save(id, new DeviceCredential(SoakCredentials.SimUser, SoakCredentials.SimPassword));
            return new SoakTarget(name, new DeviceDefinition(id, name, protocol, "Configured", host, port, 1, 1000, now, now), tags(id));
        }
        SoakTag T(Guid device, string name, string address, TagDataType type, Expectation e,
            ModbusByteOrder order = ModbusByteOrder.ABCD, int? bit = null) =>
            new(new TagDefinition(Guid.NewGuid(), device, name, address, type, bit, order, "", 1, 0, 0, true, now, now), e);
        var c = Math.Clamp(o.Counters, 1, 1000);
        var mqttCounters = Math.Min(c, 50);
        return
        [
            Target("Modbus sim", "Modbus TCP", "127.0.0.1", ModbusPort, id =>
            [
                .. Enumerable.Range(0, c).Select(k => T(id, $"HR{k}", (40001 + k).ToString(), TagDataType.Word, new(k, null))),
                .. Enumerable.Range(0, Math.Min(c, 100)).Select(k => T(id, $"IR{k}", (30001 + k).ToString(), TagDataType.Word, new(k, null))),
                T(id, "Word", "41001", TagDataType.Word, new(null, 1234)),
                T(id, "Int16", "41002", TagDataType.Int16, new(null, -1234)),
                T(id, "Int32", "41003", TagDataType.Int32, new(null, -123456)),
                T(id, "UInt32", "41005", TagDataType.UInt32, new(null, 4000000000)),
                T(id, "FloatABCD", "41007", TagDataType.Float32, new(null, 12.5)),
                T(id, "FloatCDAB", "41009", TagDataType.Float32, new(null, 12.5), ModbusByteOrder.CDAB),
                T(id, "Coil", "00001", TagDataType.Bool, new(null, 1)),
                T(id, "Discrete", "10001", TagDataType.Bool, new(null, 1)),
                // Six-digit addresses above register 9999 and 64-bit values.
                T(id, "Float64", "419000", TagDataType.Float64, new(null, 12.5)),
                T(id, "UInt64", "419004", TagDataType.UInt64, new(null, 5000000000)),
                T(id, "Int64CDAB", "419008", TagDataType.Int64, new(null, -123456789012), ModbusByteOrder.CDAB),
            ]),
            Target("S7 sim", "Siemens S7", "127.0.0.1", S7Port, id =>
            [
                .. Enumerable.Range(0, Math.Min(c, 200)).Select(k => T(id, $"DBW{2 * k}", $"DB1.DBW{2 * k}", TagDataType.Word, new(k, null))),
                T(id, "Word", "DB1.DBW400", TagDataType.Word, new(null, 1234)),
                T(id, "Int16", "DB1.DBW402", TagDataType.Int16, new(null, -1234)),
                T(id, "Int32", "DB1.DBD404", TagDataType.Int32, new(null, -123456)),
                T(id, "UInt32", "DB1.DBD408", TagDataType.UInt32, new(null, 4000000000)),
                T(id, "Float", "DB1.DBD412", TagDataType.Float32, new(null, 12.5)),
                T(id, "Bit0", "DB1.DBX416.0", TagDataType.Bool, new(null, 1)),
                T(id, "Bit1", "DB1.DBX416.1", TagDataType.Bool, new(null, 0)),
                T(id, "Bit3", "DB1.DBX416.3", TagDataType.Bool, new(null, 1)),
                // Inputs, outputs and bit memory.
                T(id, "Input", "I0.1", TagDataType.Bool, new(null, 1)),
                T(id, "InputWord", "%IW2", TagDataType.Int16, new(null, -1234)),
                T(id, "Output", "Q1.7", TagDataType.Bool, new(null, 1)),
                T(id, "OutputWord", "QW4", TagDataType.Word, new(null, 1234)),
                T(id, "MerkerReal", "MD4", TagDataType.Float32, new(null, 12.5)),
                T(id, "MerkerBit", "M8.0", TagDataType.Bool, new(null, 1)),
            ]),
            Target("OPC UA sim", "OPC UA", $"opc.tcp://127.0.0.1:{OpcPort}/prognode-soak/", OpcPort, id =>
            [
                .. Enumerable.Range(0, c).Select(k => T(id, $"Counter{k}", $"ns=2;s=Counter{k}", TagDataType.Word, new(k, null))),
                T(id, "Word", "ns=2;s=FixedWord", TagDataType.Word, new(null, 1234)),
                T(id, "Int16", "ns=2;s=FixedInt16", TagDataType.Int16, new(null, -1234)),
                T(id, "Int32", "ns=2;s=FixedInt32", TagDataType.Int32, new(null, -123456)),
                T(id, "UInt32", "ns=2;s=FixedUInt32", TagDataType.UInt32, new(null, 4000000000)),
                T(id, "Float", "ns=2;s=FixedFloat", TagDataType.Float32, new(null, 12.5)),
                T(id, "Bool", "ns=2;s=FixedBool", TagDataType.Bool, new(null, 1)),
                T(id, "Double", "ns=2;s=FixedDouble", TagDataType.Float64, new(null, 12.5)),
                T(id, "Int64", "ns=2;s=FixedInt64", TagDataType.Int64, new(null, -123456789012)),
            ]),
            Target("MQTT sim", "MQTT", "mqtt://127.0.0.1", MqttPort, id =>
            [
                .. Enumerable.Range(0, mqttCounters).Select(k => T(id, $"c{k}", $"soak/c/{k}", TagDataType.Word, new(k, null))),
                T(id, "Word", "soak/s/word", TagDataType.Word, new(null, 1234)),
                T(id, "Float", "soak/s/float", TagDataType.Float32, new(null, 12.5)),
                T(id, "Bool", "soak/s/bool", TagDataType.Bool, new(null, 1)),
                // JSON message read field by field.
                T(id, "JsonFloat", "soak/s/json#temp", TagDataType.Float32, new(null, 12.5)),
                T(id, "JsonNested", "soak/s/json#sensors[1].value", TagDataType.Int32, new(null, -123456)),
                T(id, "JsonBig", "soak/s/json#energy", TagDataType.UInt64, new(null, 5000000000)),
            ]),
        ];
    }
}

/// <summary>
/// Real devices: a JSON file with targets. Each Tag may carry "expect": a number (fixed value) or
/// "counter:k"; without it any Good value counts. Example: tools/protocol-soak/real-plcs.example.json.
/// </summary>
static class ConfigTargets
{
    public static List<SoakTarget> Load(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var now = DateTimeOffset.UtcNow;
        var list = new List<SoakTarget>();
        foreach (var t in doc.RootElement.GetProperty("targets").EnumerateArray())
        {
            var id = Guid.NewGuid();
            var device = new DeviceDefinition(id, t.GetProperty("name").GetString()!, t.GetProperty("protocol").GetString()!,
                "Configured", t.GetProperty("host").GetString(), t.TryGetProperty("port", out var p) ? p.GetInt32() : null,
                t.TryGetProperty("unitId", out var u) ? u.GetInt32() : 1,
                t.TryGetProperty("pollMs", out var ms) ? ms.GetInt32() : 1000, now, now);
            // Optional "username" / "password" for an MQTT broker or OPC UA server.
            if (t.TryGetProperty("username", out var user) && user.GetString() is { Length: > 0 } name)
                SoakCredentials.Store.Save(id, new DeviceCredential(name,
                    t.TryGetProperty("password", out var password) ? password.GetString() ?? "" : ""));
            var tags = new List<SoakTag>();
            foreach (var g in t.GetProperty("tags").EnumerateArray())
            {
                var expect = Expectation.Any;
                if (g.TryGetProperty("expect", out var e))
                    expect = e.ValueKind == JsonValueKind.Number ? new(null, e.GetDouble())
                        : e.GetString() is { } s && s.StartsWith("counter:") ? new(int.Parse(s[8..], CultureInfo.InvariantCulture), null)
                        : Expectation.Any;
                tags.Add(new SoakTag(new TagDefinition(Guid.NewGuid(), id, g.GetProperty("name").GetString()!,
                    g.GetProperty("address").GetString()!, Enum.Parse<TagDataType>(g.GetProperty("type").GetString()!, true),
                    g.TryGetProperty("bit", out var b) ? b.GetInt32() : null,
                    g.TryGetProperty("byteOrder", out var o) ? Enum.Parse<ModbusByteOrder>(o.GetString()!, true) : ModbusByteOrder.ABCD,
                    "", 1, 0, g.TryGetProperty("decimals", out var dp) ? dp.GetInt32() : 0, true, now, now), expect));
            }
            list.Add(new SoakTarget(device.Name, device, tags));
        }
        return list;
    }
}

// ----------------------------------------------------------------------------- measurements

sealed record ResourceSample(DateTimeOffset At, long ManagedBytes, long PrivateBytes, int Threads, int Handles);

sealed class Outage(DateTimeOffset start)
{
    public DateTimeOffset Start { get; } = start;
    public DateTimeOffset? Restored { get; set; }
    public DateTimeOffset? Detected { get; set; }
    public DateTimeOffset? Recovered { get; set; }
    public string? LastProblem { get; set; }
}

sealed class TargetStats(string name, string protocol, int tagCount)
{
    /// <summary>The first readings after start (connect, first MQTT message) are not counted.</summary>
    public static DateTimeOffset CountFrom = DateTimeOffset.UtcNow.AddSeconds(20);
    /// <summary>Real devices: an outage starts with the first bad reading and ends with the first fully correct one.</summary>
    public static bool AutoOutages;
    private readonly object _gate = new();
    private readonly List<double> _latency = [];
    private readonly List<string> _examples = [];
    private readonly Dictionary<string, int> _errors = [];
    public string Name { get; } = name;
    public string Protocol { get; } = protocol;
    public int TagCount { get; } = tagCount;
    public long Reads, FailedReads, Samples, Good, NotGood, Mismatches, OutageSamples;
    public List<Outage> Outages { get; } = [];
    private Outage? _open;

    public double GoodPercent { get { lock (_gate) return Samples == 0 ? 0 : 100.0 * Good / Samples; } }

    public void OutageStarted(DateTimeOffset at) { lock (_gate) { _open = new Outage(at); Outages.Add(_open); } }
    public void OutageEnded(DateTimeOffset at) { lock (_gate) { if (_open is not null) _open.Restored = at; } }

    public void Record(DateTimeOffset at, double ms, IReadOnlyList<TagValueSnapshot>? values, string? error, List<SoakTag> tags)
    {
        if (at < CountFrom) return;
        lock (_gate)
        {
            Reads++;
            _latency.Add(ms);
            if (AutoOutages && _open is null && (values is null || values.Any(v => v.Quality != TagQuality.Good)))
            {
                _open = new Outage(at) { Detected = at, Restored = at };
                Outages.Add(_open);
            }
            var inOutage = _open is not null && (_open.Restored is null || _open.Recovered is null);
            if (values is null)
            {
                FailedReads++;
                Count(error ?? "no values");
                if (_open is not null) { _open.Detected ??= at; _open.LastProblem = error; }
                return;
            }
            var byId = values.ToDictionary(v => v.TagId);
            var allGood = true;
            foreach (var t in tags)
            {
                Samples++;
                if (!byId.TryGetValue(t.Tag.Id, out var v) || v.Quality != TagQuality.Good || v.Value is null)
                {
                    allGood = false;
                    if (_open is not null) _open.LastProblem = $"{t.Tag.Name}: {v?.Quality} {v?.Error}";
                    if (inOutage) OutageSamples++; else { NotGood++; Count(v?.Error ?? $"{v?.Quality}" ?? "missing"); }
                    continue;
                }
                if (!t.Expect.Matches(v.Value.Value, v.Timestamp))
                {
                    allGood = false;
                    if (_open is not null) _open.LastProblem = $"{t.Tag.Name}: read {v.Value} expected {Describe(t.Expect, v.Timestamp)}";
                    if (inOutage) { OutageSamples++; continue; }
                    Mismatches++;
                    if (_examples.Count < 10) _examples.Add($"{at:HH:mm:ss} {t.Tag.Name} ({t.Tag.Address}) read {v.Value} expected {Describe(t.Expect, v.Timestamp)}");
                    continue;
                }
                Good++;
            }
            if (_open is not null)
            {
                if (!allGood && _open.Detected is null) _open.Detected = at;
                // Recovered = the first fully correct read that started after the device came back.
                if (allGood && _open.Restored is { } restored && at >= restored && _open.Recovered is null)
                { _open.Recovered = at.AddMilliseconds(ms); _open = null; }
            }
        }
    }

    private static string Describe(Expectation e, DateTimeOffset at) =>
        e.Counter is { } k ? $"≈{(at.ToUnixTimeSeconds() + k) % 30000}" : $"{e.Fixed}";

    private void Count(string reason)
    {
        var key = reason.Length > 120 ? reason[..120] : reason;
        _errors[key] = _errors.GetValueOrDefault(key) + 1;
    }

    public object ToSummary()
    {
        lock (_gate)
        {
            var sorted = _latency.OrderBy(x => x).ToArray();
            double P(double q) => sorted.Length == 0 ? 0 : sorted[Math.Min(sorted.Length - 1, (int)(q * sorted.Length))];
            return new
            {
                Name, Protocol, TagCount, Reads, FailedReads, Samples, Good, NotGood, Mismatches, OutageSamples,
                GoodPercentOutsideOutages = Samples - OutageSamples == 0 ? 0 : 100.0 * Good / (Samples - OutageSamples),
                LatencyP50 = P(0.5), LatencyP95 = P(0.95), LatencyMax = sorted.LastOrDefault(),
                Outages = Outages.Select(o => new
                {
                    o.Start, o.Restored,
                    DetectSeconds = o.Detected is { } d ? (d - o.Start).TotalSeconds : (double?)null,
                    RecoverSeconds = o.Recovered is { } r && o.Restored is { } s ? (r - s).TotalSeconds : (double?)null,
                    DownSeconds = o.Recovered is { } end ? (end - o.Start).TotalSeconds : (double?)null,
                    o.LastProblem,
                }).ToList(),
                Examples = _examples.ToList(),
                Errors = _errors.OrderByDescending(e => e.Value).Take(8).ToDictionary(e => e.Key, e => e.Value),
            };
        }
    }
}

// ----------------------------------------------------------------------------- simulators and faults

sealed class Simulators(string root, string work, SoakOptions options)
{
    private readonly Dictionary<string, Process> _processes = [];
    private readonly DateTimeOffset _endsAt = DateTimeOffset.UtcNow.AddMinutes(options.Minutes);
    private MqttServer? _broker;
    private string Python => options.Python ?? Path.Combine(root, "tools", "protocol-soak", ".venv", "Scripts", "python.exe");
    private string Script => Path.Combine(root, "tools", "protocol-soak", "simulators.py");

    public async Task StartAllAsync()
    {
        if (!File.Exists(Python))
            throw new InvalidOperationException($"Python venv not found at {Python}. See tools/protocol-soak/README.md.");
        await StartBrokerAsync();
        foreach (var kind in new[] { "modbus", "s7", "opcua", "mqtt" }) Start(kind);
        await Task.Delay(TimeSpan.FromSeconds(12)); // OPC UA creates its certificate on first start
    }

    private int Port(string kind) => kind switch
    {
        "modbus" => SimTargets.ModbusPort, "s7" => SimTargets.S7Port, "opcua" => SimTargets.OpcPort, _ => SimTargets.MqttPort
    };

    private void Start(string kind)
    {
        var psi = new ProcessStartInfo(Python)
        {
            UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true,
        };
        foreach (var a in new[] { Script, kind, "--port", Port(kind).ToString(), "--counters",
                     (kind == "mqtt" ? Math.Min(options.Counters, 50) : options.Counters).ToString(),
                     "--pki", Path.Combine(work, "opcua-server-pki"),
                     "--user", SoakCredentials.SimUser, "--password", SoakCredentials.SimPassword })
            psi.ArgumentList.Add(a);
        var p = Process.Start(psi)!;
        var log = Path.Combine(work, $"sim-{kind}.log");
        p.OutputDataReceived += (_, e) => { if (e.Data is not null) File.AppendAllText(log, e.Data + Environment.NewLine); };
        p.ErrorDataReceived += (_, e) => { if (e.Data is not null) File.AppendAllText(log, e.Data + Environment.NewLine); };
        p.BeginOutputReadLine(); p.BeginErrorReadLine();
        _processes[kind] = p;
    }

    private async Task StartBrokerAsync()
    {
        var factory = new MqttServerFactory();
        _broker = factory.CreateMqttServer(factory.CreateServerOptionsBuilder()
            .WithDefaultEndpoint().WithDefaultEndpointPort(SimTargets.MqttPort).Build());
        // Like most site brokers, the simulated one refuses anonymous clients.
        _broker.ValidatingConnectionAsync += args =>
        {
            args.ReasonCode = args.UserName == SoakCredentials.SimUser && args.Password == SoakCredentials.SimPassword
                ? MQTTnet.Protocol.MqttConnectReasonCode.Success
                : MQTTnet.Protocol.MqttConnectReasonCode.BadUserNameOrPassword;
            return Task.CompletedTask;
        };
        await _broker.StartAsync();
    }

    private async Task DownAsync(string kind)
    {
        if (kind == "mqtt-broker") { await _broker!.StopAsync(); _broker.Dispose(); _broker = null; return; }
        if (_processes.TryGetValue(kind, out var p) && !p.HasExited) { p.Kill(entireProcessTree: true); await p.WaitForExitAsync(); }
    }

    private async Task UpAsync(string kind)
    {
        if (kind == "mqtt-broker") await StartBrokerAsync(); else Start(kind);
    }

    /// <summary>Takes one simulator down at a time, round robin: Modbus, S7, OPC UA, MQTT broker.</summary>
    public async Task FaultLoopAsync(Dictionary<string, TargetStats> stats, CancellationToken stop)
    {
        var all = new Dictionary<string, string>
        {
            ["modbus"] = "Modbus sim", ["s7"] = "S7 sim", ["opcua"] = "OPC UA sim", ["mqtt-broker"] = "MQTT sim", ["mqtt"] = "MQTT sim",
        };
        var plan = options.Faults.Where(all.ContainsKey).Select(f => (f, all[f])).ToArray();
        if (plan.Length == 0) return;
        var i = 0;
        while (true)
        {
            try { await Task.Delay(TimeSpan.FromMinutes(options.FaultEveryMinutes), stop); } catch (OperationCanceledException) { return; }
            // No new outage near the end: its recovery must still be measured.
            if (DateTimeOffset.UtcNow.AddSeconds(options.FaultDownSeconds + 60) > _endsAt) return;
            var (kind, target) = plan[i++ % plan.Length];
            Console.WriteLine($"Fault: {kind} down for {options.FaultDownSeconds} s");
            stats[target].OutageStarted(DateTimeOffset.UtcNow);
            await DownAsync(kind);
            try { await Task.Delay(TimeSpan.FromSeconds(options.FaultDownSeconds), stop); } catch (OperationCanceledException) { }
            await UpAsync(kind);
            stats[target].OutageEnded(DateTimeOffset.UtcNow);
            if (stop.IsCancellationRequested) return;
        }
    }

    public async Task StopAllAsync()
    {
        foreach (var p in _processes.Values)
            try { if (!p.HasExited) { p.Kill(entireProcessTree: true); await p.WaitForExitAsync(); } }
            catch (InvalidOperationException) { }
        _processes.Clear();
        if (_broker is not null) { try { await _broker.StopAsync(); } catch (ObjectDisposedException) { } _broker = null; }
    }
}

// ----------------------------------------------------------------------------- report

static class Report
{
    public static (string Text, bool Passed) Write(SoakOptions o, List<TargetStats> stats, List<ResourceSample> res,
        DateTimeOffset start, DateTimeOffset end)
    {
        var sb = new StringBuilder();
        var passed = true;
        sb.AppendLine($"# PROGNODE protocol soak · {start.ToLocalTime():yyyy-MM-dd HH:mm} · {(end - start).TotalMinutes:0} min");
        sb.AppendLine();
        sb.AppendLine(o.ConfigPath is null
            ? $"Simulated devices; one taken down every {o.FaultEveryMinutes:0.#} min for {o.FaultDownSeconds} s. Poll interval 1 s."
            : $"Real devices from {Path.GetFileName(o.ConfigPath)}. An outage runs from the first bad reading to the first fully correct one; " +
              "for these, Recover is the outage length (detection is immediate by definition).");
        sb.AppendLine();
        sb.AppendLine("| Protocol | Tags | Reads | Good outside outages | Wrong values | Failed reads | Latency p50 / p95 / max (ms) | Outages | Detect (s) avg / max | Recover (s) avg / max | Result |");
        sb.AppendLine("|---|---:|---:|---:|---:|---:|---|---:|---|---|---|");
        foreach (var s in stats)
        {
            dynamic m = s.ToSummary();
            var outages = ((IEnumerable<dynamic>)m.Outages).ToList();
            var detect = outages.Where(x => x.DetectSeconds is not null).Select(x => (double)x.DetectSeconds).ToList();
            var recover = outages.Where(x => x.RecoverSeconds is not null).Select(x => (double)x.RecoverSeconds).ToList();
            var unrecovered = outages.Count(x => x.Restored is not null && x.RecoverSeconds is null);
            var ok = m.Mismatches == 0 && m.GoodPercentOutsideOutages >= 99.9 && unrecovered == 0 &&
                     (recover.Count == 0 || recover.Max() <= 60);
            passed &= ok;
            string Pair(List<double> l) => l.Count == 0 ? "—" : $"{l.Average():0.0} / {l.Max():0.0}";
            sb.AppendLine($"| {s.Protocol} | {s.TagCount} | {m.Reads} | {m.GoodPercentOutsideOutages:0.000}% | {m.Mismatches} | {m.FailedReads} | " +
                          $"{m.LatencyP50:0} / {m.LatencyP95:0} / {m.LatencyMax:0} | {outages.Count}{(unrecovered > 0 ? $" ({unrecovered} not recovered)" : "")} | " +
                          $"{Pair(detect)} | {Pair(recover)} | {(ok ? "PASS" : "FAIL")} |");
        }
        sb.AppendLine();
        if (res.Count > 1)
        {
            var first = res[Math.Min(res.Count - 1, 3)]; var last = res[^1];
            sb.AppendLine($"Memory (private): {first.PrivateBytes / 1048576.0:0} MB after warm-up → {last.PrivateBytes / 1048576.0:0} MB at the end " +
                          $"(max {res.Max(r => r.PrivateBytes) / 1048576.0:0} MB). Managed heap {last.ManagedBytes / 1048576.0:0} MB. " +
                          $"Threads {first.Threads} → {last.Threads}, handles {first.Handles} → {last.Handles}.");
            var growth = (last.PrivateBytes - first.PrivateBytes) / 1048576.0;
            if ((end - start).TotalMinutes >= 30 && growth > 100) { passed = false; sb.AppendLine($"**Memory grew by {growth:0} MB: possible leak.**"); }
        }
        sb.AppendLine();
        foreach (var s in stats)
        {
            dynamic m = s.ToSummary();
            var examples = (List<string>)m.Examples;
            var errors = (Dictionary<string, int>)m.Errors;
            if (examples.Count == 0 && errors.Count == 0 && !((IEnumerable<dynamic>)m.Outages).Any(x => x.RecoverSeconds is null)) continue;
            sb.AppendLine($"## {s.Protocol}");
            foreach (var e in examples) sb.AppendLine($"- wrong value: {e}");
            foreach (var e in errors) sb.AppendLine($"- {e.Value}× {e.Key}");
            foreach (var u in ((IEnumerable<dynamic>)m.Outages).Where(x => x.Restored is not null && x.RecoverSeconds is null))
                sb.AppendLine($"- not recovered after outage at {((DateTimeOffset)u.Start).ToLocalTime():HH:mm:ss}; last problem: {u.LastProblem}");
            sb.AppendLine();
        }
        sb.AppendLine("PASS means: no wrong value, at least 99.9 % Good readings outside outages, every outage recovered within 60 s, and (runs of 30 min or more) less than 100 MB memory growth.");
        return (sb.ToString(), passed);
    }
}
