namespace Prognode.Protocols.OpcUa;

public static class OpcUaEndpoint
{
    public static string Parse(string? endpointUrl)
    {
        if (!Uri.TryCreate((endpointUrl ?? string.Empty).Trim(), UriKind.Absolute, out var uri) ||
            uri.Scheme != "opc.tcp" || string.IsNullOrWhiteSpace(uri.Host) ||
            uri.Port is < 1 or > 65535 || uri.UserInfo.Length != 0 ||
            uri.Query.Length != 0 || uri.Fragment.Length != 0)
            throw new ArgumentException("OPC UA endpoint must be an opc.tcp://host:port/path URL without credentials or query.");
        return uri.AbsoluteUri;
    }
}
