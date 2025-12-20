namespace DukascopyDownloader.Generation;

internal enum SpreadMode
{
    None = 0,
    FromTicks = 1,
    Fixed = 2
}

internal readonly record struct SpreadPlan(SpreadMode Mode, decimal? TickSize, int? FixedSpreadPoints)
{
    public static SpreadPlan None => new(SpreadMode.None, null, null);
    public static SpreadPlan FromTickSize(decimal tickSize) => new(SpreadMode.FromTicks, tickSize, null);
    public static SpreadPlan Fixed(int spreadPoints) => new(SpreadMode.Fixed, null, spreadPoints);
}
