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
        var topic = candidate.Address;
        if (string.IsNullOrWhiteSpace(topic) ||
            Encoding.UTF8.GetByteCount(topic) > 512 ||
            topic.IndexOfAny(['+', '#', '\0']) >= 0 ||
            topic.Any(char.IsControl))
            throw new ArgumentException("MQTT Tag address must be an exact topic (1–512 UTF-8 bytes), without wildcards or control characters.");
        if (candidate.BitIndex is not null)
            throw new ArgumentException("MQTT tags do not use a bit index.");
    }
}
