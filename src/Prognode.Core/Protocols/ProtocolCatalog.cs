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
        new(
            "modbus-rtu",
            "Modbus RTU (RS-485)",
            "Serial Modbus RTU master over a COM port or USB RS-485 adapter; several devices per line by unit ID.",
            "Available",
            true,
            "Field Test"
        ),
        new(
            "mitsubishi-mc",
            "Mitsubishi MC",
            "Read-only MC protocol (SLMP 3E binary) for Q, L, iQ-R and iQ-F (FX5) PLCs: D, W, R, M, X, Y, B, L, SM, SD, timers and counters.",
            "Available",
            true,
            "Field Test"
        ),
        new(
            "omron-fins",
            "Omron FINS",
            "Read-only FINS/TCP for CS, CJ, CP, NJ and NX PLCs: D, CIO, W, H, A, EM banks, timers and counters.",
            "Available",
            true,
            "Field Test"
        ),
        new(
            "ethernet-ip",
            "EtherNet/IP (Logix)",
            "Read-only tag reads by name from Allen-Bradley ControlLogix, CompactLogix, GuardLogix and Micro800 controllers.",
            "Available",
            true,
            "Field Test"
        ),
        new(
            "bacnet-ip",
            "BACnet/IP",
            "Read-only Present_Value of analog, binary and multi-state objects from building automation controllers on the IP network.",
            "Available",
            true,
            "Field Test"
        ),
        new("siemens-s7-tcp", "Siemens S7 TCP", "Read-only ISO-on-TCP S7comm reader: inputs, outputs, bit memory and non-optimized DBs (absolute addresses); S7-1200/1500 need PUT/GET.", "Available", true, "Field Test"),
        new(
            "opc-ua",
            "OPC UA",
            "Read-only NodeId polling over a trusted SignAndEncrypt OPC UA session, anonymous or with user name and password.",
            "Available",
            true,
            "Field Test"
        ),
        new(
            "mqtt",
            "MQTT",
            "Read-only exact-topic subscription from an external broker (optional login); plain values or one field of a JSON message.",
            "Available",
            true,
            "Field Test"
        )
    ];

    public IReadOnlyList<ProtocolDescriptor> GetAll() => Items;
}
