using System.Buffers;

namespace DukascopyDownloader.Download;

internal static class Bi5Verifier
{
    /// <summary>
    /// Validates a BI5 file by fully decoding its LZMA payload. Throws on corruption.
    /// </summary>
    /// <param name="path">Path to the BI5 file on disk.</param>
    /// <param name="cancellationToken">Cancellation token for the verification task.</param>
    /// <exception cref="InvalidDataException">Thrown when the BI5 header/payload is invalid.</exception>
    public static async Task VerifyAsync(string path, CancellationToken cancellationToken)
    {
        await Task.Run(() => Verify(path), cancellationToken);
    }

    private static void Verify(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (fs.Length == 0)
        {
            return;
        }

        if (fs.Length < 13)
        {
            throw new InvalidDataException("BI5 file is too small to contain a valid LZMA header.");
        }

        Span<byte> header = stackalloc byte[13];
        fs.ReadExactly(header);
        var props = header[..5].ToArray();
        var uncompressedSize = BitConverter.ToInt64(header[5..]);
        var compressedSize = fs.Length - fs.Position;

        using var lzma = new SharpCompress.Compressors.LZMA.LzmaStream(props, fs, compressedSize, uncompressedSize);
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            while (lzma.Read(buffer, 0, buffer.Length) > 0)
            {
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}
