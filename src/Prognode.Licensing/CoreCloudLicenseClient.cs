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
    public string LicenseDownloadPath { get; set; } = "/api/core/license";
    public string LinkStartPath { get; set; } = "/api/core/link/start";
    public string LinkPollPath { get; set; } = "/api/core/link/poll";
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
    public static bool IsAllowedBaseUrl(string? value)
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
    DateTimeOffset? RevokedAtUtc = null,
    string? ActivationCertificate = null,
    DateTimeOffset? ServerTimeUtc = null,
    int? LicenseRevision = null);

/// <summary>Code shown in Core while the user connects it to a PROGNODE account in the browser.</summary>
public sealed record CoreLinkTicket(string BaseUrl, string DeviceCode, string UserCode, string VerificationUriComplete, DateTimeOffset ExpiresAtUtc, int IntervalSeconds);

/// <summary>Poll answer: pending, slow_down, denied, expired, or approved with the signed license.</summary>
public sealed record CoreLinkPoll(string Status, string? LicenseId, byte[]? SignedLicenseDocument);

/// <summary>A structured refusal from PROGNODE Cloud (e.g. installation_revoked); never retried on another host.</summary>
public sealed class CloudLicenseRejectedException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;
}

public sealed class CoreCloudLicenseClient
{
    private readonly CoreCloudLicenseOptions options;
    private readonly HttpClient httpClient;

    public CoreCloudLicenseClient(CoreCloudLicenseOptions options)
    {
        this.options = options;
        httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
    }

    /// <summary>Contract tests supply a recording handler.</summary>
    internal CoreCloudLicenseClient(CoreCloudLicenseOptions options, HttpClient httpClient)
    {
        this.options = options;
        this.httpClient = httpClient;
    }

    public bool IsConfigured => options.IsConfigured;

    public Task<CoreCloudLicenseStatus> ActivateAsync(
        string licenseId,
        string signedLicenseDocument,
        Guid serverId,
        string serverName,
        string machineFingerprint,
        string coreVersion,
        CancellationToken cancellationToken = default) =>
        SendAsync(options.ActivatePath, null, licenseId, new
        {
            licenseId,
            signedLicenseDocument,
            serverId,
            serverName,
            machineFingerprint,
            coreVersion
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

    /// <summary>
    /// Downloads the current signed license of this activated installation. A renewal, a plan change
    /// or an added Remote Access subscription is issued as a higher licenseRevision. The bytes are
    /// verified and imported locally; PROGNODE Cloud is never trusted blindly.
    /// </summary>
    public async Task<(int Revision, byte[] Document)?> DownloadLicenseAsync(
        string activationToken,
        string licenseId,
        CancellationToken cancellationToken = default)
    {
        foreach (var baseUrl in options.CandidateBaseUrls)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Post,
                    new Uri(new Uri(baseUrl + "/"), options.LicenseDownloadPath.TrimStart('/')));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", activationToken);
                request.Content = JsonContent.Create(new { licenseId });
                using var response = await httpClient.SendAsync(request, cancellationToken);
                if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed)
                    continue;
                if (!response.IsSuccessStatusCode)
                    return null;
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
                var root = document.RootElement;
                if (!string.Equals(GetString(root, "licenseId"), licenseId, StringComparison.OrdinalIgnoreCase))
                    return null;
                var signed = GetString(root, "signedLicenseDocument");
                if (string.IsNullOrWhiteSpace(signed) ||
                    !root.TryGetProperty("licenseRevision", out var revision) || !revision.TryGetInt32(out var value))
                    return null;
                return (value, System.Text.Encoding.UTF8.GetBytes(signed));
            }
            catch (HttpRequestException)
            {
                // Try the next trusted host.
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Try the next trusted host.
            }
        }
        return null;
    }

    /// <summary>Asks PROGNODE Cloud for a connect code (device authorization) on the first reachable host.</summary>
    public async Task<CoreLinkTicket> StartLinkAsync(
        Guid serverId, string serverName, string machineFingerprint, string coreVersion, CancellationToken cancellationToken = default)
    {
        Exception? lastError = null;
        foreach (var baseUrl in options.CandidateBaseUrls)
        {
            try
            {
                using var response = await httpClient.PostAsJsonAsync(
                    new Uri(new Uri(baseUrl + "/"), options.LinkStartPath.TrimStart('/')),
                    new { serverId, serverName, machineFingerprint, coreVersion }, cancellationToken);
                var raw = await response.Content.ReadAsStringAsync(cancellationToken);
                if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed) continue;
                if (!response.IsSuccessStatusCode)
                    throw new CloudLicenseRejectedException(TryGetErrorCode(raw) ?? "link_unavailable", DescribeError(baseUrl, response.StatusCode, raw));
                using var document = JsonDocument.Parse(raw);
                var root = document.RootElement;
                var deviceCode = GetString(root, "deviceCode");
                var userCode = GetString(root, "userCode");
                var uri = GetString(root, "verificationUriComplete");
                // The browser is sent only to the PROGNODE host Core already trusts.
                if (deviceCode is null || userCode is null || uri is null || !CoreCloudLicenseOptions.IsAllowedBaseUrl(uri) ||
                    !Uri.TryCreate(uri, UriKind.Absolute, out var page) || !string.Equals(page.Host, new Uri(baseUrl).Host, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("PROGNODE Cloud returned an invalid connect code.");
                var expiresIn = root.TryGetProperty("expiresIn", out var e) && e.TryGetInt32(out var seconds) ? seconds : 900;
                var interval = root.TryGetProperty("interval", out var i) && i.TryGetInt32(out var every) ? Math.Clamp(every, 2, 30) : 5;
                return new CoreLinkTicket(baseUrl, deviceCode, userCode, uri, DateTimeOffset.UtcNow.AddSeconds(expiresIn), interval);
            }
            catch (HttpRequestException ex) { lastError = ex; }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested) { lastError = ex; }
        }
        throw new InvalidOperationException($"PROGNODE Cloud could not be reached. Check the internet connection of this PC. {lastError?.Message}", lastError);
    }

    public async Task<CoreLinkPoll> PollLinkAsync(CoreLinkTicket ticket, CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.PostAsJsonAsync(
            new Uri(new Uri(ticket.BaseUrl + "/"), options.LinkPollPath.TrimStart('/')),
            new { deviceCode = ticket.DeviceCode }, cancellationToken);
        var raw = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(DescribeError(ticket.BaseUrl, response.StatusCode, raw));
        using var document = JsonDocument.Parse(raw);
        var root = document.RootElement;
        var status = GetString(root, "status") ?? "pending";
        var signed = GetString(root, "signedLicenseDocument");
        return new CoreLinkPoll(status, GetString(root, "licenseId"),
            status == "approved" && !string.IsNullOrWhiteSpace(signed) ? System.Text.Encoding.UTF8.GetBytes(signed) : null);
    }

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
                    // A structured PROGNODE error is an authoritative answer: do not try another host.
                    var code = TryGetErrorCode(raw);
                    if (code is not null && response.StatusCode is not (HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed))
                        throw new CloudLicenseRejectedException(code, DescribeError(baseUrl, response.StatusCode, raw));

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
            catch (CloudLicenseRejectedException)
            {
                throw;
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

        var activationCertificate = GetString(payload, "activationCertificate") ?? GetString(root, "activationCertificate");
        var serverTime = GetTimestamp(payload, "serverTime") ?? GetTimestamp(root, "serverTime");
        int? licenseRevision = null;
        foreach (var container in new[] { payload, root })
        {
            if (container.ValueKind == JsonValueKind.Object && container.TryGetProperty("licenseRevision", out var rev) &&
                rev.ValueKind == JsonValueKind.Number && rev.TryGetInt32(out var parsedRevision))
            {
                licenseRevision = parsedRevision;
                break;
            }
        }

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
            revokedAtUtc,
            activationCertificate,
            serverTime,
            licenseRevision);
    }

    private static string? TryGetErrorCode(string raw)
    {
        try
        {
            using var document = JsonDocument.Parse(raw);
            return document.RootElement.ValueKind == JsonValueKind.Object ? GetString(document.RootElement, "error") : null;
        }
        catch (JsonException)
        {
            return null;
        }
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
