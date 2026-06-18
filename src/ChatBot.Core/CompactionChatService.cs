using System.Runtime.CompilerServices;
using System.Text;
using Anthropic;
using Anthropic.Models.Beta.Messages;
using Microsoft.Extensions.Logging;

namespace ChatBot;

/// <summary>
/// Chat engine that uses beta server-side compaction (<c>compact-2026-01-12</c>):
/// the API summarizes earlier turns as context grows. Non-streaming — the reply is
/// yielded as a single chunk. Compaction blocks in the response are preserved
/// verbatim in history, so the full content is round-tripped each turn.
/// </summary>
public sealed class CompactionChatService : IChatService
{
    private readonly AnthropicClient _client;
    private readonly IConversationStore _store;
    private readonly ILogger<CompactionChatService> _logger;
    private readonly string _system;
    private readonly List<BetaMessageParam> _betaHistory = new();
    private readonly List<StoredTurn> _turns = new();

    public string ConversationId { get; }
    public string Model { get; }
    public long MaxTokens { get; }
    public string SystemPrompt { get; }
    public IReadOnlyList<StoredTurn> History => _turns;
    public TokenUsage? LastTurnUsage { get; private set; }

    public CompactionChatService(
        AnthropicClient client,
        ChatOptions options,
        string systemPrompt,
        string conversationId,
        IConversationStore store,
        IEnumerable<StoredTurn> seed,
        ILogger<CompactionChatService> logger)
    {
        _client = client;
        _store = store;
        _logger = logger;
        ConversationId = conversationId;
        Model = string.IsNullOrWhiteSpace(options.Model) ? "claude-opus-4-8" : options.Model.Trim();
        MaxTokens = options.MaxTokens >= 1 ? options.MaxTokens : 4096;
        _system = systemPrompt;
        SystemPrompt = systemPrompt;

        foreach (StoredTurn turn in seed)
        {
            _turns.Add(turn);
            Role role = turn.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase)
                ? Role.Assistant
                : Role.User;
            _betaHistory.Add(new BetaMessageParam { Role = role, Content = turn.Text });
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        _betaHistory.Clear();
        _turns.Clear();
        await _store.SaveAsync(ConversationId, _turns, cancellationToken);
    }

    public async IAsyncEnumerable<string> SendAsync(
        string userMessage, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var userMsg = new BetaMessageParam { Role = Role.User, Content = userMessage };
        var requestMessages = new List<BetaMessageParam>(_betaHistory) { userMsg };

        var parameters = new MessageCreateParams
        {
            Model = Model,
            MaxTokens = MaxTokens,
            System = _system,
            Betas = ["compact-2026-01-12"],
            ContextManagement = new BetaContextManagementConfig
            {
                Edits = [new BetaCompact20260112Edit()],
            },
            Messages = requestMessages,
        };

        _logger.LogInformation("Compaction request: {MessageCount} message(s) in context", requestMessages.Count);

        BetaMessage response = await _client.Beta.Messages.Create(parameters, cancellationToken);

        // Round-trip response content into history. Compaction blocks MUST be preserved
        // (the API uses them to replace the compacted history on the next request).
        var blocks = new List<BetaContentBlockParam>();
        var text = new StringBuilder();
        foreach (BetaContentBlock block in response.Content)
        {
            if (block.TryPickText(out BetaTextBlock? t))
            {
                blocks.Add(new BetaTextBlockParam { Text = t.Text });
                text.Append(t.Text);
            }
            else if (block.TryPickCompaction(out BetaCompactionBlock? compaction))
            {
                blocks.Add(new BetaCompactionBlockParam { Content = compaction.Content });
            }
        }

        // Commit only on success.
        _betaHistory.Add(userMsg);
        _betaHistory.Add(new BetaMessageParam { Role = Role.Assistant, Content = blocks });

        string reply = text.ToString();
        _turns.Add(new StoredTurn("user", userMessage));
        _turns.Add(new StoredTurn("assistant", reply));

        LastTurnUsage = new TokenUsage(
            response.Usage.InputTokens,
            response.Usage.OutputTokens,
            response.Usage.CacheReadInputTokens ?? 0,
            response.Usage.CacheCreationInputTokens ?? 0);

        await _store.SaveAsync(ConversationId, _turns, cancellationToken);
        _logger.LogInformation("Turn complete: {Usage}", LastTurnUsage);

        yield return reply;
    }
}
