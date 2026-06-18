using Anthropic.Models.Beta.Messages;

namespace ChatBot;

/// <summary>
/// The outcome of one beta (compaction) completion: the reply text and tool calls in
/// engine-domain terms, plus the raw assistant content blocks to round-trip back into history
/// (these preserve compaction and tool-use blocks the API needs on the next request).
/// </summary>
public sealed record BetaCompletionResult(
    string Text,
    IReadOnlyList<ToolCall> ToolCalls,
    IReadOnlyList<BetaContentBlockParam> AssistantContent,
    bool StoppedForToolUse,
    TokenUsage Usage);

/// <summary>
/// Seam over the beta (compaction) Messages.Create call. Abstracting it lets the compaction
/// engine's agentic tool loop be unit-tested with a scripted fake instead of a live API call.
/// </summary>
public interface IBetaCompletionClient
{
    Task<BetaCompletionResult> CreateAsync(MessageCreateParams parameters, CancellationToken cancellationToken);
}
