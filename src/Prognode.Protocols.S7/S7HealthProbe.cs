using System.Diagnostics;
using Prognode.Contracts.Devices;
using Prognode.Protocols.Abstractions;
namespace Prognode.Protocols.S7;
public sealed class S7HealthProbe : IDeviceHealthProbe
{
    public bool CanHandle(DeviceDefinition device)=>device.Protocol.Equals("Siemens S7 TCP",StringComparison.OrdinalIgnoreCase);
    public async Task<DeviceHealthResult> CheckAsync(DeviceDefinition device,CancellationToken ct)
    {
        var sw=Stopwatch.StartNew();
        try { await using var s=new S7Client();var (rack,slot)=S7TagReader.DecodeRackSlot(device.UnitId??1);
            await s.ConnectAsync(device.Host??"",device.Port??102,rack,slot,3000,ct);
            return new(true,"S7 COTP and SetupCommunication OK",Math.Round(sw.Elapsed.TotalMilliseconds,1)); }
        catch(Exception e) when(e is not OperationCanceledException || !ct.IsCancellationRequested)
        { return new(false,e.Message,Math.Round(sw.Elapsed.TotalMilliseconds,1)); }
    }
}
