using System.Buffers.Binary;

namespace DukascopyDownloader.Generation;

/// <summary>
/// Decodes Dukascopy BI5 files into typed records.
/// </summary>
internal static class Bi5Decoder
{
    private const int TickRecordSize = 20;
    private const int MinuteRecordSize = 24;
    private const decimal PriceScale = 100000m;
    private const string InvalidHeaderMessage = "BI5 header is invalid or truncated.";

    /// <summary>
    /// Decodes a BI5 tick file into timestamped tick records using the provided slice start as the anchor.
    /// </summary>
    /// <param name="path">Path to the BI5 tick file.</param>
    /// <param name="sliceStart">UTC start of the slice (hour-aligned) used to compute timestamps.</param>
    /// <returns>List of decoded tick records.</returns>
    /// <summary>
    /// Reads a tick BI5 slice and returns the contained tick records.
    /// </summary>
    /// <param name="path">Absolute path to the BI5 file.</param>
    /// <param name="sliceStart">UTC slice start used to reconstruct timestamps.</param>
    /// <returns>All ticks in the slice with reconstructed UTC timestamps.</returns>
    /// <exception cref="InvalidDataException">Thrown when the BI5 payload is malformed.</exception>
    public static IReadOnlyList<TickRecord> ReadTicks(string path, DateTimeOffset sliceStart)
    {
        using var fs = File.OpenRead(path);
        if (fs.Length == 0)
        {
            return Array.Empty<TickRecord>();
        }

        using var reader = new BinaryReader(fs);
        var props = reader.ReadBytes(5);
        var uncompressedSize = reader.ReadInt64();
        var compressedSize = fs.Length - fs.Position;
        using var lzma = new SharpCompress.Compressors.LZMA.LzmaStream(props, fs, compressedSize, uncompressedSize);
        return ParseTicks(lzma, sliceStart);
    }

    /// <summary>
    /// Reads a candle BI5 slice (m1/h1/d1) and returns the contained bars.
    /// </summary>
    /// <param name="path">Absolute path to the BI5 file.</param>
    /// <param name="sliceStart">UTC slice start used to reconstruct timestamps.</param>
    /// <returns>All candles in the slice with reconstructed UTC timestamps.</returns>
    /// <exception cref="InvalidDataException">Thrown when the BI5 payload is malformed.</exception>
    public static IReadOnlyList<MinuteRecord> ReadCandles(string path, DateTimeOffset sliceStart)
    {
        using var fs = File.OpenRead(path);
        if (fs.Length == 0)
        {
            return Array.Empty<MinuteRecord>();
        }

        if (fs.Length < 13)
        {
            throw new InvalidDataException(InvalidHeaderMessage);
        }

        Span<byte> header = stackalloc byte[13];
        fs.ReadExactly(header);
        var props = header[..5].ToArray();
        var uncompressedSize = BitConverter.ToInt64(header[5..]);
        var compressedSize = fs.Length - fs.Position;
        using var lzma = new SharpCompress.Compressors.LZMA.LzmaStream(props, fs, compressedSize, uncompressedSize);
        return ParseCandles(lzma, sliceStart);
    }

    /// <summary>
    /// Decodes a BI5 minute file into minute records using the provided slice start as the anchor.
    /// </summary>
    /// <param name="path">Path to the BI5 minute file.</param>
    /// <param name="sliceStart">UTC start of the slice (day-aligned) used to compute timestamps.</param>
    /// <returns>List of decoded minute records.</returns>
    /// <exception cref="InvalidDataException">Thrown when the BI5 payload is malformed.</exception>
    public static IReadOnlyList<MinuteRecord> ReadMinutes(string path, DateTimeOffset sliceStart) =>
        ReadCandles(path, sliceStart);

    internal static IReadOnlyList<TickRecord> ParseTicks(Stream stream, DateTimeOffset sliceStart)
    {
        var ticks = new List<TickRecord>();
        var buffer = new byte[TickRecordSize];
        while (ReadExactly(stream, buffer))
        {
            var msOffset = BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(0, 4));
            var askRaw = BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(4, 4));
            var bidRaw = BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(8, 4));
            var askVol = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(12, 4)));
            var bidVol = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(16, 4)));

            var timestamp = sliceStart.AddMilliseconds(msOffset);
            ticks.Add(new TickRecord(
                timestamp,
                askRaw / PriceScale,
                bidRaw / PriceScale,
                askVol,
                bidVol));
        }

        return ticks;
    }

    internal static IReadOnlyList<MinuteRecord> ParseCandles(Stream stream, DateTimeOffset sliceStart)
    {
        var minutes = new List<MinuteRecord>();
        var buffer = new byte[MinuteRecordSize];
        while (ReadExactly(stream, buffer))
        {
            var secOffset = BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(0, 4));
            var openRaw = BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(4, 4));
            var closeRaw = BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(8, 4));
            var lowRaw = BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(12, 4));
            var highRaw = BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(16, 4));
            var volume = BitConverter.Int32BitsToSingle(BinaryPrimitives.ReadInt32BigEndian(buffer.AsSpan(20, 4)));

            var timestamp = sliceStart.AddSeconds(secOffset);
            minutes.Add(new MinuteRecord(
                timestamp,
                openRaw / PriceScale,
                highRaw / PriceScale,
                lowRaw / PriceScale,
                closeRaw / PriceScale,
                volume));
        }

        return minutes;
    }

    private static bool ReadExactly(Stream stream, byte[] buffer)
    {
        var read = 0;
        while (read < buffer.Length)
        {
            var r = stream.Read(buffer, read, buffer.Length - read);
            if (r == 0)
            {
                return false;
            }
            read += r;
        }
        return true;
    }
}
