using Opc.Ua;
using Prognode.Contracts.Devices;
using Prognode.Contracts.Tags;
using Prognode.Protocols.Abstractions;

namespace Prognode.Protocols.OpcUa;

public sealed class OpcUaTagValidator : ITagDefinitionValidator
{
    public bool CanHandle(DeviceDefinition device) =>
        device.Protocol.Equals("OPC UA", StringComparison.OrdinalIgnoreCase);

    public void Validate(TagDefinition candidate, IReadOnlyList<TagDefinition> existingTags)
    {
        if (candidate.Address.Length > 256)
            throw new ArgumentException("OPC UA NodeId cannot exceed 256 characters.");
        if (candidate.BitIndex is not null)
            throw new ArgumentException("OPC UA tags do not use a bit index.");
        try
        {
            OpcUaNodeAddress.Parse(candidate.Address);
        }
        catch (Exception ex) when (ex is FormatException or ServiceResultException or ArgumentException)
        {
            throw new ArgumentException("OPC UA Tag address must be a NodeId such as ns=2;i=13 or nsu=http://ServerInterface;i=13.", ex);
        }
    }
}
