using DukascopyDownloader.Download;
using DukascopyDownloader.Generation;

namespace DukascopyDownloader.Cli;

internal sealed record AppOptions(DownloadOptions Download, GenerationOptions Generation, bool Verbose);
