using Prognode.Contracts.Batches;

namespace Prognode.Core.Batches;

public sealed class BatchService(IBatchRepository repository)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public Task<BatchRun?> GetCurrentAsync(CancellationToken cancellationToken = default) =>
        repository.GetCurrentAsync(cancellationToken);

    public Task<BatchRun?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        repository.GetByIdAsync(id, cancellationToken);

    public Task<IReadOnlyList<BatchRun>> GetRecentAsync(
        int limit,
        CancellationToken cancellationToken = default) =>
        repository.GetRecentAsync(Math.Clamp(limit, 1, 200), cancellationToken);

    public async Task<BatchRun> StartAsync(
        string? batchNo,
        string? recipeName,
        string? operatorName,
        string? note,
        CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var current = await repository.GetCurrentAsync(cancellationToken);
            if (current is not null)
                throw new InvalidOperationException($"Batch '{current.BatchNo}' is already running.");

            var cleanBatchNo = CleanRequired(batchNo, 100, "Batch / Lot number");
            var cleanRecipe = CleanOptional(recipeName, 120, "Recipe name");
            var cleanOperator = CleanOptional(operatorName, 120, "Operator");
            var cleanNote = CleanOptional(note, 1000, "Note");
            var now = DateTimeOffset.UtcNow;

            var batch = new BatchRun(
                Id: Guid.NewGuid(),
                BatchNo: cleanBatchNo,
                RecipeName: cleanRecipe,
                StartedAt: now,
                EndedAt: null,
                State: BatchRunState.Running,
                Operator: cleanOperator,
                Note: cleanNote);

            await repository.AddAsync(batch, cancellationToken);
            return batch;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task<BatchRun> CompleteAsync(
        Guid id,
        string? note,
        CancellationToken cancellationToken = default) =>
        EndAsync(id, BatchRunState.Completed, note, cancellationToken);

    public Task<BatchRun> AbortAsync(
        Guid id,
        string? note,
        CancellationToken cancellationToken = default) =>
        EndAsync(id, BatchRunState.Aborted, note, cancellationToken);

    private async Task<BatchRun> EndAsync(
        Guid id,
        BatchRunState state,
        string? note,
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var existing = await repository.GetByIdAsync(id, cancellationToken)
                ?? throw new KeyNotFoundException("Batch was not found.");

            if (existing.State != BatchRunState.Running || existing.EndedAt is not null)
                throw new InvalidOperationException("Only a running Batch can be completed or aborted.");

            var cleanNote = note is null
                ? existing.Note
                : CleanOptional(note, 1000, "Note");

            var updated = existing with
            {
                State = state,
                EndedAt = DateTimeOffset.UtcNow,
                Note = cleanNote
            };

            await repository.UpdateAsync(updated, cancellationToken);
            return updated;
        }
        finally
        {
            _gate.Release();
        }
    }

    private static string CleanRequired(string? value, int maxLength, string field)
    {
        var clean = (value ?? string.Empty).Trim();
        if (clean.Length == 0)
            throw new ArgumentException($"{field} is required.");
        if (clean.Length > maxLength)
            throw new ArgumentException($"{field} cannot exceed {maxLength} characters.");
        return clean;
    }

    private static string? CleanOptional(string? value, int maxLength, string field)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var clean = value.Trim();
        if (clean.Length > maxLength)
            throw new ArgumentException($"{field} cannot exceed {maxLength} characters.");
        return clean;
    }
}
