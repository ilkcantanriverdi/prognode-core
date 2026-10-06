using Prognode.Contracts.Devices;
using Prognode.Contracts.Tags;
using Prognode.Protocols.Abstractions;

namespace Prognode.Protocols.Modbus;

public sealed class ModbusTagDefinitionValidator : ITagDefinitionValidator
{
    public bool CanHandle(DeviceDefinition device) =>
        string.Equals(device.Protocol, "Modbus TCP", StringComparison.OrdinalIgnoreCase);

    public void Validate(TagDefinition candidate, IReadOnlyList<TagDefinition> existingTags)
    {
        var candidateAddress = ModbusTagAddressParser.Parse(candidate.Address, candidate.DataType);
        ValidateBitIndex(candidate, candidateAddress);

        foreach (var existing in existingTags)
        {
            if (existing.Id == candidate.Id || existing.DeviceId != candidate.DeviceId)
                continue;

            var existingAddress = ModbusTagAddressParser.Parse(existing.Address, existing.DataType);
            if (candidateAddress.Area != existingAddress.Area)
                continue;

            var candidateEnd = candidateAddress.ZeroBasedOffset + candidateAddress.Width - 1;
            var existingEnd = existingAddress.ZeroBasedOffset + existingAddress.Width - 1;
            var overlaps = candidateAddress.ZeroBasedOffset <= existingEnd &&
                           existingAddress.ZeroBasedOffset <= candidateEnd;

            if (!overlaps)
                continue;

            // Register BOOL tags can share one register when they point at different bits.
            if (!candidateAddress.IsBitArea &&
                candidate.DataType == TagDataType.Bool &&
                existing.DataType == TagDataType.Bool &&
                candidateAddress.ZeroBasedOffset == existingAddress.ZeroBasedOffset &&
                candidate.BitIndex != existing.BitIndex)
            {
                continue;
            }

            throw new ArgumentException(
                $"Address conflict: {Describe(candidate, candidateAddress)} overlaps with tag '{existing.Name}' ({Describe(existing, existingAddress)})." );
        }
    }

    private static void ValidateBitIndex(TagDefinition tag, ParsedModbusTagAddress address)
    {
        if (address.IsBitArea)
        {
            if (tag.DataType != TagDataType.Bool)
                throw new ArgumentException("Coil and Discrete Input addresses require BOOL datatype.");

            if (tag.BitIndex is not null)
                throw new ArgumentException("Coil and Discrete Input BOOL tags do not use BitIndex.");

            return;
        }

        if (tag.DataType == TagDataType.Bool)
        {
            if (tag.BitIndex is null or < 0 or > 15)
                throw new ArgumentException("Register BOOL bit index must be between 0 and 15.");
        }
        else if (tag.BitIndex is not null)
        {
            throw new ArgumentException("BitIndex is only valid for BOOL register tags.");
        }
    }

    private static string Describe(TagDefinition tag, ParsedModbusTagAddress parsed)
    {
        if (tag.DataType == TagDataType.Bool && !parsed.IsBitArea)
            return $"{tag.Address}.{tag.BitIndex ?? 0}";

        if (parsed.Width == 1)
            return tag.Address;

        var numeric = int.Parse(tag.Address);
        return $"{numeric}-{numeric + parsed.Width - 1}";
    }
}
