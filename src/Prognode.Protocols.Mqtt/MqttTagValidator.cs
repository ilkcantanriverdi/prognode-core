using System.Text;
using Prognode.Contracts.Devices;
using Prognode.Contracts.Tags;
using Prognode.Protocols.Abstractions;

namespace Prognode.Protocols.Mqtt;

public sealed class MqttTagValidator : ITagDefinitionValidator
{
    public bool CanHandle(DeviceDefinition device) =>
        device.Protocol.Equals("MQTT", StringComparison.OrdinalIgnoreCase);

    public void Validate(TagDefinition candidate, IReadOnlyList<TagDefinition> existingTags)
    {
        MqttTopicAddress.Parse(candidate.Address);
        if (candidate.BitIndex is not null)
            throw new ArgumentException("MQTT tags do not use a bit index.");
    }
}
