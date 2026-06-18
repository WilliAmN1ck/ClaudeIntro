using ChatBot;
using Xunit;

namespace ChatBot.Tests;

public class ModelPricesTests
{
    [Fact]
    public void For_known_model_returns_builtin_rates()
    {
        ModelPricing? p = ModelPrices.For("claude-opus-4-8");

        Assert.NotNull(p);
        Assert.Equal(5m, p!.InputPerMillion);
        Assert.Equal(25m, p.OutputPerMillion);
        Assert.Equal(6.25m, p.CacheWritePerMillion);
        Assert.Equal(0.50m, p.CacheReadPerMillion);
    }

    [Theory]
    [InlineData("claude-sonnet-4-6", 3)]
    [InlineData("claude-haiku-4-5", 1)]
    [InlineData("claude-fable-5", 10)]
    public void For_covers_other_known_models(string id, int expectedInput) =>
        Assert.Equal(expectedInput, ModelPrices.For(id)!.InputPerMillion);

    [Fact]
    public void For_is_case_insensitive() =>
        Assert.NotNull(ModelPrices.For("CLAUDE-OPUS-4-8"));

    [Fact]
    public void For_unknown_model_returns_null() =>
        Assert.Null(ModelPrices.For("gpt-9000"));

    [Fact]
    public void For_blank_model_returns_null() =>
        Assert.Null(ModelPrices.For("  "));

    [Fact]
    public void For_prefers_config_override()
    {
        var overrides = new Dictionary<string, ModelPricing>
        {
            ["claude-opus-4-8"] = new() { InputPerMillion = 99m },
        };
        Assert.Equal(99m, ModelPrices.For("claude-opus-4-8", overrides)!.InputPerMillion);
    }

    [Fact]
    public void For_override_matches_case_insensitively()
    {
        var overrides = new Dictionary<string, ModelPricing>
        {
            ["Claude-Opus-4-8"] = new() { InputPerMillion = 42m },
        };
        Assert.Equal(42m, ModelPrices.For("claude-opus-4-8", overrides)!.InputPerMillion);
    }

    [Fact]
    public void For_override_does_not_mutate_builtin_table()
    {
        var overrides = new Dictionary<string, ModelPricing>
        {
            ["claude-opus-4-8"] = new() { InputPerMillion = 999m },
        };
        _ = ModelPrices.For("claude-opus-4-8", overrides);

        // A later lookup without overrides still returns the built-in rate.
        Assert.Equal(5m, ModelPrices.For("claude-opus-4-8")!.InputPerMillion);
    }
}
