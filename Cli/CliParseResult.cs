namespace DukascopyDownloader.Cli;

internal sealed class CliParseResult
{
    public CliParseResult(bool showHelp, bool showVersion, AppOptions? options, string? error)
    {
        ShowHelp = showHelp;
        ShowVersion = showVersion;
        Options = options;
        Error = error;
    }

    public bool ShowHelp { get; }
    public bool ShowVersion { get; }
    public AppOptions? Options { get; }
    public string? Error { get; }
    public bool IsValid => Options != null && Error is null;
}
