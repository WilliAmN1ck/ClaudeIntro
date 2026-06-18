namespace ChatBot;

/// <summary>
/// Per-model token rates in US dollars per million tokens. Cache-write and cache-read rates
/// follow Anthropic's ephemeral (5-minute) cache pricing — 1.25× and 0.1× of the input rate —
/// matching the engine's <c>CacheControlEphemeral</c>. Settable properties so the table can be
/// overridden from configuration (the <c>ChatBot:Pricing</c> section).
/// </summary>
public sealed record ModelPricing
{
    public decimal InputPerMillion { get; set; }
    public decimal OutputPerMillion { get; set; }
    public decimal CacheWritePerMillion { get; set; }
    public decimal CacheReadPerMillion { get; set; }
}
