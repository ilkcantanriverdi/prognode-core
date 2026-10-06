namespace Prognode.Web.Models;

public sealed record AgentHeartbeatRequest(
    string? MachineName,
    string? Version,
    string? NotificationMode
);
