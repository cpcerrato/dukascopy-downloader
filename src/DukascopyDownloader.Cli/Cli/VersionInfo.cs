using System.Reflection;

namespace DukascopyDownloader.Cli;

internal static class VersionInfo
{
    private static string? _cached;

    public static string GetVersion()
    {
        if (_cached is not null)
        {
            return _cached;
        }

        var assembly = Assembly.GetEntryAssembly() ?? typeof(VersionInfo).Assembly;
        _cached =
            assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0";

        return _cached;
    }
}
