namespace Prognode.Web.Models;

public sealed record StartBatchRequest(
    string? BatchNo,
    string? RecipeName = null,
    string? Operator = null,
    string? Note = null
);

public sealed record EndBatchRequest(string? Note = null);
