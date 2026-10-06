using Prognode.Contracts.Devices;
using Prognode.Contracts.Tags;
using Prognode.Protocols.Abstractions;
namespace Prognode.Protocols.S7;
public sealed class S7TagValidator : ITagDefinitionValidator
{
    public bool CanHandle(DeviceDefinition device) => device.Protocol.Equals("Siemens S7 TCP",StringComparison.OrdinalIgnoreCase);
    public void Validate(TagDefinition candidate,IReadOnlyList<TagDefinition> existingTags)
    {
        if (candidate.BitIndex is not null) throw new ArgumentException("For S7 BOOL put the bit in address: DB1.DBX0.0. Leave extra BitIndex empty.");
        var a=S7Address.Parse(candidate.Address,candidate.DataType);
        foreach(var old in existingTags.Where(t=>t.DeviceId==candidate.DeviceId&&t.Id!=candidate.Id))
        {
            var b=S7Address.Parse(old.Address,old.DataType);
            if (a.Db!=b.Db || a.ByteOffset>b.ByteOffset+b.Length-1 || b.ByteOffset>a.ByteOffset+a.Length-1) continue;
            if (candidate.DataType==TagDataType.Bool && old.DataType==TagDataType.Bool && (a.ByteOffset!=b.ByteOffset||a.Bit!=b.Bit)) continue;
            // Reading overlapping BOOL and numeric data is intentional for industrial diagnostics.
            if (candidate.DataType==TagDataType.Bool ^ old.DataType==TagDataType.Bool) continue;
            throw new ArgumentException($"Overlapping S7 tag address: {old.Name} ({old.Address}).");
        }
    }
}
