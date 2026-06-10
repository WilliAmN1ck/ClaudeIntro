using Anthropic;
using ChatBot;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ChatBot.Tests;

public class StreamingChatServiceTests
{
    // A non-null client is required to construct the service; no network call is made
    // by the constructor, so a dummy key is fine for these state-only tests.
    private static AnthropicClient DummyClient() => new() { ApiKey = "sk-ant-dummy" };

    private static StreamingChatService NewService(ChatOptions options, IConversationStore store) =>
        new(DummyClient(), options, "system", store, Array.Empty<IChatTool>(), NullLogger<StreamingChatService>.Instance);

    [Fact]
    public void Clamps_invalid_options_to_defaults()
    {
        var svc = NewService(
            new ChatOptions { Model = "  ", MaxTokens = 0 },
            new FakeConversationStore());

        Assert.Equal("claude-opus-4-8", svc.Model);
        Assert.Equal(4096, svc.MaxTokens);
    }

    [Fact]
    public void Trims_model_and_honors_overrides()
    {
        var svc = NewService(
            new ChatOptions { Model = "  claude-haiku-4-5 ", MaxTokens = 1024 },
            new FakeConversationStore());

        Assert.Equal("claude-haiku-4-5", svc.Model);
        Assert.Equal(1024, svc.MaxTokens);
    }

    [Fact]
    public void Seeds_history_from_store()
    {
        var seed = new[] { new StoredTurn("user", "hi"), new StoredTurn("assistant", "hello") };
        var svc = NewService(new ChatOptions(), new FakeConversationStore(seed));

        Assert.Equal(2, svc.History.Count);
        Assert.Equal("hello", svc.History[1].Text);
    }

    [Fact]
    public void Clear_empties_history_and_store()
    {
        var store = new FakeConversationStore(new[] { new StoredTurn("user", "hi") });
        var svc = NewService(new ChatOptions(), store);

        svc.Clear();

        Assert.Empty(svc.History);
        Assert.True(store.Cleared);
    }

    [Fact]
    public void No_usage_before_first_turn()
    {
        var svc = NewService(new ChatOptions(), new FakeConversationStore());
        Assert.Null(svc.LastTurnUsage);
    }
}
