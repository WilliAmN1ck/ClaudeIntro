using System.Runtime.CompilerServices;
using System.Text;
using Anthropic;
using Anthropic.Models.Beta.Messages;

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
    private readonly string _system;
    private readonly List<BetaMessageParam> _betaHistory = new();
    private readonly List<StoredTurn> _turns = new();

    public string Model { get; }
    public long MaxTokens { get; }
    public string SystemPrompt { get; }
    public IReadOnlyList<StoredTurn> History => _turns;

    public CompactionChatService(
        AnthropicClient client, ChatOptions options, string systemPrompt, IEnumerable<StoredTurn> seed)
    {
        _client = client;
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

    public void Clear()
    {
        _betaHistory.Clear();
        _turns.Clear();
    }

    public async IAsyncEnumerable<string> SendAsync(
        string userMessage, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _turns.Add(new StoredTurn("user", userMessage));
        _betaHistory.Add(new BetaMessageParam { Role = Role.User, Content = userMessage });

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
            Messages = _betaHistory,
        };

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

        _betaHistory.Add(new BetaMessageParam { Role = Role.Assistant, Content = blocks });

        string reply = text.ToString();
        _turns.Add(new StoredTurn("assistant", reply));
        yield return reply;
    }
}
