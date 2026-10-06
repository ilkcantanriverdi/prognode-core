using System.Collections.Concurrent;
using Prognode.Contracts.Tags;

namespace Prognode.Core.Tags;

public sealed class CurrentTagValueStore
{
    private readonly ConcurrentDictionary<Guid, TagValueSnapshot> _values = new();

    public void Set(TagValueSnapshot value) =>
        _values[value.TagId] = value;

    public void SetMany(IEnumerable<TagValueSnapshot> values)
    {
        foreach (var value in values)
            Set(value);
    }

    public TagValueSnapshot? Get(Guid tagId) =>
        _values.TryGetValue(tagId, out var value)
            ? value
            : null;

    public IReadOnlyList<TagValueSnapshot> GetAll() =>
        _values.Values
            .OrderBy(x => x.TagId)
            .ToArray();

    public void Remove(Guid tagId) =>
        _values.TryRemove(tagId, out _);
}
