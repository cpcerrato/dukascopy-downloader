namespace DukascopyDownloader.Generation;

internal sealed record TickRecord(
    DateTimeOffset TimestampUtc,
    decimal Ask,
    decimal Bid,
    double AskVolume,
    double BidVolume);

internal sealed record MinuteRecord(
    DateTimeOffset TimestampUtc,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    double Volume);

internal sealed record CandleRecord(
    DateTimeOffset LocalStart,
    decimal Open,
    decimal High,
    decimal Low,
    decimal Close,
    double Volume);
