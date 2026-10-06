namespace Prognode.Core.Devices;

public sealed class DeviceCapacityExceededException(int usedDevices, int? maxDevices)
    : Exception($"Device capacity reached ({usedDevices}/{maxDevices?.ToString() ?? "unlimited"}).")
{
    public int UsedDevices { get; } = usedDevices;
    public int? MaxDevices { get; } = maxDevices;
}
