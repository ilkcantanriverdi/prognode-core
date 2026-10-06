namespace Prognode.Core.Tags;

public sealed class TagCapacityExceededException(int usedTags, int maxTags)
    : InvalidOperationException($"Tag limit reached. {usedTags} / {maxTags}")
{
    public int UsedTags { get; } = usedTags;
    public int MaxTags { get; } = maxTags;
}
