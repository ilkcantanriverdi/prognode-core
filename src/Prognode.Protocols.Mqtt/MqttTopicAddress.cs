using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Prognode.Protocols.Mqtt;

/// <summary>
/// An MQTT Tag address: the exact topic, optionally followed by <c>#</c> and the field to read from a
/// JSON message, e.g. <c>plant/line1/env#temp</c> or <c>gw/01/state#sensors[0].value</c>. Without a
/// field the whole message is the value (<c>21.5</c>, <c>true</c>).
/// </summary>
public sealed record MqttTopicAddress(string Topic, IReadOnlyList<object>? JsonPath)
{
    private static readonly Regex Segment = new(@"^([^.\[\]]+)?((\[\d+\])*)$", RegexOptions.CultureInvariant);

    public static MqttTopicAddress Parse(string? address)
    {
        var text = address ?? "";
        var separator = text.IndexOf('#');
        var topic = separator < 0 ? text : text[..separator];
        if (string.IsNullOrWhiteSpace(topic) || Encoding.UTF8.GetByteCount(topic) > 512 ||
            topic.IndexOfAny(['+', '\0']) >= 0 || topic.Any(char.IsControl))
            throw new ArgumentException("MQTT Tag address must be an exact topic (1–512 UTF-8 bytes), without wildcards or control characters, optionally followed by #field for JSON messages.");
        if (separator < 0) return new(topic, null);

        var field = text[(separator + 1)..].Trim();
        if (field.StartsWith("$.")) field = field[2..];
        if (field.Length is 0 or > 256)
            throw new ArgumentException("Write the JSON field after '#', e.g. plant/line1/env#temp or gw/01#sensors[0].value.");
        var path = new List<object>();
        foreach (var part in field.Split('.'))
        {
            var match = Segment.Match(part);
            if (part.Length == 0 || !match.Success || (match.Groups[1].Length == 0 && path.Count == 0 && match.Groups[2].Length == 0))
                throw new ArgumentException($"Invalid JSON field '{field}'. Use names separated by dots and [n] for array items, e.g. sensors[0].value.");
            if (match.Groups[1].Success && match.Groups[1].Length > 0) path.Add(match.Groups[1].Value);
            foreach (Capture index in match.Groups[3].Captures)
                path.Add(int.Parse(index.Value.Trim('[', ']')));
        }
        return new(topic, path);
    }

    /// <summary>The value at the JSON path as text for the number/bool parser, or an error.</summary>
    public bool TryExtract(string payload, out string value, out string? error)
    {
        value = payload;
        error = null;
        if (JsonPath is null) return true;
        try
        {
            using var document = JsonDocument.Parse(payload);
            var element = document.RootElement;
            foreach (var step in JsonPath)
            {
                if (step is string name)
                {
                    if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(name, out element))
                    {
                        error = $"JSON field '{name}' was not found in the MQTT message.";
                        return false;
                    }
                }
                else if (step is int index)
                {
                    if (element.ValueKind != JsonValueKind.Array || index >= element.GetArrayLength())
                    {
                        error = $"JSON array item [{index}] was not found in the MQTT message.";
                        return false;
                    }
                    element = element[index];
                }
            }
            switch (element.ValueKind)
            {
                case JsonValueKind.Number: value = element.GetRawText(); return true;
                case JsonValueKind.True: value = "true"; return true;
                case JsonValueKind.False: value = "false"; return true;
                case JsonValueKind.String: value = element.GetString() ?? ""; return true;
                default:
                    error = "The JSON field holds an object, array or null, not a value.";
                    return false;
            }
        }
        catch (JsonException)
        {
            error = "MQTT message is not valid JSON.";
            return false;
        }
    }
}
