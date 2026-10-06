using System.Net.Http.Json;

namespace Prognode.Licensing;

/// <summary>
/// Best-effort cloud activation boundary. It is intentionally not used by signature verification,
/// offline credential verification, local session creation, PLC polling, alarm or historian runtime.
/// A network failure therefore cannot deny a valid local login.
/// </summary>
public sealed class CloudActivationClient(HttpClient httpClient, string? activationEndpoint = null)
{
    public async Task<bool> TryActivateAsync(object activationPayload, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(activationEndpoint))
            return false;

        try
        {
            using var response = await httpClient.PostAsJsonAsync(activationEndpoint, activationPayload, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            return false;
        }
    }
}
