using Prognode.Contracts.Alarms;

namespace Prognode.Alarm;

public interface IAlarmDefinitionRepository
{
    Task<IReadOnlyList<AlarmDefinition>> GetAllAsync(
        CancellationToken cancellationToken = default);

    Task<AlarmDefinition?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task AddAsync(
        AlarmDefinition definition,
        CancellationToken cancellationToken = default);

    Task UpdateAsync(
        AlarmDefinition definition,
        CancellationToken cancellationToken = default);

    Task DeleteAsync(
        Guid id,
        CancellationToken cancellationToken = default);

    Task<int> CountAsync(
        CancellationToken cancellationToken = default);
}
