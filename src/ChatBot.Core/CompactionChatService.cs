using System.Runtime.CompilerServices;
using System.Text;
using Anthropic.Models.Beta.Messages;
using Microsoft.Extensions.Logging;

namespace ChatBot;

/// <summary>
/// Chat engine that uses beta server-side compaction (<c>compact-2026-01-12</c>): the API
/// summarizes earlier turns as context grows. Non-streaming — each turn's reply is yielded as a
/// chunk. Compaction blocks in the response are preserved verbatim and round-tripped each turn.
/// Runs an agentic tool loop when tools are registered. Talks to the API through
/// <see cref="IBetaCompletionClient"/> so the loop is testable.
/// </summary>
public sealed class CompactionChatService : IChatService
{
    private readonly IBetaCompletionClient _completion;
    private readonly IConversationStore _store;
    private readonly ToolInvoker _tools;
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
        IBetaCompletionClient completion,
        ChatOptions options,
        string systemPrompt,
        string conversationId,
        IConversationStore store,
        IEnumerable<StoredTurn> seed,
        IEnumerable<IChatTool> tools,
        ILogger<CompactionChatService> logger)
    {
        _completion = completion;
        _store = store;
        _tools = new ToolInvoker(tools);
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
        var conversation = new List<BetaMessageParam>(_betaHistory)
        {
            new() { Role = Role.User, Content = userMessage },
        };

        var reply = new StringBuilder();
        long inputTokens = 0, outputTokens = 0, cacheRead = 0, cacheCreation = 0;

        // Agentic loop: send a turn; if the model asked for tools, run them and continue.
        while (true)
        {
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
                Messages = conversation,
                Tools = _tools.BetaToolUnions.Count > 0 ? _tools.BetaToolUnions.ToList() : null,
            };

            _logger.LogInformation("Compaction request: {MessageCount} message(s) in context", conversation.Count);

            BetaCompletionResult result = await _completion.CreateAsync(parameters, cancellationToken);
            inputTokens += result.Usage.InputTokens;
            outputTokens += result.Usage.OutputTokens;
            cacheRead += result.Usage.CacheReadTokens;
            cacheCreation += result.Usage.CacheCreationTokens;

            if (result.Text.Length > 0)
            {
                reply.Append(result.Text);
                yield return result.Text;
            }

            // Round-trip the assistant content (text + tool_use + compaction blocks) into history.
            conversation.Add(new BetaMessageParam
            {
                Role = Role.Assistant,
                Content = result.AssistantContent.ToList(),
            });

            if (!result.StoppedForToolUse || result.ToolCalls.Count == 0)
                break;

            var toolResults = new List<BetaContentBlockParam>();
            foreach (ToolCall call in result.ToolCalls)
            {
                yield return $"\n[tool: {call.Name}]\n";
                _logger.LogInformation("Executing tool {Tool}", call.Name);
                string toolResult = await _tools.InvokeAsync(call.Name, call.Input, cancellationToken);
                toolResults.Add(new BetaToolResultBlockParam { ToolUseID = call.Id, Content = toolResult });
            }
            conversation.Add(new BetaMessageParam { Role = Role.User, Content = toolResults });
        }

        // Commit only on success: a cancelled or failed turn leaves history untouched.
        _betaHistory.Clear();
        _betaHistory.AddRange(conversation);
        _turns.Add(new StoredTurn("user", userMessage));
        _turns.Add(new StoredTurn("assistant", reply.ToString()));
        LastTurnUsage = new TokenUsage(inputTokens, outputTokens, cacheRead, cacheCreation);
        await _store.SaveAsync(ConversationId, _turns, cancellationToken);

        _logger.LogInformation("Turn complete: {Usage}", LastTurnUsage);
    }
}
