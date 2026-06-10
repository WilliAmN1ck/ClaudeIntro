using System.Runtime.CompilerServices;
using Anthropic;
using Anthropic.Helpers;
using Anthropic.Models.Messages;

namespace ChatBot;

/// <summary>Real <see cref="IChatCompletionClient"/> backed by the Anthropic SDK.</summary>
public sealed class AnthropicCompletionClient : IChatCompletionClient
{
    private readonly AnthropicClient _client;

    public AnthropicCompletionClient(AnthropicClient client) => _client = client;

    public ICompletionStream Stream(MessageCreateParams parameters, CancellationToken cancellationToken) =>
        new SdkCompletionStream(_client, parameters);

    private sealed class SdkCompletionStream : ICompletionStream
    {
        private readonly AnthropicClient _client;
        private readonly MessageCreateParams _parameters;
        private readonly MessageContentAggregator _aggregator = new();
        private Message? _message;

        public SdkCompletionStream(AnthropicClient client, MessageCreateParams parameters)
        {
            _client = client;
            _parameters = parameters;
        }

        public async IAsyncEnumerable<string> ReadTextAsync(
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await foreach (RawMessageStreamEvent streamEvent in
                           _client.Messages.CreateStreaming(_parameters)
                                  .CollectAsync(_aggregator)
                                  .WithCancellation(cancellationToken))
            {
                if (streamEvent.TryPickContentBlockDelta(out var delta) &&
                    delta.Delta.TryPickText(out var text))
                {
                    yield return text.Text;
                }
            }

            _message = _aggregator.Message();
        }

        public CompletionResult GetResult()
        {
            Message message = _message
                ?? throw new InvalidOperationException("ReadTextAsync must be fully consumed before GetResult.");

            var toolCalls = message.Content
                .Select(b => b.Value)
                .OfType<ToolUseBlock>()
                .Select(tu => new ToolCall(tu.ID, tu.Name, tu.Input))
                .ToList();

            var usage = new TokenUsage(
                message.Usage.InputTokens,
                message.Usage.OutputTokens,
                message.Usage.CacheReadInputTokens ?? 0,
                message.Usage.CacheCreationInputTokens ?? 0);

            return new CompletionResult(message.StopReason == "tool_use", toolCalls, usage);
        }
    }
}
