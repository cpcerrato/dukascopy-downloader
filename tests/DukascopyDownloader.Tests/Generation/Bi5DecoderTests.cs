using System.Buffers.Binary;
using System.IO;
using DukascopyDownloader.Generation;
using DukascopyDownloader.Tests.Support;
using Xunit;

namespace DukascopyDownloader.Tests.Generation;

public class Bi5DecoderTests
{
    [Fact]
    public void ParseTicks_DecodesRawRecords()
    {
        var start = new DateTimeOffset(2025, 1, 14, 0, 0, 0, TimeSpan.Zero);
        var stream = BuildTickStream((0, 123450, 123440, 1.5f, 1.0f));

        var ticks = Bi5Decoder.ParseTicks(stream, start);

        var tick = Assert.Single(ticks);
        Assert.Equal(start, tick.TimestampUtc);
        Assert.Equal(1.2345m, tick.Ask);
        Assert.Equal(1.2344m, tick.Bid);
        Assert.Equal(1.5, tick.AskVolume, 3);
        Assert.Equal(1.0, tick.BidVolume, 3);
    }

    [Fact]
    public void ParseMinutes_DecodesRawRecords()
    {
        var start = new DateTimeOffset(2025, 1, 14, 0, 0, 0, TimeSpan.Zero);
        var stream = BuildMinuteStream((60, 120000, 125000, 118000, 123000, 10f));

        var candles = Bi5Decoder.ParseMinutes(stream, start);

        var candle = Assert.Single(candles);
        Assert.Equal(start.AddMinutes(1), candle.TimestampUtc);
        Assert.Equal(1.2m, candle.Open);
        Assert.Equal(1.25m, candle.Close);
        Assert.Equal(1.18m, candle.Low);
        Assert.Equal(1.23m, candle.High);
        Assert.Equal(10, candle.Volume, 3);
    }

    [Fact]
    public void ReadTicks_WithLzmaPayload_DecodesRecords()
    {
        var path = Bi5TestSamples.WriteTickSample();
        var start = new DateTimeOffset(2025, 1, 14, 0, 0, 0, TimeSpan.Zero);
        try
        {
            var ticks = Bi5Decoder.ReadTicks(path, start);
            Assert.Equal(2, ticks.Count);

            var first = ticks[0];
            Assert.Equal(start, first.TimestampUtc);
            Assert.Equal(1.2345m, first.Ask);
            Assert.Equal(1.234m, first.Bid);
            Assert.Equal(1.5, first.AskVolume, 3);

            var second = ticks[1];
            Assert.Equal(start.AddMilliseconds(500), second.TimestampUtc);
            Assert.Equal(1.235m, second.Ask);
            Assert.Equal(1.2348m, second.Bid);
            Assert.Equal(2.0, second.AskVolume, 3);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadMinutes_WithLzmaPayload_DecodesRecords()
    {
        var path = Bi5TestSamples.WriteMinuteSample();
        var start = new DateTimeOffset(2025, 1, 14, 0, 0, 0, TimeSpan.Zero);
        try
        {
            var minutes = Bi5Decoder.ReadMinutes(path, start);
            Assert.Equal(2, minutes.Count);

            var first = minutes[0];
            Assert.Equal(start, first.TimestampUtc);
            Assert.Equal(1.2m, first.Open);
            Assert.Equal(1.3m, first.High);
            Assert.Equal(1.18m, first.Low);
            Assert.Equal(1.25m, first.Close);
            Assert.Equal(5, first.Volume, 3);

            var second = minutes[1];
            Assert.Equal(start.AddMinutes(1), second.TimestampUtc);
            Assert.Equal(1.3m, second.Open);
            Assert.Equal(1.34m, second.High);
            Assert.Equal(1.26m, second.Low);
            Assert.Equal(1.35m, second.Close);
            Assert.Equal(6.5, second.Volume, 3);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadTicks_WhenFileEmpty_ReturnsEmpty()
    {
        var path = Path.GetTempFileName();
        try
        {
            var ticks = Bi5Decoder.ReadTicks(path, DateTimeOffset.UtcNow);
            Assert.Empty(ticks);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadMinutes_WhenFileEmpty_ReturnsEmpty()
    {
        var path = Path.GetTempFileName();
        try
        {
            var candles = Bi5Decoder.ReadMinutes(path, DateTimeOffset.UtcNow);
            Assert.Empty(candles);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static MemoryStream BuildTickStream(params (int MsOffset, int AskRaw, int BidRaw, float AskVolume, float BidVolume)[] records)
    {
        var stream = new MemoryStream();
        foreach (var record in records)
        {
            WriteInt(stream, record.MsOffset);
            WriteInt(stream, record.AskRaw);
            WriteInt(stream, record.BidRaw);
            WriteInt(stream, BitConverter.SingleToInt32Bits(record.AskVolume));
            WriteInt(stream, BitConverter.SingleToInt32Bits(record.BidVolume));
        }
        stream.Position = 0;
        return stream;
    }

    private static MemoryStream BuildMinuteStream(params (int Seconds, int OpenRaw, int CloseRaw, int LowRaw, int HighRaw, float Volume)[] records)
    {
        var stream = new MemoryStream();
        foreach (var record in records)
        {
            WriteInt(stream, record.Seconds);
            WriteInt(stream, record.OpenRaw);
            WriteInt(stream, record.CloseRaw);
            WriteInt(stream, record.LowRaw);
            WriteInt(stream, record.HighRaw);
            WriteInt(stream, BitConverter.SingleToInt32Bits(record.Volume));
        }
        stream.Position = 0;
        return stream;
    }

    private static void WriteInt(Stream stream, int value)
    {
        Span<byte> buffer = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(buffer, value);
        stream.Write(buffer);
    }
}
