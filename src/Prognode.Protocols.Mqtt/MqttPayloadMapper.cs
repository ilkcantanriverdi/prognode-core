using System.Globalization;
using System.Text;
using Prognode.Contracts.Tags;

namespace Prognode.Protocols.Mqtt;

public static class MqttPayloadMapper
{
    public static TagValueSnapshot Map(Guid deviceId, TagDefinition tag, byte[] payload,
        bool retained, DateTimeOffset receivedAt, DateTimeOffset now, int pollIntervalMs)
    {
        if (payload.Length is 0 or > 256)
            return Error(deviceId, tag, now, "MQTT payload must contain 1–256 UTF-8 bytes.");

        string valueText;
        try { valueText = new UTF8Encoding(false, true).GetString(payload).Trim(); }
        catch (DecoderFallbackException) { return Error(deviceId, tag, now, "MQTT payload is not valid UTF-8."); }

        double raw;
        if (tag.DataType == TagDataType.Bool)
        {
            if (valueText.Equals("true", StringComparison.OrdinalIgnoreCase) || valueText == "1") raw = 1;
            else if (valueText.Equals("false", StringComparison.OrdinalIgnoreCase) || valueText == "0") raw = 0;
            else return Error(deviceId, tag, now, "MQTT BOOL payload must be true, false, 1, or 0.");
        }
        else if (!double.TryParse(valueText, NumberStyles.Float, CultureInfo.InvariantCulture, out raw) ||
                 !double.IsFinite(raw) || !InRange(raw, tag.DataType))
            return Error(deviceId, tag, now, "MQTT payload is not a finite number in the Tag data type range.");

        var staleAfter = TimeSpan.FromMilliseconds(Math.Clamp((long)pollIntervalMs * 3, 10_000, 300_000));
        var quality = now - receivedAt > staleAfter
            ? TagQuality.Stale
            : retained ? TagQuality.Uncertain : TagQuality.Good;
        var scaled = tag.DataType is TagDataType.Bool or TagDataType.Word
            ? raw : raw / Math.Pow(10, tag.DecimalPlaces) * tag.Scale + tag.Offset;
        return new TagValueSnapshot(tag.Id, deviceId, raw, scaled, quality, receivedAt, "MQTT",
            quality == TagQuality.Stale ? "No recent MQTT message." :
            quality == TagQuality.Uncertain ? "Retained MQTT value; source freshness is unknown." : null);
    }

    private static bool InRange(double number, TagDataType type) => type switch
    {
        TagDataType.Word or TagDataType.UInt16 => IsInteger(number) && number is >= 0 and <= ushort.MaxValue,
        TagDataType.Int16 => IsInteger(number) && number is >= short.MinValue and <= short.MaxValue,
        TagDataType.UInt32 => IsInteger(number) && number is >= 0 and <= uint.MaxValue,
        TagDataType.Int32 => IsInteger(number) && number is >= int.MinValue and <= int.MaxValue,
        TagDataType.Float32 => Math.Abs(number) <= float.MaxValue,
        _ => false
    };

    private static bool IsInteger(double value) => Math.Truncate(value) == value;

    private static TagValueSnapshot Error(Guid deviceId, TagDefinition tag, DateTimeOffset now, string message) =>
        new(tag.Id, deviceId, null, null, TagQuality.Bad, now, "MQTT", message);
}
