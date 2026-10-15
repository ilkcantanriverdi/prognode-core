using System.Net;
using Microsoft.AspNetCore.Http;
using Prognode.Contracts.Licensing;
using Prognode.Core.Connectivity;
using Prognode.Web;

/// <summary>
/// The request gates every endpoint sits behind: loopback Host check, LAN HTTPS, paired bearer token,
/// alarm view grant, and the sign-in requirement for changes with its exemptions.
/// </summary>
internal static class AccessGateContract
{
    public static async Task RunAsync(string root)
    {
        var server = new ServerAccessService(Path.Combine(root, "gate"), 5080, 5081, "GATE TEST");
        var phone = server.Pair("Gate Phone", server.GetPairingCode(), "ANDROID");
        var blind = server.Pair("Blind Phone", server.GetPairingCode(), "ANDROID");
        server.SetDeviceAccess(blind.ClientId, "Blind Phone", false, false, "USER_SESSION", "qa@example.invalid");

        async Task<(bool Passed, int Status, HttpContext Context)> Network(string ip, bool https, string host, string path, string? token = null, string method = "GET")
        {
            var context = Context(ip, https, host, path, method);
            if (token is not null) context.Request.Headers.Authorization = "Bearer " + token;
            var passed = await AccessGate.NetworkAsync(context, server);
            return (passed, context.Response.StatusCode, context);
        }

        // Loopback: the local browser and Agent, but not a page that rebinds its own name to 127.0.0.1.
        Require((await Network("127.0.0.1", false, "127.0.0.1:5080", "/api/tags/values")).Passed, "Local browser on 127.0.0.1.");
        Require((await Network("::1", false, "localhost:5080", "/api/devices")).Passed, "Local browser on localhost.");
        var rebind = await Network("127.0.0.1", false, "evil.example:5080", "/api/tags/values");
        Require(!rebind.Passed && rebind.Status == 400, "DNS rebinding host must be refused.");
        Require((await Network("127.0.0.1", true, "plant-pc:5443", "/api/devices")).Passed, "Loopback HTTPS (Client on the Monitor PC) keeps working.");

        // LAN: HTTPS and a paired token, except the public pairing routes.
        Require((await Network("192.168.1.5", false, "plant-pc:5080", "/api/devices")).Status == 426, "LAN HTTP is refused.");
        Require((await Network("192.168.1.5", true, "plant-pc:5443", "/api/devices")).Status == 401, "LAN without a token is refused.");
        Require((await Network("192.168.1.5", true, "plant-pc:5443", "/api/devices", "wrong-token")).Status == 401, "LAN with a wrong token is refused.");
        Require((await Network("192.168.1.5", true, "plant-pc:5443", "/api/server/identity")).Passed, "Server identity is public.");
        Require((await Network("192.168.1.5", true, "plant-pc:5443", "/api/client/pair", method: "POST")).Passed, "Pairing is public.");
        var paired = await Network("192.168.1.5", true, "plant-pc:5443", "/api/alarms/active", phone.AccessToken);
        Require(paired.Passed && paired.Context.Items["PROGNODE_CLIENT_ID"] is Guid id && id == phone.ClientId, "A paired phone passes and is identified.");
        Require((await Network("192.168.1.5", true, "plant-pc:5443", "/api/alarms/active", blind.AccessToken)).Status == 403, "No alarm view grant: /api/alarms refused.");
        Require((await Network("192.168.1.5", true, "plant-pc:5443", "/api/notifications", blind.AccessToken)).Status == 403, "No alarm view grant: /api/notifications refused.");

        // Changes: a valid, assigned license and a signed-in session; reads and the listed routes are exempt.
        var licensed = License(valid: true);
        var unlicensed = License(valid: false);
        async Task<(bool Passed, int Status)> Change(string path, LicenseSnapshot license, string? session = null, string method = "POST", bool pairedDevice = false)
        {
            var context = Context("127.0.0.1", false, "127.0.0.1:5080", path, method);
            if (session is not null) context.Request.Headers["X-PROGNODE-Session"] = session;
            if (pairedDevice) context.Items["PROGNODE_CLIENT_ID"] = phone.ClientId;
            var passed = await AccessGate.ChangesAsync(context, () => license, token => token == "signed-in");
            return (passed, context.Response.StatusCode);
        }
        Require((await Change("/api/devices", licensed, method: "GET")).Passed, "Reads need no session.");
        Require((await Change("/api/devices", unlicensed, "signed-in")).Status == 403, "No valid license: View Only.");
        Require((await Change("/api/devices", licensed)).Status == 401, "A change needs a session.");
        Require((await Change("/api/devices", licensed, "forged")).Status == 401, "An unknown session is refused.");
        Require((await Change("/api/devices", licensed, "signed-in")).Passed, "A signed-in change passes.");
        Require((await Change("/api/license/import", unlicensed)).Passed, "A license can be imported without one.");
        Require((await Change("/api/license/link/start", unlicensed)).Passed, "An account can be linked while unlicensed.");
        Require((await Change("/api/license/link/start", licensed)).Status == 401, "Re-linking a licensed Monitor needs a session.");
        Require((await Change("/api/access/login", licensed)).Passed, "Sign-in itself is exempt.");
        Require((await Change($"/api/mobile/v2/alarms/{Guid.NewGuid()}/ack", licensed)).Passed, "Occurrence ACK checks its own device grant.");
        Require((await Change("/api/remote-access/clients/register", licensed, pairedDevice: true)).Passed, "A paired device registers itself for Remote Access.");
        Require((await Change("/api/remote-access/clients/register", licensed)).Status == 401, "Without a paired device that route needs a session.");
        Console.WriteLine("PASS: access gate (loopback host, LAN HTTPS, paired token, alarm grant, sign-in for changes and its exemptions)");
    }

    private static DefaultHttpContext Context(string ip, bool https, string host, string path, string method)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse(ip);
        context.Request.Scheme = https ? "https" : "http";
        context.Request.Host = new HostString(host);
        context.Request.Path = path;
        context.Request.Method = method;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static LicenseSnapshot License(bool valid) =>
        new("lic-test", "Test Plant", "ALARM_HISTORIAN", valid ? "ACTIVE" : "NONE", valid, null, null, null!, "TEST", AssignedUserId: valid ? "u-1" : null);

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }
}
