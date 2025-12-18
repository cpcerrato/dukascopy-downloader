using System;
using System.IO;

namespace DukascopyDownloader.Tests.Support;

internal static class Bi5TestSamples
{
    private const string TickSampleBase64 =
        "XQAAgAD//////////wAAaP64YBgsStm33mwEmP0sGg0hHZjq20vG9SxXTvOKtHlO/SZUQA==";

    private const string MinuteSampleBase64 =
        "XQAAgAD//////////wAAaP62tjvRAqvsTOcdpKlWEDX+Vi6cvRaDQ1jbjeUtGEt8f1THyIs03n//tRiAAA==";

    public static string WriteTickSample()
    {
        return WriteSample(TickSampleBase64);
    }

    public static string WriteMinuteSample()
    {
        return WriteSample(MinuteSampleBase64);
    }

    private static string WriteSample(string base64)
    {
        var path = Path.Combine(Path.GetTempPath(), $"bi5-sample-{Guid.NewGuid():N}.bi5");
        File.WriteAllBytes(path, Convert.FromBase64String(base64));
        return path;
    }

    public static byte[] TickBytes => Convert.FromBase64String(TickSampleBase64);
}
