using System.Buffers.Binary;

namespace DukascopyDownloader.Generation;

internal static class Bi5Decoder
{
    private const int TickRecordSize = 20;
    private const int MinuteRecordSize = 24;
    private const decimal PriceScale = 100000m;

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

    public static IReadOnlyList<MinuteRecord> ReadMinutes(string path, DateTimeOffset sliceStart)
    {
        using var fs = File.OpenRead(path);
        if (fs.Length == 0)
        {
            return Array.Empty<MinuteRecord>();
        }

        using var reader = new BinaryReader(fs);
        var props = reader.ReadBytes(5);
        var uncompressedSize = reader.ReadInt64();
        var compressedSize = fs.Length - fs.Position;
        using var lzma = new SharpCompress.Compressors.LZMA.LzmaStream(props, fs, compressedSize, uncompressedSize);
        return ParseMinutes(lzma, sliceStart);
    }

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

    internal static IReadOnlyList<MinuteRecord> ParseMinutes(Stream stream, DateTimeOffset sliceStart)
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
