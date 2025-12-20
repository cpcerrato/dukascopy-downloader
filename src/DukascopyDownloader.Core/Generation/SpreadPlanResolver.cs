using DukascopyDownloader.Download;

using Microsoft.Extensions.Logging;

namespace DukascopyDownloader.Generation;

internal static class SpreadPlanResolver
{
    /// <summary>
    /// Resolves how spreads will be produced for candle export: from explicit tick size, inferred tick size, or fixed spread points.
    /// Emits warnings/errors via logger when inference is insufficient.
    /// </summary>
    /// <param name="ticks">Ticks available for inference (may be empty).</param>
    /// <param name="generation">Generation options indicating tick-size/inference/fixed spread preferences.</param>
    /// <param name="timeframe">Target timeframe for export.</param>
    /// <param name="logger">Logger used to emit warnings or errors.</param>
    /// <returns>Spread plan describing mode, tick size (when applicable), and fixed spread fallback.</returns>
    public static SpreadPlan Resolve(
        IReadOnlyList<TickRecord> ticks,
        GenerationOptions generation,
        DukascopyTimeframe timeframe,
        ILogger logger)
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
                    logger.LogWarning("InferTickSize unavailable (no ticks in cache). Using fixed --spread-points={SpreadPoints}.", generation.SpreadPoints.Value);
                    return SpreadPlan.Fixed(generation.SpreadPoints.Value);
                }

                throw new InvalidOperationException("InferTickSize failed: no ticks available. Provide --tick-size (e.g. 0.00001) or --spread-points (e.g. 10).");
            }

            var inferred = SpreadCalculator.InferTickSize(ticks, generation.MinNonZeroDeltas, out var nonZero);
            if (inferred is null)
            {
                if (generation.SpreadPoints is not null)
                {
                    logger.LogWarning("InferTickSize insufficient ({NonZero}/{Min}). Using fixed --spread-points={SpreadPoints}.", nonZero, generation.MinNonZeroDeltas, generation.SpreadPoints.Value);
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
