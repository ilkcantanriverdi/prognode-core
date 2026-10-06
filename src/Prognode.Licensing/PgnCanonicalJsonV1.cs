using System.Buffers;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Prognode.Licensing;

/// <summary>
/// PGN_CANONICAL_JSON_1
/// - recursively sorts object property names in ascending ordinal/lexical order
/// - preserves array order
/// - emits compact UTF-8 JSON
/// - performs no Unicode normalization
/// - omits no JSON property except that the signature field is not part of the signed envelope
/// - serializes negative zero as 0
/// </summary>
internal static class PgnCanonicalJsonV1
{
    private static readonly string[] SignedEnvelopeFields =
    [
        "formatVersion",
        "keyId",
        "signatureAlgorithm",
        "canonicalization",
        "payload"
    ];

    public static byte[] CanonicalizeSignedEnvelope(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("License envelope must be a JSON object.");

        var selected = new List<JsonProperty>(SignedEnvelopeFields.Length);
        foreach (var name in SignedEnvelopeFields)
        {
            if (!root.TryGetProperty(name, out _))
                throw new InvalidOperationException($"License envelope field '{name}' is required.");

            selected.Add(root.EnumerateObject().First(p => p.NameEquals(name)));
        }

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, new JsonWriterOptions
        {
            Indented = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            SkipValidation = false
        }))
        {
            writer.WriteStartObject();
            foreach (var property in selected.OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteCanonical(writer, property.Value);
            }
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                return;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                    WriteCanonical(writer, item);
                writer.WriteEndArray();
                return;

            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                return;

            case JsonValueKind.Number:
                WriteCanonicalNumber(writer, element.GetRawText());
                return;

            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                return;

            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                return;

            case JsonValueKind.Null:
                writer.WriteNullValue();
                return;

            case JsonValueKind.Undefined:
                // Undefined is not valid JSON and JsonDocument cannot normally contain it.
                // The contract says undefined fields are omitted, therefore an undefined value
                // is rejected instead of being silently converted into a different signed value.
                throw new InvalidOperationException("Undefined JSON values are not supported in a license.");

            default:
                throw new InvalidOperationException($"Unsupported JSON token '{element.ValueKind}'.");
        }
    }

    private static void WriteCanonicalNumber(Utf8JsonWriter writer, string raw)
    {
        // JSON itself rejects NaN/Infinity. Normalize every textual representation of numeric -0 to 0.
        if (raw.StartsWith("-", StringComparison.Ordinal) &&
            decimal.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var decimalValue) &&
            decimalValue == 0m)
        {
            writer.WriteRawValue("0", skipInputValidation: false);
            return;
        }

        writer.WriteRawValue(raw, skipInputValidation: false);
    }
}
