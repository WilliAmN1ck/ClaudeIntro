namespace ChatBot;

/// <summary>
/// Estimates the US-dollar cost of a turn from its <see cref="TokenUsage"/> and the model's
/// <see cref="ModelPricing"/>. The four token categories are disjoint (uncached input,
/// output, cache write, cache read), so a rate-weighted sum is exact with no double-counting.
/// </summary>
public static class CostEstimator
{
    public static decimal Estimate(TokenUsage usage, ModelPricing pricing) =>
        (usage.InputTokens * pricing.InputPerMillion
         + usage.OutputTokens * pricing.OutputPerMillion
         + usage.CacheCreationTokens * pricing.CacheWritePerMillion
         + usage.CacheReadTokens * pricing.CacheReadPerMillion) / 1_000_000m;
}
