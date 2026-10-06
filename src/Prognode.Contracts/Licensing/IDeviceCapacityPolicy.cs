namespace Prognode.Contracts.Licensing;

public interface IDeviceCapacityPolicy
{
    CapacityLimit DeviceLimit { get; }
    bool AllowsDeviceCount(int resultingCount);
}
