namespace Prognode.Contracts.Licensing;

/// <summary>
/// Commercial license boundary for the central configured process Tag Registry.
/// A Tag consumes capacity once when it exists in the registry; reusing that Tag
/// in Alarm, Historian or Trends never consumes another licensed Tag.
/// </summary>
public interface ITagCapacityPolicy
{
    CapacityLimit UniqueTagLimit { get; }
    bool AllowsUniqueTagCount(int resultingCount);
    bool IsOverCapacity(int currentCount);
}
