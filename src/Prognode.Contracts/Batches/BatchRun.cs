namespace Prognode.Contracts.Batches;

public enum BatchRunState
{
    Running,
    Completed,
    Aborted
}

public sealed record BatchRun(
    Guid Id,
    string BatchNo,
    string? RecipeName,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    BatchRunState State,
    string? Operator,
    string? Note
);
