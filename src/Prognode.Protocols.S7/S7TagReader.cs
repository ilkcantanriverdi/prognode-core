using System.Buffers.Binary;
using Prognode.Contracts.Devices;
using Prognode.Contracts.Tags;
using Prognode.Protocols.Abstractions;
namespace Prognode.Protocols.S7;

public sealed class S7TagReader : ITagReader
{
    public bool CanHandle(DeviceDefinition device) => device.Protocol.Equals("Siemens S7 TCP",StringComparison.OrdinalIgnoreCase);
    public async Task<IReadOnlyList<TagValueSnapshot>> ReadAsync(DeviceDefinition device,IReadOnlyList<TagDefinition> tags,CancellationToken cancellationToken)
    {
        var enabled = tags.Where(t=>t.Enabled).ToArray();
        if (enabled.Length==0) return Array.Empty<TagValueSnapshot>();
        if (string.IsNullOrWhiteSpace(device.Host)) throw new IOException("Siemens PLC IP missing.");
        var result = new List<TagValueSnapshot>(enabled.Length);
        await using var client = new S7Client();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(Math.Max(3000,enabled.Length*100+2000));
        var (rack,slot) = DecodeRackSlot(device.UnitId ?? 1);
        await client.ConnectAsync(device.Host,device.Port??102,rack,slot,2000,deadline.Token);
        foreach (var tag in enabled)
        {
            var addr=S7Address.Parse(tag.Address,tag.DataType);
            var data=await client.ReadAsync(addr,deadline.Token);
            double raw=tag.DataType switch {
                TagDataType.Bool => data[0]!=0?1:0,
                TagDataType.Word or TagDataType.UInt16 => BinaryPrimitives.ReadUInt16BigEndian(data),
                TagDataType.Int16 => BinaryPrimitives.ReadInt16BigEndian(data),
                TagDataType.UInt32 => BinaryPrimitives.ReadUInt32BigEndian(data),
                TagDataType.Int32 => BinaryPrimitives.ReadInt32BigEndian(data),
                TagDataType.Float32 => BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(data)),
                _ => throw new NotSupportedException("Unsupported S7 type") };
            var value = tag.DataType is TagDataType.Bool or TagDataType.Word ? raw
                : (tag.DataType is TagDataType.Int16 or TagDataType.UInt16 or TagDataType.Int32 or TagDataType.UInt32
                    ? raw / Math.Pow(10,tag.DecimalPlaces) : raw)*tag.Scale+tag.Offset;
            result.Add(new(tag.Id,device.Id,raw,value,TagQuality.Good,DateTimeOffset.UtcNow,"Siemens S7 TCP",null));
        }
        return result;
    }
    public static (int Rack,int Slot) DecodeRackSlot(int encoded) => (encoded/32,encoded%32);
}
