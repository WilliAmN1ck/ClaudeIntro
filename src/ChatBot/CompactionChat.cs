using Anthropic;
using Anthropic.Models.Beta.Messages;

namespace ChatBot;

/// <summary>
/// Conversation backend that uses the beta server-side compaction feature
/// (<c>compact-2026-01-12</c>): the API automatically summarizes earlier turns
/// as the context grows. Non-streaming — compaction blocks in the response must
/// be preserved verbatim in history, so we round-trip the full content each turn.
/// </summary>
public sealed class CompactionChat
{
    private readonly AnthropicClient _client;
    private readonly string _model;
    private readonly long _maxTokens;
    private readonly string _system;
    private readonly List<BetaMessageParam> _history = new();

    public CompactionChat(
        AnthropicClient client, string model, long maxTokens, string system, IEnumerable<StoredTurn> seed)
    {
        _client = client;
        _model = model;
        _maxTokens = maxTokens;
        _system = system;

        foreach (StoredTurn turn in seed)
        {
            Role role = turn.Role.Equals("assistant", StringComparison.OrdinalIgnoreCase)
                ? Role.Assistant
                : Role.User;
            _history.Add(new BetaMessageParam { Role = role, Content = turn.Text });
        }
    }

    /// <summary>Drops all in-memory conversation context.</summary>
    public void Clear() => _history.Clear();

    /// <summary>Sends a user turn, prints the reply, and returns the reply text.</summary>
    public async Task<string> SendAsync(string userText)
    {
        _history.Add(new BetaMessageParam { Role = Role.User, Content = userText });

        var parameters = new MessageCreateParams
        {
            Model = _model,
            MaxTokens = _maxTokens,
            System = _system,
            Betas = ["compact-2026-01-12"],
            ContextManagement = new BetaContextManagementConfig
            {
                Edits = [new BetaCompact20260112Edit()],
            },
            Messages = _history,
        };

        BetaMessage response = await _client.Beta.Messages.Create(parameters);

        // Round-trip response content back into history. Compaction blocks MUST be
        // preserved (the API uses them to replace the compacted history next turn).
        var blocks = new List<BetaContentBlockParam>();
        var text = new System.Text.StringBuilder();
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

        _history.Add(new BetaMessageParam { Role = Role.Assistant, Content = blocks });

        string reply = text.ToString();
        Console.Write(reply);
        return reply;
    }
}
