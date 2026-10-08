using Prognode.Alarm;
using Prognode.RemoteAccess;

namespace Prognode.Host.Services;

public sealed class RemoteAckCommandHostedService(
    RemoteAccessService remoteAccess,
    RemoteAckAuditStore audit,
    AlarmService alarms,
    ILogger<RemoteAckCommandHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var commands = await remoteAccess.GetPendingCommandsAsync(stoppingToken);
                foreach (var command in commands)
                    await ProcessAsync(command, stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Remote ACK command polling failed. Local alarm processing is unaffected.");
            }

            await Task.Delay(TimeSpan.FromSeconds(3), stoppingToken);
        }
    }

    private async Task ProcessAsync(RemoteAccessCommand command, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(command.CommandId))
            return;

        var existing = audit.Find(command.CommandId);
        if (existing is not null)
        {
            await remoteAccess.CompleteCommandAsync(
                command.CommandId,
                existing.Success,
                existing.ResultCode,
                cancellationToken);
            return;
        }

        var resultCode = "ACK_FAILED";
        var success = false;

        if (!string.Equals(command.Type, "ACK", StringComparison.OrdinalIgnoreCase))
        {
            resultCode = "UNSUPPORTED_COMMAND";
        }
        else if (command.OccurrenceId is null || command.OccurrenceId == Guid.Empty)
        {
            resultCode = "OCCURRENCE_ID_REQUIRED";
        }
        else if (command.IssuedAtUtc == default || command.IssuedAtUtc < DateTimeOffset.UtcNow.AddMinutes(-10))
        {
            resultCode = "STALE_COMMAND";
        }
        else if (!remoteAccess.IsRemoteAckUserAuthorized(command.UserId))
        {
            resultCode = "USER_NOT_AUTHORIZED";
        }
        else
        {
            var result=await alarms.AcknowledgeOccurrenceAsync(command.OccurrenceId!.Value,cancellationToken,
                $"Remote access user {command.UserId}");
            success=result==OccurrenceAckResult.Acknowledged;
            resultCode=result switch {
                OccurrenceAckResult.Acknowledged => "ACKNOWLEDGED",
                OccurrenceAckResult.StaleOccurrence => "STALE_OCCURRENCE",
                _ => "OCCURRENCE_NOT_FOUND"
            };
        }

        audit.Record(new RemoteAckAuditRecord(
            command.CommandId,
            command.AlarmKey,
            command.UserId,
            command.UserDisplayName,
            command.RemoteClientId,
            success,
            resultCode,
            DateTimeOffset.UtcNow));

        await remoteAccess.CompleteCommandAsync(
            command.CommandId,
            success,
            resultCode,
            cancellationToken);
    }
}
