using System.Text.RegularExpressions;
using Prognode.Contracts.Tags;
namespace Prognode.Protocols.S7;

/// <summary>S7 memory areas with their S7comm area codes.</summary>
public enum S7Area : byte
{
    Inputs = 0x81,
    Outputs = 0x82,
    Merkers = 0x83,
    DataBlock = 0x84,
}

// Absolute addresses only: non-optimized DBs (DB1.DBX0.0 / DB1.DBW2 / DB1.DBD4) and the process
// image and bit memory (I/Q/M, also the German E/A and the TIA "%" prefix: %I0.0, %IW64, %QD4,
// %MW20). No symbolic/optimized DB access. Db is 0 for I/Q/M.
public readonly record struct S7Address(int Db, int ByteOffset, int Bit, int Length, S7Area Area = S7Area.DataBlock)
{
    private const RegexOptions Options = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    public static S7Address Parse(string? text, TagDataType type)
    {
        if (type != TagDataType.Bool && type is not (TagDataType.Word or TagDataType.Int16 or TagDataType.UInt16 or TagDataType.Int32 or TagDataType.UInt32 or TagDataType.Float32))
            throw new ArgumentException("Unsupported S7 datatype.");
        var value = (text ?? "").Trim().TrimStart('%');
        var length = type == TagDataType.Bool ? 1 : type is TagDataType.Word or TagDataType.Int16 or TagDataType.UInt16 ? 2 : 4;
        var size = type == TagDataType.Bool ? "X" : length == 2 ? "W" : "D";

        var db = Regex.Match(value, type == TagDataType.Bool ? @"^DB(\d+)\.DBX(\d+)\.([0-7])$" : $@"^DB(\d+)\.DB{size}(\d+)$", Options);
        if (db.Success)
        {
            var number = int.Parse(db.Groups[1].Value);
            var dbOffset = int.Parse(db.Groups[2].Value);
            if (number is < 1 or > 65535 || dbOffset < 0 || dbOffset > 65535 - length)
                throw new ArgumentException("S7 DB number or offset exceeds the supported range.");
            return new(number, dbOffset, type == TagDataType.Bool ? int.Parse(db.Groups[3].Value) : 0, length);
        }

        var area = Regex.Match(value, type == TagDataType.Bool ? @"^([IEQAM])X?(\d+)\.([0-7])$" : $@"^([IEQAM]){size}(\d+)$", Options);
        if (!area.Success)
            throw new ArgumentException(type == TagDataType.Bool
                ? $"Invalid S7 address '{text}' for {type}. Use DB1.DBX0.0, I0.0, Q0.1 or M10.0."
                : length == 2
                    ? $"Invalid S7 address '{text}' for {type}. Use DB1.DBW2, IW64, QW2 or MW20."
                    : $"Invalid S7 address '{text}' for {type}. Use DB1.DBD4, ID100, QD4 or MD30.");
        var offset = int.Parse(area.Groups[2].Value);
        if (offset < 0 || offset > 65535 - length)
            throw new ArgumentException("S7 byte offset exceeds the supported range.");
        var memory = char.ToUpperInvariant(area.Groups[1].Value[0]) switch
        {
            'I' or 'E' => S7Area.Inputs,
            'Q' or 'A' => S7Area.Outputs,
            _ => S7Area.Merkers,
        };
        return new(0, offset, type == TagDataType.Bool ? int.Parse(area.Groups[3].Value) : 0, length, memory);
    }
}
