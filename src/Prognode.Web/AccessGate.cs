using System.Net;
using Microsoft.AspNetCore.Http;
using Prognode.Contracts.Licensing;
using Prognode.Core.Connectivity;

namespace Prognode.Web;

/// <summary>
/// The two request gates in front of every endpoint (wired in Program.cs). Each returns false when it has
/// already written the refusal; then the request goes no further.
/// </summary>
public static class AccessGate
{
    /// <summary>
    /// Who may reach the API at all. Local browser/Agent traffic on loopback remains frictionless; remote API
    /// clients are paired once and then authenticate with a stable bearer token over LAN HTTPS.
    /// </summary>
    public static async Task<bool> NetworkAsync(HttpContext context, ServerAccessService serverAccess)
    {
        var remote = context.Connection.RemoteIpAddress;
        var isLoopback = remote is not null && IPAddress.IsLoopback(remote);
        var path = context.Request.Path;

        // DNS rebinding: a web page whose own name resolves to 127.0.0.1 must not reach the sign-in-free local API.
        if (isLoopback && !context.Request.IsHttps &&
            context.Request.Host.Host is not ("localhost" or "127.0.0.1" or "[::1]" or "::1"))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { code = "LOCAL_HOST_REQUIRED", message = "Open PROGNODE Monitor at http://127.0.0.1:5080 or http://localhost:5080." });
            return false;
        }

        var publicClientRoute =
            path.StartsWithSegments("/api/server/identity") ||
            (string.Equals(path.Value, "/api/client/pair", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(path.Value, "/api/client/pair-qr", StringComparison.OrdinalIgnoreCase) ||
             (path.StartsWithSegments("/api/mobile-access/phone-probe") && HttpMethods.IsGet(context.Request.Method)));

        // When LAN HTTPS is configured, the public HTTP port is loopback only.
        if (!isLoopback && path.StartsWithSegments("/api") && !context.Request.IsHttps)
        {
            context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
            await context.Response.WriteAsJsonAsync(new {
                code = "LAN_HTTPS_REQUIRED",
                message = "Pairing and authentication on a non-loopback network require HTTPS."
            });
            return false;
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
                return false;
            }

            // Device view grant must also cover the legacy read API. Otherwise a device
            // denied /api/mobile/v2 could bypass via /api/alarms or /api/notifications.
            if (!pairedClient.CanViewAlarms &&
                (path.StartsWithSegments("/api/alarms") ||
                 path.StartsWithSegments("/api/notifications")))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new { code = "ALARM_VIEW_FORBIDDEN" });
                return false;
            }
            context.Items["PROGNODE_REMOTE_PAIRED"] = true;
            context.Items["PROGNODE_CLIENT_ID"] = pairedClient.ClientId;
        }
        return true;
    }

    /// <summary>
    /// Who may change anything. Unlicensed / unassigned installations remain fully observable but read-only.
    /// A verified offline license account session is the authentication gate. Portal roles are deliberately
    /// NOT mapped to industrial Administrator/Engineer/Operator roles.
    /// </summary>
    public static async Task<bool> ChangesAsync(HttpContext context, Func<LicenseSnapshot> license, Func<string, bool> validSession)
    {
        var method = context.Request.Method;
        var path = context.Request.Path;
        var mutating = !(HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method));
        if (!mutating || !path.StartsWithSegments("/api")) return true;
        var exempt =
            path.StartsWithSegments("/api/license/import") ||
            // Only asks PROGNODE Account for the newest version; changes nothing.
            string.Equals(path.Value, "/api/system/update/check", StringComparison.OrdinalIgnoreCase) ||
            // Connecting to an account installs a license; once one is valid this needs a signed-in session.
            (path.StartsWithSegments("/api/license/link") && !license().IsValid) ||
            path.StartsWithSegments("/api/access/login") ||
            path.StartsWithSegments("/api/access/logout") ||
            (string.Equals(path.Value, "/api/client/pair", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(path.Value, "/api/client/pair-qr", StringComparison.OrdinalIgnoreCase)) ||
            path.StartsWithSegments("/api/agent/heartbeat") ||
            (string.Equals(path.Value, "/api/mobile-access/agent/claim", StringComparison.OrdinalIgnoreCase) &&
             context.Connection.RemoteIpAddress is { } agentIp && IPAddress.IsLoopback(agentIp));
        if (exempt) return true;

        var snapshot = license();
        if (!snapshot.IsValid || string.IsNullOrWhiteSpace(snapshot.AssignedUserId))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            await context.Response.WriteAsJsonAsync(new
            {
                message = "PROGNODE is in View Only mode. Import an ACTIVE, EXPIRING_SOON or GRACE .pgnlicense and sign in to enable configuration changes."
            });
            return false;
        }

        var occurrenceAckRoute = path.Value is { } requestPath &&
            requestPath.StartsWith("/api/mobile/v2/alarms/", StringComparison.OrdinalIgnoreCase) &&
            requestPath.EndsWith("/ack", StringComparison.OrdinalIgnoreCase) &&
            HttpMethods.IsPost(method);
        // A paired device (bearer token validated above) turns Remote Access on or off for itself:
        // it signs the cloud challenge with its own key, and the endpoints only act on the caller.
        var pairedSelfRemoteRoute = context.Items.ContainsKey("PROGNODE_CLIENT_ID") &&
            path.StartsWithSegments("/api/remote-access/clients") &&
            ((HttpMethods.IsPost(method) &&
              (string.Equals(path.Value, "/api/remote-access/clients/challenge", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(path.Value, "/api/remote-access/clients/register", StringComparison.OrdinalIgnoreCase))) ||
             HttpMethods.IsDelete(method));
        // Only these endpoints can authenticate using an explicit, server-stored device grant.
        // They perform their own bearer/session check; all other mutations still require login.
        if (!validSession(context.Request.Headers["X-PROGNODE-Session"].ToString()) && !occurrenceAckRoute && !pairedSelfRemoteRoute)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new
            {
                message = "Sign in with the user assigned to this PROGNODE license before changing configuration."
            });
            return false;
        }
        return true;
    }
}
