using System.Runtime.CompilerServices;
using System.Text;
using Anthropic;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Logging;

namespace ChatBot;

/// <summary>
/// Default chat engine: streams replies token-by-token and manages context with a
/// simple count-based trim of the most recent messages.
/// </summary>
public sealed class StreamingChatService : IChatService
{
    private readonly AnthropicClient _client;
    private readonly IConversationStore _store;
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
        AnthropicClient client,
        ChatOptions options,
        string systemPrompt,
        IConversationStore store,
        ILogger<StreamingChatService> logger)
    {
        _client = client;
        _store = store;
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

        _turns.AddRange(store.Load());
    }

    public void Clear()
    {
        _turns.Clear();
        _store.Clear();
    }

    public async IAsyncEnumerable<string> SendAsync(
        string userMessage, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        // Build the request from existing turns plus the new (not-yet-committed) user
        // message, trimmed to the recent window.
        var pending = new List<StoredTurn>(_turns) { new("user", userMessage) };
        var messages = HistoryTrimmer.Trim(pending, _maxHistoryMessages)
            .Select(t => t.ToMessage())
            .ToList();

        var parameters = new MessageCreateParams
        {
            Model = Model,
            MaxTokens = MaxTokens,
            System = _systemBlocks,
            Messages = messages,
        };

        _logger.LogInformation("Streaming request: {MessageCount} message(s) in context", messages.Count);

        var reply = new StringBuilder();
        long inputTokens = 0, outputTokens = 0, cacheRead = 0, cacheCreation = 0;

        await foreach (RawMessageStreamEvent streamEvent in
                       _client.Messages.CreateStreaming(parameters).WithCancellation(cancellationToken))
        {
            if (streamEvent.TryPickContentBlockDelta(out var delta) &&
                delta.Delta.TryPickText(out var text))
            {
                reply.Append(text.Text);
                yield return text.Text;
            }
            else if (streamEvent.TryPickStart(out var start))
            {
                inputTokens = start.Message.Usage.InputTokens;
                cacheRead = start.Message.Usage.CacheReadInputTokens ?? 0;
                cacheCreation = start.Message.Usage.CacheCreationInputTokens ?? 0;
            }
            else if (streamEvent.TryPickDelta(out var messageDelta) && messageDelta.Usage is { } deltaUsage)
            {
                outputTokens = deltaUsage.OutputTokens;
            }
        }

        // Commit only on success: a cancelled or failed turn leaves history untouched.
        _turns.Add(new StoredTurn("user", userMessage));
        _turns.Add(new StoredTurn("assistant", reply.ToString()));
        LastTurnUsage = new TokenUsage(inputTokens, outputTokens, cacheRead, cacheCreation);
        _store.Save(_turns);

        _logger.LogInformation("Turn complete: {Usage}", LastTurnUsage);
    }
}
