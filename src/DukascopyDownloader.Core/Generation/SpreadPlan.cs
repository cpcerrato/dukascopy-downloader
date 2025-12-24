namespace DukascopyDownloader.Generation;

internal enum SpreadMode
{
    None = 0,
    FromTicks = 1,
    Fixed = 2
}

internal readonly record struct SpreadPlan(SpreadMode Mode, decimal? TickSize, int? FixedSpreadPoints)
{
    /// <summary>Represents the absence of spread calculation.</summary>
    public static SpreadPlan None => new(SpreadMode.None, null, null);

    /// <summary>Creates a spread plan that derives spread points from bid/ask deltas using the provided tick size.</summary>
    /// <param name="tickSize">Tick size/point value used to convert price deltas into spread points.</param>
    /// <returns>A spread plan configured for tick-based spread computation.</returns>
    public static SpreadPlan FromTickSize(decimal tickSize) => new(SpreadMode.FromTicks, tickSize, null);

    /// <summary>Creates a spread plan with a fixed spread in points for every candle.</summary>
    /// <param name="spreadPoints">Fixed spread value (points).</param>
    /// <returns>A spread plan configured to use a fixed spread.</returns>
    public static SpreadPlan Fixed(int spreadPoints) => new(SpreadMode.Fixed, null, spreadPoints);
}
