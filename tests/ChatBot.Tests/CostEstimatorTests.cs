using ChatBot;
using Xunit;

namespace ChatBot.Tests;

public class CostEstimatorTests
{
    private static readonly ModelPricing Opus = new()
    {
        InputPerMillion = 5m,
        OutputPerMillion = 25m,
        CacheWritePerMillion = 6.25m,
        CacheReadPerMillion = 0.50m,
    };

    [Fact]
    public void Estimate_sums_all_token_categories_at_their_rates()
    {
        // 1M of each category: 5 + 25 + (cache_write) 6.25 + (cache_read) 0.50 = 36.75
        var usage = new TokenUsage(1_000_000, 1_000_000, CacheReadTokens: 1_000_000, CacheCreationTokens: 1_000_000);
        Assert.Equal(36.75m, CostEstimator.Estimate(usage, Opus));
    }

    [Fact]
    public void Estimate_scales_with_token_counts()
    {
        // 200k input * $5/M = $1.00; 40k output * $25/M = $1.00 => $2.00
        var usage = new TokenUsage(200_000, 40_000, 0, 0);
        Assert.Equal(2.00m, CostEstimator.Estimate(usage, Opus));
    }

    [Fact]
    public void Estimate_prices_cache_write_and_read_distinctly()
    {
        // cache_read 1M => $0.50, cache_write 1M => $6.25 => $6.75
        var usage = new TokenUsage(0, 0, CacheReadTokens: 1_000_000, CacheCreationTokens: 1_000_000);
        Assert.Equal(6.75m, CostEstimator.Estimate(usage, Opus));
    }

    [Fact]
    public void Estimate_zero_usage_is_zero() =>
        Assert.Equal(0m, CostEstimator.Estimate(default, Opus));
}
