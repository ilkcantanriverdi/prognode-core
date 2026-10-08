using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;

namespace Prognode.Licensing;

public sealed class CoreCloudLicenseOptions
{
    /// <summary>Production license API. Release builds always use it, whatever the configuration says.</summary>
    public const string ProductionBaseUrl = "https://account.prognode.io";

    public string BaseUrl { get; set; } = string.Empty;
    public string[] FallbackBaseUrls { get; set; } = Array.Empty<string>();
    public string ActivatePath { get; set; } = "/api/core/activate";
    public string HeartbeatPath { get; set; } = "/api/core/heartbeat";
    public int HeartbeatIntervalSeconds { get; set; } = 60;

    public IReadOnlyList<string> CandidateBaseUrls =>
        new[] { BaseUrl }
            .Concat(FallbackBaseUrls ?? Array.Empty<string>())
#if !DEBUG
            .Append(ProductionBaseUrl)
#endif
            .Where(IsAllowedBaseUrl)
            .Select(x => x!.Trim().TrimEnd('/'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public bool IsConfigured => CandidateBaseUrls.Count > 0;

    /// <summary>
    /// Release builds talk only to HTTPS PROGNODE hosts, so editing appsettings cannot point Core at a
    /// look-alike server that answers ACTIVE and clears a revocation. Debug builds also allow loopback.
    /// </summary>
    internal static bool IsAllowedBaseUrl(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
            return false;
#if DEBUG
        if (uri.IsLoopback)
            return true;
#endif
        return uri.Scheme == Uri.UriSchemeHttps &&
            (string.Equals(uri.Host, "prognode.io", StringComparison.OrdinalIgnoreCase) ||
             uri.Host.EndsWith(".prognode.io", StringComparison.OrdinalIgnoreCase));
    }
}

public sealed record CoreCloudLicenseStatus(
    bool Configured,
    string LicenseStatus,
    DateTimeOffset? ExpiresAtUtc,
    DateTimeOffset? GraceUntilUtc,
    string? MaxTags,
    string? ActivationToken,
    DateTimeOffset? LastSyncedAtUtc,
    string? LastError,
    string? LicenseId = null,
    bool Revoked = false,
    DateTimeOffset? RevokedAtUtc = null);

public sealed class CoreCloudLicenseClient
{
    private readonly CoreCloudLicenseOptions options;
    private readonly HttpClient httpClient;

    public CoreCloudLicenseClient(CoreCloudLicenseOptions options)
    {
        this.options = options;
        httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
    }

    public bool IsConfigured => options.IsConfigured;

    public Task<CoreCloudLicenseStatus> ActivateAsync(
        string licenseId,
        string signedLicenseDocument,
        Guid serverId,
        string serverName,
        CancellationToken cancellationToken = default) =>
        SendAsync(options.ActivatePath, null, licenseId, new
        {
            licenseId,
            signedLicenseDocument,
            serverId,
            serverName
        }, cancellationToken);

    public Task<CoreCloudLicenseStatus> HeartbeatAsync(
        string activationToken,
        string licenseId,
        Guid serverId,
        CancellationToken cancellationToken = default) =>
        SendAsync(options.HeartbeatPath, activationToken, licenseId, new
        {
            licenseId,
            serverId
        }, cancellationToken);

    private async Task<CoreCloudLicenseStatus> SendAsync(
        string path,
        string? token,
        string expectedLicenseId,
        object payload,
        CancellationToken cancellationToken)
    {
        var candidates = options.CandidateBaseUrls;
        if (candidates.Count == 0)
            throw new InvalidOperationException("PROGNODE Cloud license endpoint is not configured.");

        Exception? lastError = null;

        foreach (var baseUrl in candidates)
        {
            try
            {
                var uri = new Uri(new Uri(baseUrl + "/"), path.TrimStart('/'));
                using var request = new HttpRequestMessage(HttpMethod.Post, uri);
                if (!string.IsNullOrWhiteSpace(token))
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
                request.Content = JsonContent.Create(payload);

                using var response = await httpClient.SendAsync(request, cancellationToken);
                var raw = await response.Content.ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    var error = new InvalidOperationException(DescribeError(baseUrl, response.StatusCode, raw));

                    // A missing route means this PROGNODE host is not the Cloud API owner.
                    // Try the next trusted production host. Validation failures should stop here.
                    if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                    {
                        lastError = error;
                        continue;
                    }

                    throw error;
                }

                return ParseStatus(raw, token, expectedLicenseId);
            }
            catch (HttpRequestException ex)
            {
                lastError = ex;
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                lastError = ex;
            }
        }

        throw new InvalidOperationException(
            $"PROGNODE Cloud license API could not be reached on the configured production hosts. {lastError?.Message}",
            lastError);
    }

    private static CoreCloudLicenseStatus ParseStatus(string raw, string? existingToken, string expectedLicenseId)
    {
        using var document = JsonDocument.Parse(raw);
        var root = document.RootElement;

        // Accept the documented flat response and common { data: {...} } / { license: {...} } wrappers.
        var payload = root;
        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object)
            payload = data;
        else if (root.TryGetProperty("license", out var license) && license.ValueKind == JsonValueKind.Object)
            payload = license;

        var statusContainer = payload;
        if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("installation", out var installation) && installation.ValueKind == JsonValueKind.Object)
            statusContainer = installation;

        var licenseContainer = payload;
        if (payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("license", out var nestedLicense) && nestedLicense.ValueKind == JsonValueKind.Object)
            licenseContainer = nestedLicense;

        var status = GetString(statusContainer, "licenseStatus") ?? GetString(statusContainer, "status")
            ?? GetString(licenseContainer, "licenseStatus") ?? GetString(licenseContainer, "status")
            ?? GetString(payload, "licenseStatus") ?? GetString(payload, "status")
            ?? GetString(root, "licenseStatus") ?? GetString(root, "status") ?? "UNKNOWN";

        var revoked = GetBool(statusContainer, "revoked")
            ?? GetBool(licenseContainer, "revoked")
            ?? GetBool(payload, "revoked")
            ?? GetBool(root, "revoked")
            ?? string.Equals(status, "REVOKED", StringComparison.OrdinalIgnoreCase);

        if (revoked) status = "REVOKED";
        status = status.Trim().ToUpperInvariant();

        var expires = GetTimestamp(statusContainer, "expiresAtUtc") ?? GetTimestamp(statusContainer, "expiresAt")
            ?? GetTimestamp(licenseContainer, "expiresAtUtc") ?? GetTimestamp(licenseContainer, "expiresAt")
            ?? GetTimestamp(payload, "expiresAtUtc") ?? GetTimestamp(payload, "expiresAt")
            ?? GetTimestamp(root, "expiresAtUtc") ?? GetTimestamp(root, "expiresAt");
        var grace = GetTimestamp(statusContainer, "graceUntilUtc") ?? GetTimestamp(statusContainer, "graceUntil")
            ?? GetTimestamp(licenseContainer, "graceUntilUtc") ?? GetTimestamp(licenseContainer, "graceUntil")
            ?? GetTimestamp(payload, "graceUntilUtc") ?? GetTimestamp(payload, "graceUntil")
            ?? GetTimestamp(root, "graceUntilUtc") ?? GetTimestamp(root, "graceUntil");
        var maxTags = GetCapacity(statusContainer, "maxTags") ?? GetCapacity(licenseContainer, "maxTags") ?? GetCapacity(payload, "maxTags") ?? GetCapacity(root, "maxTags");
        var activationToken = GetString(statusContainer, "activationToken") ?? GetString(licenseContainer, "activationToken") ?? GetString(payload, "activationToken")
            ?? GetString(root, "activationToken") ?? existingToken;
        var responseLicenseId = GetString(statusContainer, "licenseId") ?? GetString(licenseContainer, "licenseId") ?? GetString(payload, "licenseId")
            ?? GetString(root, "licenseId") ?? expectedLicenseId;
        var revokedAtUtc = GetTimestamp(statusContainer, "revokedAtUtc") ?? GetTimestamp(statusContainer, "revokedAt")
            ?? GetTimestamp(licenseContainer, "revokedAtUtc") ?? GetTimestamp(licenseContainer, "revokedAt")
            ?? GetTimestamp(payload, "revokedAtUtc") ?? GetTimestamp(payload, "revokedAt")
            ?? GetTimestamp(root, "revokedAtUtc") ?? GetTimestamp(root, "revokedAt");

        if (!string.Equals(responseLicenseId, expectedLicenseId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("PROGNODE Cloud returned a licenseId that does not match the local signed license.");

        return new CoreCloudLicenseStatus(
            true,
            status,
            expires,
            grace,
            maxTags,
            activationToken,
            DateTimeOffset.UtcNow,
            null,
            responseLicenseId,
            revoked,
            revokedAtUtc);
    }

    /// <summary>Turns a structured Cloud error into a message the Core UI can show as-is.</summary>
    private static string DescribeError(string baseUrl, HttpStatusCode statusCode, string raw)
    {
        try
        {
            using var document = JsonDocument.Parse(raw);
            var root = document.RootElement;
            var code = GetString(root, "error");
            var details = GetString(root, "details");
            if (code == "installation_limit_reached")
            {
                var machine = GetString(root, "activeMachineName");
                return $"installation_limit_reached: This license is already active on another PROGNODE Core{(string.IsNullOrWhiteSpace(machine) ? "" : $" ({machine})")}. Release it in PROGNODE Account, then activate this PC.";
            }
            if (!string.IsNullOrWhiteSpace(code))
                return string.IsNullOrWhiteSpace(details) ? code : $"{code}: {details}";
        }
        catch (JsonException)
        {
            // Not a PROGNODE JSON error (proxy page, HTML); fall through to the status code.
        }
        return $"PROGNODE Cloud license API {baseUrl} returned HTTP {(int)statusCode}.";
    }

    private static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool? GetBool(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static DateTimeOffset? GetTimestamp(JsonElement root, string name)
    {
        var raw = GetString(root, name);
        return DateTimeOffset.TryParse(raw, out var parsed) ? parsed : null;
    }

    private static string? GetCapacity(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.String) return value.GetString();
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var parsed)) return parsed.ToString();
        return null;
    }
}
