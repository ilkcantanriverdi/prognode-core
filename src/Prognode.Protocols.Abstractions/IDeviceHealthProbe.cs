using Prognode.Contracts.Devices;

namespace Prognode.Protocols.Abstractions;

public interface IDeviceHealthProbe
{
    bool CanHandle(DeviceDefinition device);

    Task<DeviceHealthResult> CheckAsync(
        DeviceDefinition device,
        CancellationToken cancellationToken);
}
