using System.Diagnostics;
using Prognode.Contracts.Devices;
using Prognode.Protocols.Abstractions;
namespace Prognode.Protocols.S7;

/// <summary>
/// Health check on the shared S7 session: a recent successful poll counts as healthy, otherwise
/// the pool connects (and keeps that session for polling). No extra PLC connection per check.
/// </summary>
public sealed class S7HealthProbe(S7SessionPool pool) : IDeviceHealthProbe
{
    private static readonly TimeSpan PollWindow = TimeSpan.FromSeconds(30);

    public bool CanHandle(DeviceDefinition device)=>device.Protocol.Equals("Siemens S7 TCP",StringComparison.OrdinalIgnoreCase);

    public async Task<DeviceHealthResult> CheckAsync(DeviceDefinition device,CancellationToken ct)
    {
        if (pool.HasRecentSuccess(device.Id, PollWindow))
            return new(true, "S7 session is healthy.", 0);
        var sw=Stopwatch.StartNew();
        try
        {
            await pool.UseAsync(device, (_, _) => Task.FromResult(true), ct);
            return new(true,"S7 COTP and SetupCommunication OK",Math.Round(sw.Elapsed.TotalMilliseconds,1));
        }
        catch(Exception e) when(e is not OperationCanceledException || !ct.IsCancellationRequested)
        { return new(false,e.Message,Math.Round(sw.Elapsed.TotalMilliseconds,1)); }
    }
}
