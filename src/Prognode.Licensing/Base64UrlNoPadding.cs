namespace Prognode.Licensing;

internal static class Base64UrlNoPadding
{
    public static byte[] Decode(string? value, string fieldName)
    {
        var text = value ?? string.Empty;
        if (text.Length == 0)
            throw new InvalidOperationException($"{fieldName} is empty.");
        if (text.Contains('=') || text.Contains('+') || text.Contains('/'))
            throw new InvalidOperationException($"{fieldName} must use Base64Url without padding.");
        if (text.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            throw new InvalidOperationException($"{fieldName} contains invalid Base64Url characters.");

        var standard = text.Replace('-', '+').Replace('_', '/');
        standard += (standard.Length % 4) switch
        {
            0 => string.Empty,
            2 => "==",
            3 => "=",
            _ => throw new InvalidOperationException($"{fieldName} has an invalid Base64Url length.")
        };

        try
        {
            return Convert.FromBase64String(standard);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException($"{fieldName} is not valid Base64Url.", ex);
        }
    }

    public static string Encode(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
