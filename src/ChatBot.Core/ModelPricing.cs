namespace ChatBot;

/// <summary>
/// Per-model token rates in US dollars per million tokens. Cache-write and cache-read rates
/// follow Anthropic's ephemeral (5-minute) cache pricing — 1.25× and 0.1× of the input rate —
/// matching the engine's <c>CacheControlEphemeral</c>. <c>init</c> properties so the built-in
/// table is immutable (instances are shared by reference) while still binding from configuration
/// (the <c>ChatBot:Pricing</c> section).
/// </summary>
public sealed record ModelPricing
{
    public decimal InputPerMillion { get; init; }
    public decimal OutputPerMillion { get; init; }
    public decimal CacheWritePerMillion { get; init; }
    public decimal CacheReadPerMillion { get; init; }
}
