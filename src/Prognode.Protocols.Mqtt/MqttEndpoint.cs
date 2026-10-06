namespace Prognode.Protocols.Mqtt;

public sealed record MqttEndpoint(string Host, int Port, bool UseTls)
{
    public static MqttEndpoint Parse(string? address, int? port)
    {
        var source = (address ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(source))
            throw new ArgumentException("MQTT broker host is required.");

        // The device's existing Host and Port columns carry the endpoint. Secrets must
        // never be embedded in a URL because device records are returned by the API.
        var uriText = source.Contains("://", StringComparison.Ordinal)
            ? source
            : $"mqtts://{source}";
        if (!Uri.TryCreate(uriText, UriKind.Absolute, out var uri) ||
            uri.Scheme is not ("mqtt" or "mqtts") ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            uri.UserInfo.Length != 0 ||
            uri.AbsolutePath != "/" ||
            uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("MQTT host must be a broker name or mqtt:// / mqtts:// URL without credentials or path.");

        if (!uri.IsDefaultPort)
            throw new ArgumentException("Set the MQTT port in the Port field, not in the broker URL.");

        var resolvedPort = port ?? (uri.Scheme == "mqtts" ? 8883 : 1883);
        if (resolvedPort is < 1 or > 65535)
            throw new ArgumentException("MQTT port must be between 1 and 65535.");
        return new MqttEndpoint(uri.Host, resolvedPort, uri.Scheme == "mqtts");
    }
}
