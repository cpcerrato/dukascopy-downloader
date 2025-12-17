namespace DukascopyDownloader.Cli;

internal sealed class CliParseResult
{
    public CliParseResult(bool showHelp, AppOptions? options, string? error)
    {
        ShowHelp = showHelp;
        Options = options;
        Error = error;
    }

    public bool ShowHelp { get; }
    public AppOptions? Options { get; }
    public string? Error { get; }
    public bool IsValid => Options != null && Error is null;
}
