using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Prognode.Contracts.Notifications;
using Prognode.Licensing;

namespace Prognode.RemoteAccess;

internal sealed class RemoteAccessCloudStatusResponse
{
    public bool Enabled { get; set; }
    public bool ServerBound { get; set; }
    public string BindingStatus { get; set; } = "UNKNOWN";
    public string SubscriptionStatus { get; set; } = "UNKNOWN";
    public bool UnlimitedClients { get; set; }
    public int? MaxClients { get; set; }
    public int UsedClients { get; set; }
    public DateTimeOffset? ExpiresAtUtc { get; set; }
    public List<RemoteAccessCloudClientDocument> Clients { get; set; } = [];
}

internal sealed class RemoteAccessCloudOperationResponse
{
    public bool Success { get; set; }
    public string Code { get; set; } = "UNKNOWN";
    public string Message { get; set; } = string.Empty;
    public RemoteAccessCloudStatusResponse? Status { get; set; }
    public Guid? RemoteClientId { get; set; }
    public string? RemoteClientToken { get; set; }
    public DateTimeOffset? RemoteClientTokenExpiresAtUtc { get; set; }
}

/// <summary>One-time proof request: the device signs <see cref="Challenge"/> with its Ed25519 key.</summary>
public sealed record RemoteAccessChallenge(Guid ChallengeId, string Challenge, DateTimeOffset ExpiresAtUtc);

public sealed class RemoteAccessCommand
{
    public string CommandId { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public string AlarmKey { get; set; } = string.Empty;
    public Guid? OccurrenceId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string? UserDisplayName { get; set; }
    public Guid? RemoteClientId { get; set; }
    public DateTimeOffset IssuedAtUtc { get; set; }
}

/// <summary>
/// PROGNODE Cloud Remote Access relay (account.prognode.io /api/remote-access/*). Core authenticates
/// with its license activation token; hosts come from the license API allow-list (HTTPS
/// *.prognode.io in Release builds). Alarm events carry name, severity, state and time only.
/// </summary>
public sealed class RemoteAccessCloudClient(HttpClient httpClient, CoreCloudLicenseOptions options)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    // RFC 8410 SubjectPublicKeyInfo prefix for a raw 32-byte Ed25519 public key.
    private static readonly byte[] Ed25519SpkiPrefix = [0x30, 0x2A, 0x30, 0x05, 0x06, 0x03, 0x2B, 0x65, 0x70, 0x03, 0x21, 0x00];

    public bool IsConfigured => options.IsConfigured;

    private string BaseUrl => options.CandidateBaseUrls.FirstOrDefault()
        ?? throw new InvalidOperationException("PROGNODE Remote Access cloud endpoint is not configured.");

    internal async Task<RemoteAccessCloudStatusResponse> GetStatusAsync(string token, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/remote-access/entitlement", token, null, cancellationToken);
        using var document = await ReadJsonAsync(response, cancellationToken);
        var root = document.RootElement;
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(ErrorMessage(root, response));

        var status = ParseEntitlement(root);
        if (status.ServerBound && status.Enabled)
            status.Clients = await GetClientsAsync(token, cancellationToken);
        return status;
    }

    internal async Task<RemoteAccessCloudOperationResponse> BindServerAsync(string token, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, "/api/remote-access/activate", token, new { }, cancellationToken);
        using var document = await ReadJsonAsync(response, cancellationToken);
        var root = document.RootElement;
        if (!response.IsSuccessStatusCode)
            return Failure(root, response);
        return new RemoteAccessCloudOperationResponse
        {
            Success = true,
            Code = Bool(root, "boundNow") == true ? "BOUND" : "ALREADY_BOUND",
            Message = "Remote Access subscription is bound to this PROGNODE Server.",
            Status = ParseEntitlement(root, bound: true),
        };
    }

    internal async Task<RemoteAccessChallenge> CreateChallengeAsync(string token, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, "/api/remote-access/clients/challenge", token, new { }, cancellationToken);
        using var document = await ReadJsonAsync(response, cancellationToken);
        var root = document.RootElement;
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(ErrorMessage(root, response));
        if (!Guid.TryParse(Str(root, "challengeId"), out var id) || Str(root, "challenge") is not { Length: > 0 } challenge)
            throw new InvalidOperationException("Remote Access cloud returned an invalid challenge.");
        return new RemoteAccessChallenge(id, challenge, Time(root, "expiresAtUtc") ?? DateTimeOffset.UtcNow.AddMinutes(5));
    }

    internal async Task<RemoteAccessCloudOperationResponse> RegisterClientAsync(
        string token,
        string deviceName,
        string platform,
        string devicePublicKey,
        Guid challengeId,
        string deviceSignature,
        string? userId,
        CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, "/api/remote-access/clients/register", token, new
        {
            deviceName,
            platform = CloudPlatform(platform),
            devicePublicKey = ToSpkiBase64Url(devicePublicKey),
            challengeId,
            deviceSignature,
            userId,
        }, cancellationToken);
        using var document = await ReadJsonAsync(response, cancellationToken);
        var root = document.RootElement;
        if (!response.IsSuccessStatusCode)
            return Failure(root, response);
        if (!Guid.TryParse(Str(root, "remoteClientId"), out var remoteClientId) || Str(root, "remoteClientToken") is not { Length: > 0 } clientToken)
            return new RemoteAccessCloudOperationResponse { Success = false, Code = "INVALID_CLOUD_CONTRACT", Message = "Remote Access cloud did not return a device credential." };
        return new RemoteAccessCloudOperationResponse
        {
            Success = true,
            Code = "REGISTERED",
            Message = "Remote Access enabled for this device. One remote seat is now in use.",
            RemoteClientId = remoteClientId,
            RemoteClientToken = clientToken,
            RemoteClientTokenExpiresAtUtc = Time(root, "tokenExpiresAtUtc"),
        };
    }

    internal async Task<RemoteAccessCloudOperationResponse> RevokeClientAsync(string token, Guid remoteClientId, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Post, $"/api/remote-access/clients/{remoteClientId:D}/revoke", token, new { }, cancellationToken);
        using var document = await ReadJsonAsync(response, cancellationToken);
        if (!response.IsSuccessStatusCode && !(Str(document.RootElement, "error") == "remote_client_not_found"))
            return Failure(document.RootElement, response);
        return new RemoteAccessCloudOperationResponse { Success = true, Code = "REVOKED", Message = "Remote Access revoked and the seat was released. LAN pairing remains available." };
    }

    internal async Task<bool> PublishNotificationAsync(string token, NotificationEvent notification, CancellationToken cancellationToken = default)
    {
        // Decision: only alarm name, severity, state and time leave the site. Message text (which
        // may contain process values) is never sent.
        var eventId = notification.OccurrenceId is { } occurrence && occurrence != Guid.Empty
            ? occurrence.ToString("D")
            : "notification-" + notification.Id.ToString(CultureInfo.InvariantCulture);
        var alarmName = string.IsNullOrWhiteSpace(notification.SourceName) ? notification.Title : notification.SourceName!;
        using var response = await SendAsync(HttpMethod.Post, "/api/remote-access/alarms/events", token, new
        {
            eventId,
            alarmName = Truncate(alarmName, 240),
            severity = Truncate(string.IsNullOrWhiteSpace(notification.Severity) ? "INFO" : notification.Severity, 64),
            state = Truncate(CloudState(notification), 64),
            timestampUtc = (notification.ActiveAtUtc ?? notification.Timestamp).ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            displayMetadata = new { repeatSequence = notification.RepeatSequence, requiresAcknowledgement = notification.RequiresAcknowledgement },
        }, cancellationToken);
        if (!response.IsSuccessStatusCode ||
            !string.Equals(response.Content.Headers.ContentType?.MediaType, "application/json", StringComparison.OrdinalIgnoreCase))
            return false; // a 200 HTML page is not a relay ACK
        using var document = await ReadJsonAsync(response, cancellationToken);
        return Bool(document.RootElement, "ok") == true && Str(document.RootElement, "eventId") == eventId;
    }

    internal async Task<IReadOnlyList<RemoteAccessCommand>> GetCommandsAsync(string token, CancellationToken cancellationToken = default)
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/remote-access/commands?limit=25", token, null, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return [];
        using var document = await ReadJsonAsync(response, cancellationToken);
        if (!document.RootElement.TryGetProperty("commands", out var commands) || commands.ValueKind != JsonValueKind.Array)
            return [];
        var result = new List<RemoteAccessCommand>();
        foreach (var item in commands.EnumerateArray())
        {
            var client = item.TryGetProperty("remoteClient", out var rc) && rc.ValueKind == JsonValueKind.Object ? rc : default;
            result.Add(new RemoteAccessCommand
            {
                CommandId = Str(item, "commandId") ?? string.Empty,
                Type = Str(item, "type") ?? string.Empty,
                AlarmKey = string.Empty,
                OccurrenceId = Guid.TryParse(Str(item, "alarmEventId"), out var occurrence) ? occurrence : null,
                UserId = client.ValueKind == JsonValueKind.Object ? Str(client, "userId") ?? string.Empty : string.Empty,
                UserDisplayName = client.ValueKind == JsonValueKind.Object ? Str(client, "deviceName") : null,
                RemoteClientId = client.ValueKind == JsonValueKind.Object && Guid.TryParse(Str(client, "id"), out var rid) ? rid : null,
                IssuedAtUtc = Time(item, "requestedAtUtc") ?? default,
            });
        }
        return result;
    }

    internal async Task<bool> CompleteCommandAsync(string token, string commandId, bool success, string resultCode, CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(commandId, out var id))
            return false;
        using var response = await SendAsync(HttpMethod.Post, $"/api/remote-access/commands/{id:D}/complete", token, new
        {
            status = success ? "COMPLETED" : "REJECTED",
            result = new { code = resultCode },
        }, cancellationToken);
        // 404 means already completed elsewhere: nothing left to retry.
        return response.IsSuccessStatusCode || response.StatusCode == System.Net.HttpStatusCode.NotFound;
    }

    private async Task<List<RemoteAccessCloudClientDocument>> GetClientsAsync(string token, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(HttpMethod.Get, "/api/remote-access/clients", token, null, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return [];
        using var document = await ReadJsonAsync(response, cancellationToken);
        if (!document.RootElement.TryGetProperty("clients", out var clients) || clients.ValueKind != JsonValueKind.Array)
            return [];
        var list = new List<RemoteAccessCloudClientDocument>();
        foreach (var item in clients.EnumerateArray())
        {
            if (!Guid.TryParse(Str(item, "remoteClientId"), out var id))
                continue;
            var user = item.TryGetProperty("assignedUser", out var u) && u.ValueKind == JsonValueKind.Object ? u : default;
            list.Add(new RemoteAccessCloudClientDocument
            {
                RemoteClientId = id,
                DeviceName = Str(item, "deviceName") ?? string.Empty,
                Platform = Str(item, "platform") ?? string.Empty,
                Status = Str(item, "status") ?? "ACTIVE",
                UserId = user.ValueKind == JsonValueKind.Object ? Str(user, "userId") : null,
                UserDisplayName = user.ValueKind == JsonValueKind.Object ? Str(user, "displayName") : null,
                RegisteredAtUtc = Time(item, "activatedAtUtc"),
                LastSeenAtUtc = Time(item, "lastSeenAtUtc"),
            });
        }
        return list;
    }

    private static RemoteAccessCloudStatusResponse ParseEntitlement(JsonElement root, bool bound = false)
    {
        var binding = Str(root, "serverBindingStatus") ?? (bound ? "BOUND" : "UNKNOWN");
        var unlimited = root.TryGetProperty("maxClients", out var max) && max.ValueKind == JsonValueKind.String &&
            string.Equals(max.GetString(), "unlimited", StringComparison.OrdinalIgnoreCase);
        return new RemoteAccessCloudStatusResponse
        {
            Enabled = Bool(root, "remoteAccessEnabled") ?? false,
            ServerBound = string.Equals(binding, "BOUND", StringComparison.OrdinalIgnoreCase),
            BindingStatus = binding,
            SubscriptionStatus = Str(root, "subscriptionStatus") ?? "UNKNOWN",
            UnlimitedClients = unlimited,
            MaxClients = !unlimited && max.ValueKind == JsonValueKind.Number && max.TryGetInt32(out var m) ? m : null,
            UsedClients = root.TryGetProperty("usedClients", out var used) && used.TryGetInt32(out var n) ? n : 0,
            ExpiresAtUtc = Time(root, "expiresAtUtc"),
        };
    }

    /// <summary>Cloud expects SPKI (PEM or base64url DER); devices may hold the raw 32-byte key.</summary>
    internal static string ToSpkiBase64Url(string devicePublicKey)
    {
        var value = devicePublicKey.Trim();
        if (value.Contains("BEGIN PUBLIC KEY", StringComparison.Ordinal))
            return value;
        byte[] bytes;
        try
        {
            var padded = value.Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            bytes = Convert.FromBase64String(padded);
        }
        catch (FormatException)
        {
            return value; // let the cloud reject it with a precise error
        }
        if (bytes.Length == 32)
            bytes = [.. Ed25519SpkiPrefix, .. bytes];
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    internal static string CloudPlatform(string? platform)
    {
        var p = (platform ?? string.Empty).Trim().ToUpperInvariant();
        if (p.Contains("ANDROID")) return "ANDROID";
        if (p.Contains("IOS") || p.Contains("IPHONE") || p.Contains("IPAD")) return "IOS";
        if (p.Contains("WIN")) return "WINDOWS";
        if (p.Contains("TABLET")) return "TABLET";
        return "OTHER";
    }

    private static string CloudState(NotificationEvent notification)
    {
        var type = (notification.EventType ?? "INFO").ToUpperInvariant();
        if (notification.ClearedAtUtc is not null || type.Contains("CLEAR")) return "CLEARED";
        if (type.Contains("ACK")) return "ACKNOWLEDGED";
        if (type.Contains("ALARM") || type.Contains("ACTIVE") || notification.OccurrenceId is not null) return "ACTIVE";
        return type;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string token, object? body, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, new Uri(new Uri(BaseUrl + "/"), path.TrimStart('/')));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            request.Content = JsonContent.Create(body);
        return await httpClient.SendAsync(request, cancellationToken);
    }

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            return JsonDocument.Parse(string.IsNullOrWhiteSpace(raw) ? "{}" : raw);
        }
        catch (JsonException)
        {
            return JsonDocument.Parse("{\"error\":\"invalid_cloud_response\"}");
        }
    }

    private static RemoteAccessCloudOperationResponse Failure(JsonElement root, HttpResponseMessage response) => new()
    {
        Success = false,
        Code = (Str(root, "error") ?? $"HTTP_{(int)response.StatusCode}").ToUpperInvariant(),
        Message = ErrorMessage(root, response),
    };

    private static string ErrorMessage(JsonElement root, HttpResponseMessage response) =>
        (Str(root, "error") ?? $"http_{(int)response.StatusCode}") switch
        {
            "remote_access_not_entitled" => "Remote Access is not part of this license.",
            "remote_access_not_active" => "The Remote Access subscription is not active.",
            "remote_access_bound_to_another_server" => "Remote Access is bound to another PROGNODE Server.",
            "remote_access_not_bound" => "Bind this PROGNODE Server to Remote Access first.",
            "remote_client_limit_reached" => "All Remote Access seats are in use. Revoke a device or upgrade the add-on.",
            "remote_client_revoked" => "This device was revoked for Remote Access.",
            "device_challenge_invalid" => "The device confirmation expired. Try again.",
            "device_proof_invalid" => "The device could not prove its identity.",
            "invalid_device_public_key" => "The device key is not a valid Ed25519 key.",
            "core_unauthorized" => "This PROGNODE Core is not activated with PROGNODE Cloud.",
            var code => $"Remote Access cloud error: {code}",
        };

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool? Bool(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False ? v.GetBoolean() : null;

    private static DateTimeOffset? Time(JsonElement e, string name) =>
        DateTimeOffset.TryParse(Str(e, name), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var t) ? t : null;

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
