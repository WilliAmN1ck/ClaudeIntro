using System.Runtime.CompilerServices;
using System.Text.Json;
using Anthropic.Models.Messages;
using ChatBot;

namespace ChatBot.Tests;

/// <summary>A scripted <see cref="IChatCompletionClient"/> for testing the engine loop.</summary>
internal sealed class FakeCompletionClient : IChatCompletionClient
{
    private readonly Queue<FakeTurn> _turns;

    public int CallCount { get; private set; }

    public FakeCompletionClient(params FakeTurn[] turns) => _turns = new Queue<FakeTurn>(turns);

    public ICompletionStream Stream(MessageCreateParams parameters, CancellationToken cancellationToken)
    {
        CallCount++;
        return new FakeStream(_turns.Dequeue());
    }
}

internal sealed record FakeTurn(string Text, IReadOnlyList<ToolCall> ToolCalls, TokenUsage Usage)
{
    public bool StoppedForToolUse => ToolCalls.Count > 0;

    public static FakeTurn Reply(string text, long inTok = 10, long outTok = 5) =>
        new(text, Array.Empty<ToolCall>(), new TokenUsage(inTok, outTok, 0, 0));

    public static FakeTurn ToolUse(string text, params ToolCall[] calls) =>
        new(text, calls, new TokenUsage(10, 5, 0, 0));
}

internal sealed class FakeStream : ICompletionStream
{
    private readonly FakeTurn _turn;

    public FakeStream(FakeTurn turn) => _turn = turn;

    public async IAsyncEnumerable<string> ReadTextAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(_turn.Text))
            yield return _turn.Text;
        await Task.CompletedTask;
    }

    public CompletionResult GetResult() => new(_turn.StoppedForToolUse, _turn.ToolCalls, _turn.Usage);
}

/// <summary>An <see cref="IChatTool"/> that records its invocation, for assertions.</summary>
internal sealed class RecordingTool : IChatTool
{
    public RecordingTool(string name = "echo", string result = "recorded")
    {
        Name = name;
        _result = result;
    }

    private readonly string _result;

    public string Name { get; }
    public string Description => "Records that it was called.";
    public IReadOnlyDictionary<string, JsonElement> Properties => new Dictionary<string, JsonElement>();
    public IReadOnlyList<string> Required => Array.Empty<string>();

    public bool Invoked { get; private set; }
    public IReadOnlyDictionary<string, JsonElement>? LastInput { get; private set; }

    public Task<string> ExecuteAsync(IReadOnlyDictionary<string, JsonElement> arguments, CancellationToken ct)
    {
        Invoked = true;
        LastInput = arguments;
        return Task.FromResult(_result);
    }
}

internal static class Json
{
    public static IReadOnlyDictionary<string, JsonElement> Args(object value) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(value))!;
}
