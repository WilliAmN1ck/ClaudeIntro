namespace ChatBot;

/// <summary>
/// Built-in token pricing for known Claude models (USD per million tokens — Anthropic list
/// prices as of 2026-06). Callers can override or extend these via the <c>ChatBot:Pricing</c>
/// configuration section so prices can be updated without recompiling.
/// </summary>
public static class ModelPrices
{
    private static ModelPricing Opus() => new()
    {
        InputPerMillion = 5m,
        OutputPerMillion = 25m,
        CacheWritePerMillion = 6.25m,
        CacheReadPerMillion = 0.50m,
    };

    private static readonly IReadOnlyDictionary<string, ModelPricing> BuiltIn =
        new Dictionary<string, ModelPricing>(StringComparer.OrdinalIgnoreCase)
        {
            ["claude-opus-4-8"] = Opus(),
            ["claude-opus-4-7"] = Opus(),
            ["claude-opus-4-6"] = Opus(),
            ["claude-sonnet-4-6"] = new()
            {
                InputPerMillion = 3m,
                OutputPerMillion = 15m,
                CacheWritePerMillion = 3.75m,
                CacheReadPerMillion = 0.30m,
            },
            ["claude-haiku-4-5"] = new()
            {
                InputPerMillion = 1m,
                OutputPerMillion = 5m,
                CacheWritePerMillion = 1.25m,
                CacheReadPerMillion = 0.10m,
            },
            ["claude-fable-5"] = new()
            {
                InputPerMillion = 10m,
                OutputPerMillion = 50m,
                CacheWritePerMillion = 12.50m,
                CacheReadPerMillion = 1.00m,
            },
        };

    /// <summary>
    /// Returns the pricing for <paramref name="modelId"/>, preferring <paramref name="overrides"/>
    /// (from configuration) over the built-in table. Returns <c>null</c> when the model is unknown,
    /// so callers fall back to showing tokens without a cost. Matching is case-insensitive.
    /// </summary>
    public static ModelPricing? For(string modelId, IReadOnlyDictionary<string, ModelPricing>? overrides = null)
    {
        if (string.IsNullOrWhiteSpace(modelId))
            return null;

        string id = modelId.Trim();

        if (overrides is not null)
        {
            // Config-bound dictionaries are ordinal by default; match case-insensitively here.
            foreach (KeyValuePair<string, ModelPricing> entry in overrides)
                if (string.Equals(entry.Key, id, StringComparison.OrdinalIgnoreCase))
                    return entry.Value;
        }

        return BuiltIn.TryGetValue(id, out ModelPricing? pricing) ? pricing : null;
    }
}
