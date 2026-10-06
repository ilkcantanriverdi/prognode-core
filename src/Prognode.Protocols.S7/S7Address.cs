using System.Text.RegularExpressions;
using Prognode.Contracts.Tags;
namespace Prognode.Protocols.S7;

// Absolute, non-optimized Siemens DB addresses only. No symbolic/optimized DB access.
public readonly record struct S7Address(int Db, int ByteOffset, int Bit, int Length)
{
    public static S7Address Parse(string? text, TagDataType type)
    {
        var pattern = type == TagDataType.Bool ? @"^DB(\d+)\.DBX(\d+)\.([0-7])$"
            : type is TagDataType.Int16 or TagDataType.UInt16 or TagDataType.Word ? @"^DB(\d+)\.DBW(\d+)$"
            : @"^DB(\d+)\.DBD(\d+)$";
        var m = Regex.Match((text ?? "").Trim(), pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!m.Success) throw new ArgumentException($"Invalid S7 address '{text}' for {type}. Use DB1.DBX0.0 / DB1.DBW2 / DB1.DBD4.");
        var db = int.Parse(m.Groups[1].Value);
        var offset = int.Parse(m.Groups[2].Value);
        var bit = type == TagDataType.Bool ? int.Parse(m.Groups[3].Value) : 0;
        var length = type == TagDataType.Bool ? 1 : type is TagDataType.Word or TagDataType.Int16 or TagDataType.UInt16 ? 2 : 4;
        if (db is < 1 or > 65535 || offset < 0 || offset > 65535 - length)
            throw new ArgumentException("S7 DB number or offset exceeds the supported range.");
        if (type != TagDataType.Bool && type is not (TagDataType.Word or TagDataType.Int16 or TagDataType.UInt16 or TagDataType.Int32 or TagDataType.UInt32 or TagDataType.Float32))
            throw new ArgumentException("Unsupported S7 datatype.");
        return new(db, offset, bit, length);
    }
}
