using System.Text.Json;
using Anthropic.Models.Messages;

namespace ChatBot;

/// <summary>A tool the model asked to call, in engine-domain terms.</summary>
public sealed record ToolCall(string Id, string Name, IReadOnlyDictionary<string, JsonElement> Input);

/// <summary>The outcome of one streamed completion, after the text has been consumed.</summary>
public sealed record CompletionResult(
    bool StoppedForToolUse,
    IReadOnlyList<ToolCall> ToolCalls,
    TokenUsage Usage);

/// <summary>
/// One in-progress streamed completion: enumerate <see cref="ReadTextAsync"/> for the
/// reply text, then call <see cref="GetResult"/> for the tool calls / usage / stop reason.
/// </summary>
public interface ICompletionStream
{
    IAsyncEnumerable<string> ReadTextAsync(CancellationToken cancellationToken);
    CompletionResult GetResult();
}

/// <summary>
/// Seam over the Messages streaming API. Abstracting it lets the engine's agentic
/// loop be unit-tested with a scripted fake instead of a live API call.
/// </summary>
public interface IChatCompletionClient
{
    ICompletionStream Stream(MessageCreateParams parameters, CancellationToken cancellationToken);
}
