using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Prognode.Core.Connectivity;

namespace Prognode.Host.Services;

public sealed class ServerDiscoveryHostedService : BackgroundService
{
    private const string DiscoveryRequest = "PROGNODE_DISCOVER_V1";
    private readonly ServerAccessService _serverAccess;
    private readonly ILogger<ServerDiscoveryHostedService> _logger;
    private UdpClient? _udp;

    public ServerDiscoveryHostedService(
        ServerAccessService serverAccess,
        ILogger<ServerDiscoveryHostedService> logger)
    {
        _serverAccess = serverAccess;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var identity = _serverAccess.Identity;

        try
        {
            _udp = new UdpClient(new IPEndPoint(IPAddress.Any, identity.DiscoveryPort));
            _udp.EnableBroadcast = true;
            _logger.LogInformation(
                "PROGNODE LAN discovery listening on UDP {DiscoveryPort} for server {ServerId}",
                identity.DiscoveryPort,
                identity.ServerId);

            while (!stoppingToken.IsCancellationRequested)
            {
                UdpReceiveResult packet;
                try
                {
                    packet = await _udp.ReceiveAsync(stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                var request = Encoding.UTF8.GetString(packet.Buffer).Trim();
                if (!string.Equals(request, DiscoveryRequest, StringComparison.Ordinal))
                    continue;

                var response = JsonSerializer.SerializeToUtf8Bytes(new
                {
                    type = "PROGNODE_SERVER_V1",
                    identity.ServerId,
                    identity.DisplayName,
                    identity.ApiVersion,
                    identity.ApiPort,
                    identity.PairingRequired,
                    identity.SecureApiPort,
                    identity.CertificateSha256
                });

                await _udp.SendAsync(response, packet.RemoteEndPoint, stoppingToken);
            }
        }
        catch (SocketException ex)
        {
            _logger.LogError(ex, "PROGNODE LAN discovery could not bind to UDP port {DiscoveryPort}.", identity.DiscoveryPort);
        }
    }

    public override void Dispose()
    {
        _udp?.Dispose();
        base.Dispose();
    }
}
