using Prognode.Contracts.Devices;
using Prognode.Contracts.Tags;

namespace Prognode.Protocols.Abstractions;

public interface ITagReader
{
    bool CanHandle(DeviceDefinition device);

    Task<IReadOnlyList<TagValueSnapshot>> ReadAsync(
        DeviceDefinition device,
        IReadOnlyList<TagDefinition> tags,
        CancellationToken cancellationToken);
}
