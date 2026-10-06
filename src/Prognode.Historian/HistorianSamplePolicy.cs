using Prognode.Contracts.Historian;
using Prognode.Contracts.Tags;

namespace Prognode.Historian;

public static class HistorianSamplePolicy
{
    public static HistorianSample? Create(
        TagValueSnapshot? snapshot, DateTimeOffset sampledAt, Guid? batchId)
    {
        if (snapshot is not { Quality: TagQuality.Good, Value: not null })
            return null;

        return new HistorianSample(snapshot.TagId, sampledAt.ToUnixTimeMilliseconds(),
            snapshot.Value, TagQuality.Good.ToString(), batchId);
    }
}
