using System.Text;
using Anthropic;
using Anthropic.Models.Beta.Messages;

namespace ChatBot;

/// <summary>Real <see cref="IBetaCompletionClient"/> backed by the Anthropic SDK's beta Messages API.</summary>
public sealed class AnthropicBetaCompletionClient : IBetaCompletionClient
{
    private readonly AnthropicClient _client;

    public AnthropicBetaCompletionClient(AnthropicClient client) => _client = client;

    public async Task<BetaCompletionResult> CreateAsync(
        MessageCreateParams parameters, CancellationToken cancellationToken)
    {
        BetaMessage response = await _client.Beta.Messages.Create(parameters, cancellationToken);

        // Project the response into engine-domain text + tool calls, and rebuild the assistant
        // content blocks to round-trip — compaction blocks MUST be preserved verbatim so the API
        // can replace the compacted history on the next request.
        var assistantContent = new List<BetaContentBlockParam>();
        var toolCalls = new List<ToolCall>();
        var text = new StringBuilder();

        foreach (BetaContentBlock block in response.Content)
        {
            if (block.TryPickText(out BetaTextBlock? t))
            {
                assistantContent.Add(new BetaTextBlockParam { Text = t.Text });
                text.Append(t.Text);
            }
            else if (block.TryPickToolUse(out BetaToolUseBlock? toolUse))
            {
                assistantContent.Add(new BetaToolUseBlockParam
                {
                    ID = toolUse.ID,
                    Name = toolUse.Name,
                    Input = toolUse.Input,
                });
                toolCalls.Add(new ToolCall(toolUse.ID, toolUse.Name, toolUse.Input));
            }
            else if (block.TryPickCompaction(out BetaCompactionBlock? compaction))
            {
                // Round-trip both the summary and the opaque metadata verbatim — the API uses
                // them to replace the compacted history on the next request.
                assistantContent.Add(new BetaCompactionBlockParam
                {
                    Content = compaction.Content,
                    EncryptedContent = compaction.EncryptedContent,
                });
            }
        }

        var usage = new TokenUsage(
            response.Usage.InputTokens,
            response.Usage.OutputTokens,
            response.Usage.CacheReadInputTokens ?? 0,
            response.Usage.CacheCreationInputTokens ?? 0);

        return new BetaCompletionResult(
            text.ToString(), toolCalls, assistantContent, response.StopReason == "tool_use", usage);
    }
}
