using System.Globalization;
using System.Text.RegularExpressions;
using Prognode.Contracts.Tags;

namespace Prognode.Protocols.Mitsubishi;

/// <summary>Mitsubishi PLC series: how X/Y numbers are written (hex on Q/L/iQ-R, octal on iQ-F FX5).</summary>
public enum McSeries
{
    QLiQR = 0,
    FX5 = 1,
}

/// <summary>A device memory type with its 3E binary device code.</summary>
public sealed record McDevice(string Name, byte Code, bool IsBit, int NumberBase, string Description);

/// <summary>
/// A Mitsubishi MC Tag address: device and number (D100, M0, X1F, ZR1000, TN0), a word device with a
/// hex bit (D100.A) for BOOL. X/Y/B/W/SB/SW numbers are hexadecimal; on FX5, X/Y are octal.
/// ZR (serial file register) is left out until its numbering (decimal in GX Works, hexadecimal in
/// MC client libraries) is confirmed on a real PLC; R covers file registers meanwhile.
/// </summary>
public readonly record struct McAddress(McDevice Device, int Number, int? Bit, int Words)
{
    private static readonly McDevice[] Devices =
    [
        new("SM", 0x91, true, 10, "special relay"),
        new("SD", 0xA9, false, 10, "special register"),
        new("SB", 0xA1, true, 16, "link special relay"),
        new("SW", 0xB5, false, 16, "link special register"),
        new("TS", 0xC1, true, 10, "timer contact"),
        new("TN", 0xC2, false, 10, "timer current value"),
        new("CS", 0xC4, true, 10, "counter contact"),
        new("CN", 0xC5, false, 10, "counter current value"),
        new("X", 0x9C, true, 16, "input"),
        new("Y", 0x9D, true, 16, "output"),
        new("M", 0x90, true, 10, "internal relay"),
        new("L", 0x92, true, 10, "latch relay"),
        new("F", 0x93, true, 10, "annunciator"),
        new("V", 0x94, true, 10, "edge relay"),
        new("B", 0xA0, true, 16, "link relay"),
        new("D", 0xA8, false, 10, "data register"),
        new("W", 0xB4, false, 16, "link register"),
        new("R", 0xAF, false, 10, "file register"),
    ];

    private static readonly Regex Format = new(@"^(SM|SD|SB|SW|TS|TN|CS|CN|X|Y|M|L|F|V|B|D|W|R)([0-9A-F]+)(?:\.([0-9A-F]))?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static McAddress Parse(string? text, TagDataType type, McSeries series = McSeries.QLiQR)
    {
        var match = Format.Match((text ?? "").Trim());
        if (!match.Success)
            throw new ArgumentException($"Invalid Mitsubishi address '{text}'. Use e.g. D100, M0, X1F, Y10, W1A, R100, TN0 or D100.A for a bit of a word.");
        var device = Devices.First(d => d.Name.Equals(match.Groups[1].Value, StringComparison.OrdinalIgnoreCase));
        var numberBase = device.Name is "X" or "Y" && series == McSeries.FX5 ? 8 : device.NumberBase;
        var digits = match.Groups[2].Value;
        int number;
        try { number = Convert.ToInt32(digits, numberBase); }
        catch (FormatException)
        {
            throw new ArgumentException(numberBase switch
            {
                8 => $"{device.Name} numbers on FX5 are octal (0-7 digits): '{text}'.",
                10 => $"{device.Name} numbers are decimal: '{text}'.",
                _ => $"{device.Name} numbers are hexadecimal: '{text}'.",
            });
        }
        if (number is < 0 or > 0xFFFFFF) throw new ArgumentException($"Mitsubishi device number out of range: '{text}'.");

        int? bit = match.Groups[3].Success ? Convert.ToInt32(match.Groups[3].Value, 16) : null;
        if (device.IsBit)
        {
            if (bit is not null) throw new ArgumentException($"{device.Name} is a bit device; write it without '.bit' ({device.Name}{digits}).");
            if (type != TagDataType.Bool) throw new ArgumentException($"{device.Name} ({device.Description}) is a bit device and needs the BOOL data type.");
            return new(device, number, null, 1);
        }
        if (type == TagDataType.Bool && bit is null)
            throw new ArgumentException($"A BOOL on a word device needs the bit: {device.Name}{digits}.0 … {device.Name}{digits}.F.");
        if (type != TagDataType.Bool && bit is not null)
            throw new ArgumentException("'.bit' addresses need the BOOL data type.");
        var words = type switch
        {
            TagDataType.Bool or TagDataType.Word or TagDataType.Int16 or TagDataType.UInt16 => 1,
            TagDataType.Int32 or TagDataType.UInt32 or TagDataType.Float32 => 2,
            TagDataType.Int64 or TagDataType.UInt64 or TagDataType.Float64 => 4,
            _ => throw new ArgumentException("Unsupported Mitsubishi data type."),
        };
        if ((long)number + words - 1 > 0xFFFFFF) throw new ArgumentException($"Mitsubishi device number out of range: '{text}'.");
        return new(device, number, bit, words);
    }

    public override string ToString() =>
        $"{Device.Name}{Convert.ToString(Number, Device.NumberBase == 10 ? 10 : 16).ToUpperInvariant()}{(Bit is { } b ? "." + b.ToString("X", CultureInfo.InvariantCulture) : "")}";
}
