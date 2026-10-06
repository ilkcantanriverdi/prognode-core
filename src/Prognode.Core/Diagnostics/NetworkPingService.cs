using System.Net.NetworkInformation;

namespace Prognode.Core.Diagnostics;

public sealed record NetworkPingResult(
    bool Success,
    string Host,
    string? Address,
    long? RoundtripTimeMs,
    string Status,
    string Message
);

public sealed class NetworkPingService
{
    public async Task<NetworkPingResult> PingAsync(
        string? host,
        int timeoutMs = 1200,
        CancellationToken cancellationToken = default)
    {
        var cleanHost = (host ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(cleanHost))
            throw new ArgumentException("Host / IP is required.");

        timeoutMs = Math.Clamp(timeoutMs, 250, 10000);

        using var ping = new Ping();

        try
        {
            var pingTask = ping.SendPingAsync(cleanHost, timeoutMs);
            var cancellationTask = Task.Delay(
                Timeout.InfiniteTimeSpan,
                cancellationToken);

            var completed = await Task.WhenAny(
                pingTask,
                cancellationTask);

            if (completed == cancellationTask)
                throw new OperationCanceledException(cancellationToken);

            var reply = await pingTask;

            if (reply.Status == IPStatus.Success)
            {
                return new NetworkPingResult(
                    Success: true,
                    Host: cleanHost,
                    Address: reply.Address?.ToString(),
                    RoundtripTimeMs: reply.RoundtripTime,
                    Status: reply.Status.ToString(),
                    Message: $"ICMP ping successful in {reply.RoundtripTime} ms."
                );
            }

            return new NetworkPingResult(
                Success: false,
                Host: cleanHost,
                Address: reply.Address?.ToString(),
                RoundtripTimeMs: null,
                Status: reply.Status.ToString(),
                Message: $"ICMP ping failed: {reply.Status}. The device may still accept Modbus TCP if ICMP is blocked."
            );
        }
        catch (OperationCanceledException)
            when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new NetworkPingResult(
                Success: false,
                Host: cleanHost,
                Address: null,
                RoundtripTimeMs: null,
                Status: "Error",
                Message: $"ICMP ping failed: {ex.Message}"
            );
        }
    }
}
