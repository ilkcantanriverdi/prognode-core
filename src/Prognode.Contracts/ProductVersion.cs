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
}
