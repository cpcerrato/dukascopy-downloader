using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DukascopyDownloader.Download;
using DukascopyDownloader.Core.Tests.Support;
using Xunit;

namespace DukascopyDownloader.Tests.Download;

public class Bi5VerifierTests
{
    [Fact]
    public async Task VerifyAsync_AllowsEmptyFiles()
    {
        var path = Path.GetTempFileName();
        try
        {
            await Bi5Verifier.VerifyAsync(path, CancellationToken.None);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task VerifyAsync_WithValidMinuteSample_Completes()
    {
        var path = Bi5TestSamples.WriteMinuteSample();
        try
        {
            await Bi5Verifier.VerifyAsync(path, CancellationToken.None);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
