using System.Globalization;
using System.Text.RegularExpressions;
using Prognode.Contracts.Tags;

namespace Prognode.Protocols.Bacnet;

/// <summary>A BACnet object type whose Present_Value PROGNODE reads.</summary>
public sealed record BacnetObjectType(int Code, string Short, string Name, bool Binary);

/// <summary>
/// A BACnet object whose Present_Value a Tag reads: <c>AI:1</c>, <c>AV:12</c>, <c>BO:3</c>, <c>MSV:2</c>, or the
/// full name <c>analog-input,1</c> / <c>analog-input:1</c>. Instance 0..4194302.
/// </summary>
public readonly record struct BacnetObjectAddress(BacnetObjectType Type, int Instance)
{
    public const int PresentValue = 85;
    public const int MaxInstance = 4194302;

    public static readonly BacnetObjectType[] Types =
    [
        new(0, "AI", "analog-input", false),
        new(1, "AO", "analog-output", false),
        new(2, "AV", "analog-value", false),
        new(3, "BI", "binary-input", true),
        new(4, "BO", "binary-output", true),
        new(5, "BV", "binary-value", true),
        new(13, "MSI", "multi-state-input", false),
        new(14, "MSO", "multi-state-output", false),
        new(19, "MSV", "multi-state-value", false),
        new(23, "ACC", "accumulator", false),
        new(45, "IV", "integer-value", false),
        new(46, "LAV", "large-analog-value", false),
        new(48, "PIV", "positive-integer-value", false),
    ];

    private static readonly Regex Format = new(@"^([A-Za-z][A-Za-z_-]*?)\s*[:,\s]\s*(\d{1,7})$|^([A-Za-z]{2,3})(\d{1,7})$", RegexOptions.CultureInvariant);

    /// <summary>The 32-bit BACnetObjectIdentifier: 10-bit type, 22-bit instance.</summary>
    public uint Identifier => (uint)Type.Code << 22 | (uint)Instance;

    public static BacnetObjectAddress Parse(string? text, TagDataType type)
    {
        var match = Format.Match((text ?? "").Trim());
        if (!match.Success)
            throw new ArgumentException($"Invalid BACnet object '{text}'. Use type and instance: AI:1, AV:12, BI:3, BO:1, MSV:2 or analog-input,1.");
        var name = (match.Groups[1].Success ? match.Groups[1].Value : match.Groups[3].Value).Replace('_', '-');
        var instanceText = match.Groups[2].Success ? match.Groups[2].Value : match.Groups[4].Value;
        var objectType = Types.FirstOrDefault(t => t.Short.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                                                   t.Name.Equals(name, StringComparison.OrdinalIgnoreCase) ||
                                                   t.Name.Replace("-", "").Equals(name.Replace("-", ""), StringComparison.OrdinalIgnoreCase))
            ?? throw new ArgumentException($"BACnet object type '{name}' is not read. Use AI, AO, AV, BI, BO, BV, MSI, MSO, MSV, ACC, IV, LAV or PIV.");
        var instance = int.Parse(instanceText, CultureInfo.InvariantCulture);
        if (instance > MaxInstance) throw new ArgumentException($"BACnet instance must be 0..{MaxInstance}: '{text}'.");
        if (objectType.Binary && type != TagDataType.Bool)
            throw new ArgumentException($"{objectType.Short}:{instance} is a binary object; use the BOOL data type.");
        if (!objectType.Binary && type == TagDataType.Bool)
            throw new ArgumentException($"{objectType.Short}:{instance} is not a binary object; BOOL needs BI, BO or BV.");
        return new(objectType, instance);
    }

    public override string ToString() => $"{Type.Short}:{Instance}";
}
