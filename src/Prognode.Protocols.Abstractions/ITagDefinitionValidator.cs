using Prognode.Contracts.Devices;
using Prognode.Contracts.Tags;

namespace Prognode.Protocols.Abstractions;

public interface ITagDefinitionValidator
{
    bool CanHandle(DeviceDefinition device);

    void Validate(
        TagDefinition candidate,
        IReadOnlyList<TagDefinition> existingTags);
}
