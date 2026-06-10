using System.Runtime.CompilerServices;
using System.Text;
using Anthropic;
using Anthropic.Helpers;
using Anthropic.Models.Messages;
using Microsoft.Extensions.Logging;

namespace ChatBot;

/// <summary>
/// Default chat engine: streams replies token-by-token, manages context with a
/// simple count-based trim, and runs an agentic tool loop when tools are registered.
/// </summary>
public sealed class StreamingChatService : IChatService
{
    private readonly AnthropicClient _client;
    private readonly IConversationStore _store;
    private readonly ILogger<StreamingChatService> _logger;
    private readonly int _maxHistoryMessages;
    private readonly List<TextBlockParam> _systemBlocks;
    private readonly List<StoredTurn> _turns = new();
    private readonly IReadOnlyList<IChatTool> _tools;
    private readonly List<ToolUnion> _toolUnions;

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
        IEnumerable<IChatTool> tools,
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

        _tools = tools.ToList();
        _toolUnions = new List<ToolUnion>();
        foreach (IChatTool tool in _tools)
            _toolUnions.Add(BuildSdkTool(tool));

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
        // Build the in-call conversation from existing turns plus the new (not-yet-
        // committed) user message, trimmed to the recent window. Tool-use/tool-result
        // turns added during the loop are in-call only; persistence stays text-only.
        var pending = new List<StoredTurn>(_turns) { new("user", userMessage) };
        var conversation = HistoryTrimmer.Trim(pending, _maxHistoryMessages)
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
                Tools = _toolUnions.Count > 0 ? _toolUnions : null,
            };

            var aggregator = new MessageContentAggregator();
            await foreach (RawMessageStreamEvent streamEvent in
                           _client.Messages.CreateStreaming(parameters)
                                  .CollectAsync(aggregator)
                                  .WithCancellation(cancellationToken))
            {
                if (streamEvent.TryPickContentBlockDelta(out var delta) &&
                    delta.Delta.TryPickText(out var text))
                {
                    reply.Append(text.Text);
                    yield return text.Text;
                }
            }

            Message message = aggregator.Message();
            inputTokens += message.Usage.InputTokens;
            outputTokens += message.Usage.OutputTokens;
            cacheRead += message.Usage.CacheReadInputTokens ?? 0;
            cacheCreation += message.Usage.CacheCreationInputTokens ?? 0;

            conversation.Add(new MessageParam { Role = Role.Assistant, Content = ToAssistantParams(message.Content) });

            var toolUses = message.Content.Select(b => b.Value).OfType<ToolUseBlock>().ToList();
            if (message.StopReason != "tool_use" || toolUses.Count == 0)
                break;

            var results = new List<ContentBlockParam>();
            foreach (ToolUseBlock toolUse in toolUses)
            {
                yield return $"\n[tool: {toolUse.Name}]\n";
                string result = await ExecuteToolAsync(toolUse, cancellationToken);
                results.Add(new ToolResultBlockParam { ToolUseID = toolUse.ID, Content = result });
            }
            conversation.Add(new MessageParam { Role = Role.User, Content = results });
        }

        // Commit only on success: a cancelled or failed turn leaves history untouched.
        _turns.Add(new StoredTurn("user", userMessage));
        _turns.Add(new StoredTurn("assistant", reply.ToString()));
        LastTurnUsage = new TokenUsage(inputTokens, outputTokens, cacheRead, cacheCreation);
        _store.Save(_turns);

        _logger.LogInformation("Turn complete: {Usage}", LastTurnUsage);
    }

    private async Task<string> ExecuteToolAsync(ToolUseBlock toolUse, CancellationToken cancellationToken)
    {
        IChatTool? tool = _tools.FirstOrDefault(t => t.Name == toolUse.Name);
        if (tool is null)
            return $"Error: unknown tool '{toolUse.Name}'.";

        try
        {
            _logger.LogInformation("Executing tool {Tool}", toolUse.Name);
            return await tool.ExecuteAsync(toolUse.Input, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Tool {Tool} failed", toolUse.Name);
            return $"Error executing '{toolUse.Name}': {ex.Message}";
        }
    }

    private static Tool BuildSdkTool(IChatTool tool) => new()
    {
        Name = tool.Name,
        Description = tool.Description,
        InputSchema = new()
        {
            Properties = tool.Properties.ToDictionary(kv => kv.Key, kv => kv.Value),
            Required = tool.Required.ToList(),
        },
    };

    private static List<ContentBlockParam> ToAssistantParams(IReadOnlyList<ContentBlock> content)
    {
        var blocks = new List<ContentBlockParam>();
        foreach (ContentBlock block in content)
        {
            if (block.TryPickText(out TextBlock? text))
                blocks.Add(new TextBlockParam { Text = text.Text });
            else if (block.TryPickToolUse(out ToolUseBlock? toolUse))
                blocks.Add(new ToolUseBlockParam { ID = toolUse.ID, Name = toolUse.Name, Input = toolUse.Input });
        }
        return blocks;
    }
}
