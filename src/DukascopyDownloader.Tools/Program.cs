using System.CommandLine;
using System.CommandLine.Invocation;
using System.CommandLine.Parsing;
using DukascopyDownloader.Tools;

internal static class Program
{
    public static Task<int> Main(string[] args)
    {
        var root = new RootCommand("Repository tooling for instruments docs.");

        var generate = new Command("generate-instruments", "Fetch Dukascopy instruments, merge dukascopy-node metadata, snapshot them, and regenerate docs/instruments.md.");
        var rootOption = new Option<string?>(new[] { "--root" }, "Root directory (defaults to cwd).");
        var urlOption = new Option<string?>(new[] { "--url" }, $"Instruments JSONP endpoint (default: {GenerateInstrumentsCommand.DefaultUrl}).");
        var outOption = new Option<string?>(new[] { "--out" }, "Output markdown path (default docs/instruments.md).");
        generate.AddOption(rootOption);
        generate.AddOption(urlOption);
        generate.AddOption(outOption);
        generate.SetHandler(async (InvocationContext ctx) =>
        {
            var parse = ctx.ParseResult;
            var rootDir = parse.GetValueForOption(rootOption);
            var url = parse.GetValueForOption(urlOption);
            var outPath = parse.GetValueForOption(outOption);
            ctx.ExitCode = await GenerateInstrumentsCommand.RunAsync(rootDir, url, outPath);
        });

        root.AddCommand(generate);

        return root.InvokeAsync(args);
    }
}
