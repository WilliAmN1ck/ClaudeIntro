using Anthropic.Models.Beta.Messages;
using ChatBot;

namespace ChatBot.Tests;

/// <summary>A scripted <see cref="IBetaCompletionClient"/> for testing the compaction tool loop.</summary>
internal sealed class FakeBetaCompletionClient : IBetaCompletionClient
{
    private readonly Queue<BetaCompletionResult> _results;

    public int CallCount { get; private set; }

    public FakeBetaCompletionClient(params BetaCompletionResult[] results) =>
        _results = new Queue<BetaCompletionResult>(results);

    public Task<BetaCompletionResult> CreateAsync(
        MessageCreateParams parameters, CancellationToken cancellationToken)
    {
        CallCount++;
        return Task.FromResult(_results.Dequeue());
    }
}

internal static class BetaFakeTurn
{
    public static BetaCompletionResult Reply(string text, long inTok = 10, long outTok = 5) =>
        new(text,
            Array.Empty<ToolCall>(),
            new BetaContentBlockParam[] { new BetaTextBlockParam { Text = text } },
            StoppedForToolUse: false,
            new TokenUsage(inTok, outTok, 0, 0));

    public static BetaCompletionResult ToolUse(string text, ToolCall call, long inTok = 10, long outTok = 5) =>
        new(text,
            new[] { call },
            ToolUseBlocks(call),
            StoppedForToolUse: true,
            new TokenUsage(inTok, outTok, 0, 0));

    public static BetaCompletionResult ToolUses(string text, params ToolCall[] calls) =>
        new(text,
            calls,
            ToolUseBlocks(calls),
            StoppedForToolUse: true,
            new TokenUsage(10, 5, 0, 0));

    private static IReadOnlyList<BetaContentBlockParam> ToolUseBlocks(params ToolCall[] calls) =>
        calls.Select(c => (BetaContentBlockParam)new BetaToolUseBlockParam
        {
            ID = c.Id,
            Name = c.Name,
            Input = c.Input,
        }).ToList();
}

/// <summary>A beta completion client that always throws, for commit-on-failure tests.</summary>
internal sealed class ThrowingBetaCompletionClient : IBetaCompletionClient
{
    public Task<BetaCompletionResult> CreateAsync(
        MessageCreateParams parameters, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("boom");
}
