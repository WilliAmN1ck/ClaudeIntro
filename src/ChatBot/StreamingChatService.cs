using System.Runtime.CompilerServices;
using System.Text;
using Anthropic;
using Anthropic.Models.Messages;

namespace ChatBot;

/// <summary>
/// Default chat engine: streams replies token-by-token and manages context with a
/// simple count-based trim of the most recent messages.
/// </summary>
public sealed class StreamingChatService : IChatService
{
    private readonly AnthropicClient _client;
    private readonly IConversationStore _store;
    private readonly int _maxHistoryMessages;
    private readonly List<TextBlockParam> _systemBlocks;
    private readonly List<StoredTurn> _turns = new();

    public string Model { get; }
    public long MaxTokens { get; }
    public string SystemPrompt { get; }
    public IReadOnlyList<StoredTurn> History => _turns;

    public StreamingChatService(
        AnthropicClient client, ChatOptions options, string systemPrompt, IConversationStore store)
    {
        _client = client;
        _store = store;
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
        _turns.Add(new StoredTurn("user", userMessage));

        var parameters = new MessageCreateParams
        {
            Model = Model,
            MaxTokens = MaxTokens,
            System = _systemBlocks,
            Messages = BuildTrimmedMessages(),
        };

        var reply = new StringBuilder();
        await foreach (RawMessageStreamEvent streamEvent in
                       _client.Messages.CreateStreaming(parameters).WithCancellation(cancellationToken))
        {
            if (streamEvent.TryPickContentBlockDelta(out var delta) &&
                delta.Delta.TryPickText(out var text))
            {
                reply.Append(text.Text);
                yield return text.Text;
            }
        }

        _turns.Add(new StoredTurn("assistant", reply.ToString()));
        _store.Save(_turns);
    }

    // Builds the SDK message list from the text turns, keeping only the most recent
    // `maxHistoryMessages` and ensuring the result starts with a user message
    // (the API requires the first message to be from the user). 0 = no trim.
    private List<MessageParam> BuildTrimmedMessages()
    {
        var all = _turns.Select(t => t.ToMessage()).ToList();
        if (_maxHistoryMessages <= 0 || all.Count <= _maxHistoryMessages)
            return all;

        int start = all.Count - _maxHistoryMessages;
        while (start < all.Count && all[start].Role == Role.Assistant)
            start++;

        return all.GetRange(start, all.Count - start);
    }
}
