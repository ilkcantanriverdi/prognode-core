namespace Prognode.Web.Models;

public sealed record PingHostRequest(
    string? Host,
    int TimeoutMs
);
