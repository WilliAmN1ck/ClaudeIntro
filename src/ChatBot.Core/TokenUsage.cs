namespace ChatBot;

/// <summary>Token counts reported by the API for a single turn.</summary>
public readonly record struct TokenUsage(
    long InputTokens,
    long OutputTokens,
    long CacheReadTokens,
    long CacheCreationTokens)
{
    /// <summary>Sum of all token categories.</summary>
    public long TotalTokens => InputTokens + OutputTokens + CacheReadTokens + CacheCreationTokens;

    public override string ToString() =>
        $"in={InputTokens}, out={OutputTokens}, cache_read={CacheReadTokens}, cache_write={CacheCreationTokens}";
}
