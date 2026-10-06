using Prognode.Contracts.Tags;
using Prognode.Contracts.Licensing;
using Prognode.Core.Devices;
using Prognode.Protocols.Abstractions;

namespace Prognode.Core.Tags;

public sealed class TagService(
    ITagRepository repository,
    IDeviceRepository devices,
    CurrentTagValueStore currentValues,
    IEnumerable<ITagDefinitionValidator> validators,
    ITagCapacityPolicy tagCapacityPolicy)
{
    private readonly SemaphoreSlim _createGate = new(1, 1);

    public void ForgetCurrentValue(Guid id) => currentValues.Remove(id);

    public Task<IReadOnlyList<TagDefinition>> GetAllAsync(
        CancellationToken cancellationToken = default) =>
        repository.GetAllAsync(cancellationToken);

    public Task<int> CountAsync(
        CancellationToken cancellationToken = default) =>
        repository.CountAsync(cancellationToken);

    public async Task<TagDefinition> CreateAsync(
        Guid deviceId,
        string? name,
        string? address,
        TagDataType dataType,
        int? bitIndex,
        ModbusByteOrder byteOrder,
        string? unit,
        double offset,
        int decimalPlaces,
        CancellationToken cancellationToken = default)
    {
        await _createGate.WaitAsync(cancellationToken);
        try
        {
            await Devices.EngineeringNameGate.Write.WaitAsync(cancellationToken);
            try
            {
            var currentCount = await repository.CountAsync(cancellationToken);
            if (!tagCapacityPolicy.AllowsUniqueTagCount(currentCount + 1))
            {
                var limit = tagCapacityPolicy.UniqueTagLimit;
                var max = limit.Value ?? currentCount;
                throw new TagCapacityExceededException(currentCount, max);
            }

            var now = DateTimeOffset.UtcNow;
            var tag = await BuildValidatedAsync(
                id: Guid.NewGuid(),
                deviceId,
                name,
                address,
                dataType,
                bitIndex,
                byteOrder,
                unit,
                offset,
                decimalPlaces,
                createdAt: now,
                cancellationToken);

            await repository.AddAsync(tag, cancellationToken);
            return tag;
            }
            finally { Devices.EngineeringNameGate.Write.Release(); }
        }
        finally
        {
            _createGate.Release();
        }
    }

    public async Task<TagDefinition> UpdateAsync(
        Guid id,
        Guid deviceId,
        string? name,
        string? address,
        TagDataType dataType,
        int? bitIndex,
        ModbusByteOrder byteOrder,
        string? unit,
        double offset,
        int decimalPlaces,
        CancellationToken cancellationToken = default)
    {
        await Devices.EngineeringNameGate.Write.WaitAsync(cancellationToken);
        try
        {
        var existing = await repository.GetByIdAsync(id, cancellationToken);

        if (existing is null)
            throw new KeyNotFoundException("Tag was not found.");

        var tag = await BuildValidatedAsync(
            id,
            deviceId,
            name,
            address,
            dataType,
            bitIndex,
            byteOrder,
            unit,
            offset,
            decimalPlaces,
            existing.CreatedAt,
            cancellationToken);

        await repository.UpdateAsync(tag, cancellationToken);
        currentValues.Remove(id);

        return tag;
        }
        finally { Devices.EngineeringNameGate.Write.Release(); }
    }

    public async Task<bool> DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var existing = await repository.GetByIdAsync(id, cancellationToken);

        if (existing is null)
            return false;

        await repository.DeleteAsync(id, cancellationToken);
        currentValues.Remove(id);
        return true;
    }

    private async Task<TagDefinition> BuildValidatedAsync(
        Guid id,
        Guid deviceId,
        string? name,
        string? address,
        TagDataType dataType,
        int? bitIndex,
        ModbusByteOrder byteOrder,
        string? unit,
        double offset,
        int decimalPlaces,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        var device = await devices.GetByIdAsync(deviceId, cancellationToken);

        if (device is null)
            throw new ArgumentException("Device was not found.");

        var cleanName = (name ?? string.Empty).Trim();
        var cleanAddress = (address ?? string.Empty).Trim();
        var cleanUnit = (unit ?? string.Empty).Trim();

        if (cleanName.Length < 1)
            throw new ArgumentException("Tag name is required.");

        if (cleanName.Length > 100)
            throw new ArgumentException("Tag name cannot exceed 100 characters.");

        if (string.IsNullOrWhiteSpace(cleanAddress))
            throw new ArgumentException("Tag address is required.");

        // Protocol validators decide whether BOOL is a register bit (BitIndex required)
        // or a native bit area such as Modbus Coil/Discrete Input (BitIndex must be null).
        if (dataType == TagDataType.Bool && bitIndex is < 0 or > 15)
            throw new ArgumentException("BOOL bit index must be between 0 and 15 when used.");

        if (dataType != TagDataType.Bool)
            bitIndex = null;

        if (double.IsNaN(offset) || double.IsInfinity(offset))
            throw new ArgumentException("Offset must be a valid number.");

        decimalPlaces = Math.Clamp(decimalPlaces, 0, 6);

        var candidate = new TagDefinition(
            Id: id,
            DeviceId: deviceId,
            Name: cleanName,
            Address: cleanAddress,
            DataType: dataType,
            BitIndex: bitIndex,
            ByteOrder: byteOrder,
            Unit: dataType is TagDataType.Bool or TagDataType.Word ? string.Empty : cleanUnit,
            Scale: 1.0,
            Offset: dataType is TagDataType.Bool or TagDataType.Word ? 0.0 : offset,
            DecimalPlaces: dataType is TagDataType.Bool or TagDataType.Word ? 0 : decimalPlaces,
            Enabled: true,
            CreatedAt: createdAt,
            UpdatedAt: DateTimeOffset.UtcNow
        );

        var validator = validators.FirstOrDefault(x => x.CanHandle(device));

        if (validator is null)
            throw new ArgumentException($"No tag validator is available for protocol '{device.Protocol}'.");

        var existingTags = await repository.GetAllAsync(cancellationToken);
        if(existingTags.Any(x=>x.Id!=id&&string.Equals(x.Name,cleanName,StringComparison.OrdinalIgnoreCase)) ||
           (await devices.GetAllAsync(cancellationToken)).Any(x=>
               string.Equals(x.Name,cleanName,StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException($"Name '{cleanName}' is already used by a Device or Tag.");
        validator.Validate(candidate, existingTags);

        return candidate;
    }
}
