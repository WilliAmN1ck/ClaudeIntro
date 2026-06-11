using System.Runtime.CompilerServices;
using System.Text;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Logging;

namespace ChatBot;

/// <summary>
/// Default chat engine: streams replies token-by-token, manages context with a
/// simple count-based trim, and runs an agentic tool loop when tools are registered.
/// Talks to the API through <see cref="IChatCompletionClient"/> so the loop is testable.
/// </summary>
public sealed class StreamingChatService : IChatService
{
    private readonly IChatCompletionClient _completion;
    private readonly IConversationStore _store;
    private readonly ToolInvoker _tools;
    private readonly ILogger<StreamingChatService> _logger;
    private readonly int _maxHistoryMessages;
    private readonly List<TextBlockParam> _systemBlocks;
    private readonly List<StoredTurn> _turns = new();

    public string Model { get; }
    public long MaxTokens { get; }
    public string SystemPrompt { get; }
    public IReadOnlyList<StoredTurn> History => _turns;
    public TokenUsage? LastTurnUsage { get; private set; }

    public StreamingChatService(
        IChatCompletionClient completion,
        ChatOptions options,
        string systemPrompt,
        IConversationStore store,
        IEnumerable<StoredTurn> seed,
        IEnumerable<IChatTool> tools,
        ILogger<StreamingChatService> logger)
    {
        _completion = completion;
        _store = store;
        _tools = new ToolInvoker(tools);
        _logger = logger;
        Model = string.IsNullOrWhiteSpace(options.Model) ? "claude-opus-4-8" : options.Model.Trim();
        MaxTokens = options.MaxTokens >= 1 ? options.MaxTokens : 4096;
        _maxHistoryMessages = options.MaxHistoryMessages >= 0 ? options.MaxHistoryMessages : 40;
        SystemPrompt = systemPrompt;

        // Cache the system prompt so repeated requests reuse its prefix.
        _systemBlocks = new List<TextBlockParam>
        {
            new() { Text = systemPrompt, CacheControl = new CacheControlEphemeral() },
        };

        _turns.AddRange(seed);
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        _turns.Clear();
        await _store.ClearAsync(cancellationToken);
    }

    public async IAsyncEnumerable<string> SendAsync(
        string userMessage, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Build the in-call conversation from existing turns plus the new (not-yet-
        // committed) user message, trimmed to the recent window. Tool-use/tool-result
        // turns added during the loop are in-call only; persistence stays text-only.
        var conversation = HistoryTrimmer
            .Trim(new List<StoredTurn>(_turns) { new("user", userMessage) }, _maxHistoryMessages)
            .Select(t => t.ToMessage())
            .ToList();

        var reply = new StringBuilder();
        long inputTokens = 0, outputTokens = 0, cacheRead = 0, cacheCreation = 0;

        // Agentic loop: stream a turn; if the model asked for tools, run them and continue.
        while (true)
        {
            var parameters = new MessageCreateParams
            {
                Model = Model,
                MaxTokens = MaxTokens,
                System = _systemBlocks,
                Messages = conversation,
                Tools = _tools.Tools.Count > 0 ? _tools.ToolUnions.ToList() : null,
            };

            _logger.LogInformation("Streaming request: {MessageCount} message(s) in context", conversation.Count);

            ICompletionStream stream = _completion.Stream(parameters, cancellationToken);

            var turnText = new StringBuilder();
            await foreach (string delta in stream.ReadTextAsync(cancellationToken))
            {
                turnText.Append(delta);
                reply.Append(delta);
                yield return delta;
            }

            CompletionResult result = stream.GetResult();
            inputTokens += result.Usage.InputTokens;
            outputTokens += result.Usage.OutputTokens;
            cacheRead += result.Usage.CacheReadTokens;
            cacheCreation += result.Usage.CacheCreationTokens;

            // Echo the assistant turn (text + any tool calls) back into the conversation.
            var assistantContent = new List<ContentBlockParam>();
            if (turnText.Length > 0)
                assistantContent.Add(new TextBlockParam { Text = turnText.ToString() });
            foreach (ToolCall call in result.ToolCalls)
                assistantContent.Add(new ToolUseBlockParam { ID = call.Id, Name = call.Name, Input = call.Input });
            conversation.Add(new MessageParam { Role = Role.Assistant, Content = assistantContent });

            if (!result.StoppedForToolUse || result.ToolCalls.Count == 0)
                break;

            var results = new List<ContentBlockParam>();
            foreach (ToolCall call in result.ToolCalls)
            {
                yield return $"\n[tool: {call.Name}]\n";
                _logger.LogInformation("Executing tool {Tool}", call.Name);
                string toolResult = await _tools.InvokeAsync(call.Name, call.Input, cancellationToken);
                results.Add(new ToolResultBlockParam { ToolUseID = call.Id, Content = toolResult });
            }
            conversation.Add(new MessageParam { Role = Role.User, Content = results });
        }

        // Commit only on success: a cancelled or failed turn leaves history untouched.
        _turns.Add(new StoredTurn("user", userMessage));
        _turns.Add(new StoredTurn("assistant", reply.ToString()));
        LastTurnUsage = new TokenUsage(inputTokens, outputTokens, cacheRead, cacheCreation);
        await _store.SaveAsync(_turns, cancellationToken);

        _logger.LogInformation("Turn complete: {Usage}", LastTurnUsage);
    }
}
