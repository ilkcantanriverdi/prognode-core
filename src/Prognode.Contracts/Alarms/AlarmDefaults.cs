namespace Prognode.Contracts.Alarms;

/// <summary>
/// Product defaults for alarm definitions. Keep the UI (app.js ALARM_DEFAULT_REQUIRES_ACK,
/// index.html checkbox) in sync with these values.
/// </summary>
public static class AlarmDefaults
{
    /// <summary>
    /// "Require operator ACK" is ON unless the user explicitly turns it off. Applies to new
    /// alarms, API requests that omit the field and CSV imports without the column.
    /// </summary>
    public const bool RequiresAcknowledgement = true;
}
