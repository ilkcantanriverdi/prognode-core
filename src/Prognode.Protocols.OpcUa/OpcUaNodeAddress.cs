using Opc.Ua;

namespace Prognode.Protocols.OpcUa;

public static class OpcUaNodeAddress
{
    public static ExpandedNodeId Parse(string? address)
    {
        var value = (address ?? string.Empty).Trim();
        if (value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
            value.StartsWith("urn:", StringComparison.OrdinalIgnoreCase))
            value = "nsu=" + value;
        if (value.Length == 0) throw new FormatException("OPC UA NodeId is empty.");
        return ExpandedNodeId.Parse(value);
    }

    public static NodeId Resolve(string? address, NamespaceTable serverNamespaces)
    {
        var expanded = Parse(address);
        var node = ExpandedNodeId.ToNodeId(expanded, serverNamespaces);
        if (NodeId.IsNull(node))
            throw new ArgumentException($"OPC UA namespace '{expanded.NamespaceUri}' is not advertised by the server.");
        return node;
    }
}
