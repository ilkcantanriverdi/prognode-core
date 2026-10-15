using System.Globalization;
using System.Text;
using Prognode.Contracts.Tags;

namespace Prognode.Protocols.Mqtt;

public static class MqttPayloadMapper
{
    /// <summary>Largest message kept per topic; JSON messages from gateways can be several KB.</summary>
    public const int MaxPayloadBytes = 65536;

    public static TagValueSnapshot Map(Guid deviceId, TagDefinition tag, byte[] payload,
        bool retained, DateTimeOffset receivedAt, DateTimeOffset now, int pollIntervalMs)
    {
        if (payload.Length is 0 or > MaxPayloadBytes)
            return Error(deviceId, tag, now, "MQTT payload must contain 1–65536 UTF-8 bytes.");

        string valueText;
        try { valueText = new UTF8Encoding(false, true).GetString(payload).Trim(); }
        catch (DecoderFallbackException) { return Error(deviceId, tag, now, "MQTT payload is not valid UTF-8."); }
        MqttTopicAddress address;
        try { address = MqttTopicAddress.Parse(tag.Address); }
        catch (ArgumentException ex) { return Error(deviceId, tag, now, ex.Message); }
        if (!address.TryExtract(valueText, out valueText, out var jsonError))
            return Error(deviceId, tag, now, jsonError!);
        valueText = valueText.Trim();

        double raw;
        if (tag.DataType == TagDataType.Bool)
        {
            if (valueText.Equals("true", StringComparison.OrdinalIgnoreCase) || valueText == "1") raw = 1;
            else if (valueText.Equals("false", StringComparison.OrdinalIgnoreCase) || valueText == "0") raw = 0;
            else return Error(deviceId, tag, now, "MQTT BOOL payload must be true, false, 1, or 0.");
        }
        else if (!double.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out raw) ||
                 !double.IsFinite(raw) || !TagDataRange.Fits(raw, tag.DataType))
            return Error(deviceId, tag, now, "MQTT payload is not a finite number in the Tag data type range.");

        var staleAfter = TimeSpan.FromMilliseconds(Math.Clamp((long)pollIntervalMs * 3, 10_000, 300_000));
        var quality = now - receivedAt > staleAfter
            ? TagQuality.Stale
            : retained ? TagQuality.Uncertain : TagQuality.Good;
        var scaled = EngineeringValue.From(raw, tag);
        return new TagValueSnapshot(tag.Id, deviceId, raw, scaled, quality, receivedAt, "MQTT",
            quality == TagQuality.Stale ? "No recent MQTT message." :
            quality == TagQuality.Uncertain ? "Retained MQTT value; source freshness is unknown." : null);
    }



    private static TagValueSnapshot Error(Guid deviceId, TagDefinition tag, DateTimeOffset now, string message) =>
        new(tag.Id, deviceId, null, null, TagQuality.Bad, now, "MQTT", message);
}
