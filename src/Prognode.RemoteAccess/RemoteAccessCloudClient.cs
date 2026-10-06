using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Prognode.Contracts.Notifications;

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
    public string? ServerAccessToken { get; set; }
}

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

public sealed class RemoteAccessCloudClient(HttpClient httpClient, RemoteAccessOptions options)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public bool IsConfigured => options.IsConfigured;

    internal async Task<RemoteAccessCloudStatusResponse> GetStatusAsync(
        string serverAccessToken,
        Guid serverId,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        using var request = CreateAuthorized(HttpMethod.Get,
            $"{options.StatusPath}?serverId={serverId:D}", serverAccessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return await ReadStatusAsync(response, cancellationToken);
    }

    internal Task<RemoteAccessCloudOperationResponse> BindServerAsync(
        string licenseId,
        string signedLicenseDocument,
        Guid serverId,
        string serverName,
        CancellationToken cancellationToken = default) =>
        PostAsync(options.BindServerPath, new
        {
            licenseId,
            signedLicenseDocument,
            serverId,
            serverName
        }, null, cancellationToken);

    internal Task<RemoteAccessCloudOperationResponse> RegisterClientAsync(
        string serverAccessToken,
        Guid serverId,
        Guid localClientId,
        string deviceName,
        string platform,
        string? devicePublicKey,
        string? userId,
        string? userDisplayName,
        CancellationToken cancellationToken = default) =>
        PostAsync(options.RegisterClientPath, new
        {
            serverId,
            localClientId,
            deviceName,
            platform,
            devicePublicKey,
            userId,
            userDisplayName
        }, serverAccessToken, cancellationToken);

    internal Task<RemoteAccessCloudOperationResponse> RegisterPushTokenAsync(
        string serverAccessToken,Guid serverId,Guid localClientId,Guid remoteClientId,
        string platform,string pushToken,CancellationToken cancellationToken=default) =>
        PostAsync(options.PushTokenPath,new { serverId,localClientId,remoteClientId,platform,pushToken },
            serverAccessToken,cancellationToken);

    internal Task<RemoteAccessCloudOperationResponse> RevokeClientAsync(
        string serverAccessToken,
        Guid serverId,
        Guid remoteClientId,
        CancellationToken cancellationToken = default) =>
        PostAsync(options.RevokeClientPath, new
        {
            serverId,
            remoteClientId
        }, serverAccessToken, cancellationToken);

    internal async Task<bool> PublishNotificationAsync(
        string serverAccessToken,
        Guid serverId,
        NotificationEvent notification,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        using var request = CreateAuthorized(HttpMethod.Post, options.PublishNotificationPath, serverAccessToken);
        request.Content = JsonContent.Create(new
        {
            serverId,
            eventId = notification.Id,
            notification.Severity,
            notification.Title,
            notification.Message,
            notification.Timestamp,
            notification.AlarmKey,
            notification.RequiresAcknowledgement,
            notification.RepeatSequence,
            notification.OccurrenceId,
            notification.EventType,notification.SourceName,notification.ActiveAtUtc,notification.ClearedAtUtc
        });
        using var response = await httpClient.SendAsync(request, cancellationToken);
        // A 200 HTML Vercel /_not-found page is NOT a relay ACK.
        if(!response.IsSuccessStatusCode ||
           !string.Equals(response.Content.Headers.ContentType?.MediaType,
              "application/json",StringComparison.OrdinalIgnoreCase)) return false;
        var body=await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            using var doc=JsonDocument.Parse(body);
            var root=doc.RootElement;
            return root.TryGetProperty("accepted",out var accepted) && accepted.ValueKind==JsonValueKind.True &&
                root.TryGetProperty("eventId",out var eventId) && eventId.TryGetInt64(out var echoed) &&
                echoed==notification.Id;
        }
        catch(JsonException) { return false; }
    }

    internal async Task<IReadOnlyList<RemoteAccessCommand>> GetCommandsAsync(
        string serverAccessToken,
        Guid serverId,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        using var request = CreateAuthorized(HttpMethod.Get,
            $"{options.CommandsPath}?serverId={serverId:D}", serverAccessToken);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return [];

        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        try
        {
            return JsonSerializer.Deserialize<List<RemoteAccessCommand>>(body, Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    internal async Task<bool> CompleteCommandAsync(
        string serverAccessToken,
        Guid serverId,
        string commandId,
        bool success,
        string resultCode,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        using var request = CreateAuthorized(HttpMethod.Post, options.CompleteCommandPath, serverAccessToken);
        request.Content = JsonContent.Create(new { serverId, commandId, success, resultCode });
        using var response = await httpClient.SendAsync(request, cancellationToken);
        return response.IsSuccessStatusCode;
    }

    private async Task<RemoteAccessCloudOperationResponse> PostAsync(
        string path,
        object payload,
        string? serverAccessToken,
        CancellationToken cancellationToken)
    {
        EnsureConfigured();
        using var request = CreateAuthorized(HttpMethod.Post, path, serverAccessToken);
        request.Content = JsonContent.Create(payload);
        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        RemoteAccessCloudOperationResponse? parsed = null;
        if (!string.IsNullOrWhiteSpace(body))
        {
            try { parsed = JsonSerializer.Deserialize<RemoteAccessCloudOperationResponse>(body, Json); }
            catch (JsonException) { }
        }

        if(parsed is null && response.IsSuccessStatusCode)
            return new RemoteAccessCloudOperationResponse {
                Success=false,Code="INVALID_CLOUD_CONTRACT",
                Message="Cloud returned no valid JSON operation response (possibly an HTML not-found page)."
            };
        if (parsed is not null)
        {
            if (!response.IsSuccessStatusCode)
                parsed.Success = false;
            return parsed;
        }

        return new RemoteAccessCloudOperationResponse
        {
            Success = response.IsSuccessStatusCode,
            Code = response.IsSuccessStatusCode ? "OK" : $"HTTP_{(int)response.StatusCode}",
            Message = response.IsSuccessStatusCode ? "Remote Access operation completed." : "Remote Access cloud rejected the operation."
        };
    }

    private static async Task<RemoteAccessCloudStatusResponse> ReadStatusAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"Remote Access status failed with HTTP {(int)response.StatusCode}.");

        try
        {
            return JsonSerializer.Deserialize<RemoteAccessCloudStatusResponse>(body, Json)
                ?? throw new InvalidOperationException("Remote Access cloud returned an empty status.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("Remote Access cloud returned an unsupported status payload.", ex);
        }
    }

    private HttpRequestMessage CreateAuthorized(HttpMethod method, string path, string? serverAccessToken)
    {
        var request = new HttpRequestMessage(method, path);
        if (!string.IsNullOrWhiteSpace(serverAccessToken))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", serverAccessToken);
        return request;
    }

    private void EnsureConfigured()
    {
        if (!options.IsConfigured)
            throw new InvalidOperationException("PROGNODE Remote Access cloud endpoint is not configured.");
    }
}
