using System.Globalization;
using System.Text.RegularExpressions;
using Prognode.Contracts.Tags;

namespace Prognode.Protocols.Omron;

/// <summary>An Omron memory area with its FINS word and bit area codes (CS/CJ/CP/NJ/NX mode).</summary>
public sealed record FinsArea(string Name, byte WordCode, byte BitCode, int MaxWord, string Description)
{
    public bool IsTimerCounter => Name is "T" or "C";
}

/// <summary>
/// An Omron FINS Tag address as written in CX-Programmer / Sysmac Studio: D100, W10, H5, A20,
/// CIO100 (or just 100), E0_100 for an EM bank, T0 / C0 for a timer or counter, and a decimal bit
/// of a word (D100.05, CIO0.15, W10.3) for BOOL. T/C with BOOL read the completion flag; with a
/// 16-bit type the present value. 32/64-bit values take consecutive words, low word first.
/// </summary>
public readonly record struct FinsAddress(FinsArea Area, int Word, int? Bit, int Words)
{
    private static readonly FinsArea Cio = new("CIO", 0xB0, 0x30, 6143, "CIO area");
    private static readonly FinsArea[] Areas =
    [
        Cio,
        new("D", 0x82, 0x02, 32767, "data memory (DM)"),
        new("W", 0xB1, 0x31, 511, "work area (WR)"),
        new("H", 0xB2, 0x32, 1535, "holding area (HR)"),
        new("A", 0xB3, 0x33, 11535, "auxiliary area (AR)"),
        new("T", 0x89, 0x09, 4095, "timer"),
        new("C", 0x89, 0x09, 4095, "counter"),
    ];

    private static readonly Regex Format = new(@"^(?:(CIO|DM|D|W|H|A|T|C)|E([0-9A-F])_)?(\d{1,5})(?:\.(\d{1,2}))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>The 2-byte FINS word address sent on the wire (counters follow the timers at 0x8000).</summary>
    public int WireWord => Area.Name == "C" ? 0x8000 + Word : Word;

    /// <summary>T/C read as BOOL use the completion flag area (one byte per point), not words.</summary>
    public bool IsFlag => Area.IsTimerCounter && Bit is null && Words == 0;

    public byte Code => IsFlag ? Area.BitCode : Area.WordCode;

    public static FinsAddress Parse(string? text, TagDataType type)
    {
        var match = Format.Match((text ?? "").Trim());
        if (!match.Success)
            throw new ArgumentException($"Invalid Omron address '{text}'. Use e.g. D100, W10, H5, A20, CIO100, E0_100, T0, C0 or D100.05 for a bit of a word.");
        FinsArea area;
        if (match.Groups[2].Success)
        {
            var bank = Convert.ToInt32(match.Groups[2].Value, 16);
            area = new($"E{bank:X}_", (byte)(0xA0 + bank), (byte)(0x20 + bank), 32767, $"EM bank {bank:X}");
        }
        else
        {
            var name = match.Groups[1].Success ? match.Groups[1].Value.ToUpperInvariant() : "CIO";
            if (name == "DM") name = "D";
            area = Areas.First(a => a.Name == name);
        }
        var word = int.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        int? bit = match.Groups[4].Success ? int.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture) : null;
        if (word > area.MaxWord) throw new ArgumentException($"Omron {area.Description} goes up to {area.Name}{area.MaxWord}: '{text}'.");
        if (bit is > 15) throw new ArgumentException($"Omron bit numbers are 00 to 15 (decimal): '{text}'.");

        if (area.IsTimerCounter)
        {
            if (bit is not null) throw new ArgumentException($"{area.Name}{word} has no bits; use BOOL for its completion flag.");
            return type switch
            {
                TagDataType.Bool => new(area, word, null, 0),
                TagDataType.Word or TagDataType.UInt16 or TagDataType.Int16 => new(area, word, null, 1),
                _ => throw new ArgumentException($"{area.Name}{word} ({area.Description}) is BOOL (completion flag) or a 16-bit present value."),
            };
        }
        if (type == TagDataType.Bool && bit is null)
            throw new ArgumentException($"A BOOL on Omron needs the bit: {area.Name}{word}.00 … {area.Name}{word}.15.");
        if (type != TagDataType.Bool && bit is not null)
            throw new ArgumentException("'.bit' addresses need the BOOL data type.");
        var words = type switch
        {
            TagDataType.Bool or TagDataType.Word or TagDataType.Int16 or TagDataType.UInt16 => 1,
            TagDataType.Int32 or TagDataType.UInt32 or TagDataType.Float32 => 2,
            TagDataType.Int64 or TagDataType.UInt64 or TagDataType.Float64 => 4,
            _ => throw new ArgumentException("Unsupported Omron data type."),
        };
        if (word + words - 1 > area.MaxWord) throw new ArgumentException($"Omron {area.Description} goes up to {area.Name}{area.MaxWord}: '{text}'.");
        return new(area, word, bit, words);
    }

    public override string ToString() =>
        $"{Area.Name}{Word.ToString(CultureInfo.InvariantCulture)}{(Bit is { } b ? "." + b.ToString("00", CultureInfo.InvariantCulture) : "")}";
}
