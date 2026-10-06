namespace Prognode.RemoteAccess;

public sealed class RemoteAccessOptions
{
    public string BaseUrl { get; set; } = string.Empty;
    public string StatusPath { get; set; } = "/api/v1/remote-access/status";
    public string BindServerPath { get; set; } = "/api/v1/remote-access/bind-server";
    public string RegisterClientPath { get; set; } = "/api/v1/remote-access/register-client";
    public string PushTokenPath { get; set; } = "/api/v1/remote-access/push-token";
    public string RevokeClientPath { get; set; } = "/api/v1/remote-access/revoke-client";
    public string PublishNotificationPath { get; set; } = "/api/v1/remote-access/notifications";
    public string CommandsPath { get; set; } = "/api/v1/remote-access/commands";
    public string CompleteCommandPath { get; set; } = "/api/v1/remote-access/commands/complete";
    public int SyncIntervalSeconds { get; set; } = 300;

    public bool IsConfigured =>
        Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttps || uri.IsLoopback);
}
