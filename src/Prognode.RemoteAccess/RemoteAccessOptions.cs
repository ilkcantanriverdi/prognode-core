namespace Prognode.RemoteAccess;

/// <summary>
/// Remote Access relay settings. The relay endpoint is PROGNODE Cloud itself
/// (Prognode:CloudLicense:BaseUrl); only the background sync cadence is configurable here.
/// </summary>
public sealed class RemoteAccessOptions
{
    public int SyncIntervalSeconds { get; set; } = 300;
}
