using System.Reflection;

namespace Prognode.Contracts;

/// <summary>Version of the running PROGNODE build (set by installer\build.ps1), without the source revision suffix.</summary>
public static class ProductVersion
{
    public static string Current { get; } = Resolve();

    private static string Resolve()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(ProductVersion).Assembly;
        var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "unknown";
        var plus = version.IndexOf('+');
        return plus > 0 ? version[..plus] : version;
    }

    /// <summary>Development builds (x.y.z-dev) never offer updates.</summary>
    public static bool IsDevelopmentBuild(string version) => version.EndsWith("-dev", StringComparison.OrdinalIgnoreCase) || version == "unknown";

    /// <summary>
    /// Semantic-version order: 1.0.0-beta.3 &lt; 1.0.0-beta.10 &lt; 1.0.0-rc.1 &lt; 1.0.0 &lt; 1.0.1.
    /// Returns null when either value is not major.minor.patch[-prerelease].
    /// </summary>
    public static int? Compare(string left, string right)
    {
        if (!TryParse(left, out var a, out var aPre) || !TryParse(right, out var b, out var bPre)) return null;
        for (var i = 0; i < 3; i++)
            if (a[i] != b[i]) return a[i].CompareTo(b[i]);
        // A release sorts after its pre-releases.
        if (aPre.Length == 0 || bPre.Length == 0) return aPre.Length == bPre.Length ? 0 : aPre.Length == 0 ? 1 : -1;
        for (var i = 0; i < Math.Min(aPre.Length, bPre.Length); i++)
        {
            var aNum = long.TryParse(aPre[i], out var x);
            var bNum = long.TryParse(bPre[i], out var y);
            var c = aNum && bNum ? x.CompareTo(y) : aNum ? -1 : bNum ? 1 : string.CompareOrdinal(aPre[i], bPre[i]);
            if (c != 0) return Math.Sign(c);
        }
        return aPre.Length.CompareTo(bPre.Length);
    }

    private static bool TryParse(string value, out long[] core, out string[] prerelease)
    {
        core = new long[3];
        prerelease = [];
        var text = value.Split('+')[0].Trim();
        var dash = text.IndexOf('-');
        var numbers = (dash < 0 ? text : text[..dash]).Split('.');
        if (numbers.Length != 3) return false;
        for (var i = 0; i < 3; i++)
            if (!long.TryParse(numbers[i], out core[i])) return false;
        if (dash >= 0) prerelease = text[(dash + 1)..].Split('.');
        return true;
    }
}
