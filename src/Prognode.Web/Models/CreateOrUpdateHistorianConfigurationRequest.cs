namespace Prognode.Web.Models;

public sealed record CreateOrUpdateHistorianConfigurationRequest(
    Guid TagId,
    int SampleIntervalSeconds,
    int RetentionDays
);
