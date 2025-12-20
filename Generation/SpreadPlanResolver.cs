using DukascopyDownloader.Download;
using DukascopyDownloader.Logging;

namespace DukascopyDownloader.Generation;

internal static class SpreadPlanResolver
{
    public static SpreadPlan Resolve(
        IReadOnlyList<TickRecord> ticks,
        GenerationOptions generation,
        DukascopyTimeframe timeframe,
        ConsoleLogger logger)
    {
        if (!generation.IncludeSpread && generation.Template != ExportTemplate.MetaTrader5)
        {
            return SpreadPlan.None;
        }

        if (generation.TickSize is not null)
        {
            if (ticks.Count == 0 && timeframe != DukascopyTimeframe.Tick)
            {
                throw new InvalidOperationException("Tick data required to compute spread with --tick-size but no ticks are available in cache.");
            }

            return SpreadPlan.FromTickSize(generation.TickSize.Value);
        }

        if (generation.InferTickSize)
        {
            if (ticks.Count == 0)
            {
                if (generation.SpreadPoints is not null)
                {
                    logger.Warn($"InferTickSize unavailable (no ticks in cache). Using fixed --spread-points={generation.SpreadPoints.Value}.");
                    return SpreadPlan.Fixed(generation.SpreadPoints.Value);
                }

                throw new InvalidOperationException("InferTickSize failed: no ticks available. Provide --tick-size (e.g. 0.00001) or --spread-points (e.g. 10).");
            }

            var inferred = SpreadCalculator.InferTickSize(ticks, generation.MinNonZeroDeltas, out var nonZero);
            if (inferred is null)
            {
                if (generation.SpreadPoints is not null)
                {
                    logger.Warn($"InferTickSize insufficient ({nonZero}/{generation.MinNonZeroDeltas}). Using fixed --spread-points={generation.SpreadPoints.Value}.");
                    return SpreadPlan.Fixed(generation.SpreadPoints.Value);
                }

                throw new InvalidOperationException($"InferTickSize failed: only {nonZero} non-zero deltas (min {generation.MinNonZeroDeltas}). Provide --tick-size (e.g. 0.00001) or --spread-points (e.g. 10).");
            }

            return SpreadPlan.FromTickSize(inferred.Value);
        }

        if (generation.SpreadPoints is not null)
        {
            return SpreadPlan.Fixed(generation.SpreadPoints.Value);
        }

        if (timeframe == DukascopyTimeframe.Tick)
        {
            return SpreadPlan.None;
        }

        throw new InvalidOperationException("Cannot export candles with SPREAD: provide --tick-size (e.g. 0.00001), --infer-tick-size, or --spread-points.");
    }
}
