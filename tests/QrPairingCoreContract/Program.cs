using System.Net;
using System.Text;
using System.Text.Json;
using Prognode.Core.Connectivity;

var temp = Path.Combine(Path.GetTempPath(), "prognode-qr-contract-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temp);
try
{
    var server = new ServerAccessService(temp, 5080, 5081, "QR test Core", 5443, new string('A', 64));
    var clock = new TestClock();
    var hosts = new[] { new QrNetworkOption("192.168.50.10", "Test Ethernet") };
    var qr = new QrPairingService(server, clock, () => hosts);
    var admin = "test-admin-session";
    static string Ticket(QrPairingCreation created)
    {
        var raw = created.QrPayload.Replace('-', '+').Replace('_', '/');
        raw = raw.PadRight((raw.Length + 3) / 4 * 4, '=');
        using var json = JsonDocument.Parse(Convert.FromBase64String(raw));
        var doc = json.RootElement;
        Check(doc.GetProperty("v").GetInt32() == 1, "QR payload version");
        Check(doc.GetProperty("type").GetString() == "PROGNODE_LAN_PAIRING", "QR payload type");
        Check(doc.GetProperty("httpsPort").GetInt32() == 5443, "HTTPS port");
        Check(doc.GetProperty("certSha256").GetString()!.Length == 64, "certificate pin");
        Check(doc.GetProperty("lanHosts")[0].GetString() == "192.168.50.10", "selected local host");
        return doc.GetProperty("pairingTicket").GetString()!;
    }
    static void Check(bool result, string check)
    {
        if (!result) throw new Exception("FAIL: " + check);
        Console.WriteLine("PASS " + check);
    }
    static void Rejected(Action fn, string expected)
    {
        try { fn(); throw new Exception("FAIL: expected " + expected); }
        catch (QrPairingProblem problem) when (problem.Code == expected)
        { Console.WriteLine("PASS rejects " + expected); }
    }
    var first = qr.Create(admin, null);
    var t = Ticket(first);
    Check(t.Length == 43, "32-byte cryptographic pairing ticket");
    Check(first.ExpiresInSeconds == 120, "120-second expiry");
    Check(qr.Status(admin).Status == "PENDING", "pending QR visibility");
    var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(i => Task.Run(() =>
    {
        try { return qr.Pair(server.Identity.ServerId, t, "Samsung Galaxy", "ANDROID", "public-key", IPAddress.Parse("192.168.50.11"), session => true).ClientId.ToString(); }
        catch (QrPairingProblem ex) { return ex.Code; }
    })));
    Check(results.Count(x => Guid.TryParse(x, out _)) == 1 && results.Count(x => x == "ALREADY_USED") == 1,
        "parallel pairing consumes ticket exactly once");
    Check(qr.Status(admin).Status == "PAIRED", "admin sees paired status");
    var saved = File.ReadAllText(Path.Combine(temp, "server-access.json"));
    Check(!saved.Contains(t, StringComparison.Ordinal), "pairing ticket is never persisted");
    Check(server.GetClients().Count == 1, "paired client stored through original ServerAccessService");
    var next = qr.Create(admin, null);
    var nextTicket = Ticket(next);
    Rejected(() => qr.Pair(server.Identity.ServerId, t, "Bad", "ANDROID", null, IPAddress.Parse("192.168.50.12"), _=>true), "ALREADY_USED");
    qr.Cancel(admin);
    Rejected(() => qr.Pair(server.Identity.ServerId, nextTicket, "Bad", "ANDROID", null, IPAddress.Parse("192.168.50.13"), _=>true), "REVOKED");
    var expired = qr.Create(admin, null);
    clock.Advance(TimeSpan.FromSeconds(121));
    Rejected(() => qr.Pair(server.Identity.ServerId, Ticket(expired), "Bad", "ANDROID", null, IPAddress.Parse("192.168.50.14"), _=>true), "EXPIRED");
    var relogged = qr.Create(admin, null);
    Rejected(() => qr.Pair(server.Identity.ServerId, Ticket(relogged), "Bad", "ANDROID", null, IPAddress.Parse("192.168.50.15"), _=>false), "REVOKED");
    var manual = server.Pair("Legacy Windows Client", server.GetPairingCode(), "WINDOWS");
    Check(server.GetClients().Any(x=>x.ClientId==manual.ClientId), "legacy six-digit pairing preserved");
    Check(server.GetClients().Count == 2, "QR and legacy clients both persisted");
    Console.WriteLine("RESULT QR contract tests passed");
}
finally { Directory.Delete(temp, recursive: true); }

sealed class TestClock : TimeProvider
{
    private DateTimeOffset _now = new(2026, 9, 27, 10, 0, 0, TimeSpan.Zero);
    public override DateTimeOffset GetUtcNow() => _now;
    public void Advance(TimeSpan by) => _now = _now.Add(by);
}
