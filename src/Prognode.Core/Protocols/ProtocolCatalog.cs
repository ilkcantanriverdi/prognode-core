using Prognode.Contracts.Protocols;

namespace Prognode.Core.Protocols;

public sealed class ProtocolCatalog
{
    private static readonly IReadOnlyList<ProtocolDescriptor> Items =
    [
        new(
            "modbus-tcp",
            "Modbus TCP",
            "Native Modbus TCP client connector with protocol-level connection test.",
            "Available",
            true,
            "Live Runtime"
        ),
        new("siemens-s7-tcp", "Siemens S7 TCP", "Read-only ISO-on-TCP S7comm absolute DB reader; S7-1200/1500 with non-optimized DB and PUT/GET access.", "Available", true, "Field Test"),
        new(
            "opc-ua",
            "OPC UA",
            "Read-only NodeId polling over a trusted SignAndEncrypt OPC UA session.",
            "Available",
            true,
            "Field Test"
        ),
        new(
            "mqtt",
            "MQTT",
            "Read-only exact-topic subscription from an external broker; numeric and boolean UTF-8 payloads.",
            "Available",
            true,
            "Field Test"
        )
    ];

    public IReadOnlyList<ProtocolDescriptor> GetAll() => Items;
}
