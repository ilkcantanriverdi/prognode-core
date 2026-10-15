using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Prognode.Contracts.Tags;

namespace Prognode.Protocols.EtherNetIp;

/// <summary>
/// A Logix tag name as written in Studio 5000: <c>Speed</c>, <c>Tank.Level</c>, <c>Counters[3]</c>,
/// <c>Grid[1,2]</c>, <c>Program:MainProgram.Motor.Amps</c>, and for BOOL a bit of an integer tag
/// <c>Status.5</c>. Encoded as a CIP symbolic EPATH (ANSI extended symbol segments 0x91 and member
/// segments for array indexes).
/// </summary>
public sealed class LogixTagPath
{
    private static readonly Regex Segment = new(@"^([A-Za-z_][A-Za-z0-9_]{0,39})(?:\[(\d{1,7}(?:,\d{1,7}){0,2})\])?$", RegexOptions.CultureInvariant);
    private static readonly Regex ProgramScope = new(@"^Program:[A-Za-z_][A-Za-z0-9_]{0,39}$", RegexOptions.CultureInvariant);

    private LogixTagPath(string name, byte[] path, int? bit)
    {
        Name = name;
        Path = path;
        Bit = bit;
    }

    /// <summary>The tag read from the controller (without the bit suffix).</summary>
    public string Name { get; }

    /// <summary>Padded EPATH bytes (without the path-size word count).</summary>
    public byte[] Path { get; }

    /// <summary>Bit of an integer tag for BOOL Tags (Status.5), else null.</summary>
    public int? Bit { get; }

    public static LogixTagPath Parse(string? text, TagDataType type)
    {
        var address = (text ?? "").Trim();
        if (address.Length is 0 or > 200)
            throw new ArgumentException("Enter the Logix tag name, e.g. Speed, Tank.Level, Counters[3] or Program:Main.Motor.Amps.");
        var parts = address.Split('.');
        int? bit = null;
        if (parts.Length > 1 && parts[^1].All(char.IsAsciiDigit))
        {
            if (type != TagDataType.Bool) throw new ArgumentException($"'{address}' is a bit of an integer tag; use the BOOL data type.");
            bit = int.Parse(parts[^1], CultureInfo.InvariantCulture);
            if (bit > 63) throw new ArgumentException($"Bit {bit} is out of range (0-63): '{address}'.");
            parts = parts[..^1];
        }
        var bytes = new List<byte>();
        for (var i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (i == 0 && ProgramScope.IsMatch(part) && parts.Length > 1)
            {
                AddSymbol(bytes, part);
                continue;
            }
            var match = Segment.Match(part);
            if (!match.Success)
                throw new ArgumentException($"Invalid Logix tag name '{address}'. Use letters, digits and _ (Speed, Tank.Level, Counters[3], Program:Main.Motor.Amps).");
            AddSymbol(bytes, match.Groups[1].Value);
            if (match.Groups[2].Success)
                foreach (var index in match.Groups[2].Value.Split(','))
                    AddMember(bytes, uint.Parse(index, CultureInfo.InvariantCulture));
        }
        return new(string.Join('.', parts), [.. bytes], bit);
    }

    private static void AddSymbol(List<byte> bytes, string name)
    {
        var ascii = Encoding.ASCII.GetBytes(name);
        bytes.Add(0x91);
        bytes.Add((byte)ascii.Length);
        bytes.AddRange(ascii);
        if (ascii.Length % 2 == 1) bytes.Add(0);
    }

    // Logical member segments: 8-bit 0x28, 16-bit 0x29 (pad), 32-bit 0x2A (pad); little-endian.
    private static void AddMember(List<byte> bytes, uint index)
    {
        if (index <= 0xFF) { bytes.Add(0x28); bytes.Add((byte)index); }
        else if (index <= 0xFFFF) { bytes.Add(0x29); bytes.Add(0); bytes.Add((byte)index); bytes.Add((byte)(index >> 8)); }
        else { bytes.Add(0x2A); bytes.Add(0); bytes.Add((byte)index); bytes.Add((byte)(index >> 8)); bytes.Add((byte)(index >> 16)); bytes.Add((byte)(index >> 24)); }
    }
}
