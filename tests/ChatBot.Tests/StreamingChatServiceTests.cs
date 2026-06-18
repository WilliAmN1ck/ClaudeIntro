using ChatBot;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ChatBot.Tests;

public class StreamingChatServiceTests
{
    private const string ConvId = "test";

    private static StreamingChatService NewService(
        ChatOptions options,
        IConversationStore store,
        IChatCompletionClient? completion = null,
        IEnumerable<IChatTool>? tools = null,
        IEnumerable<StoredTurn>? seed = null) =>
        new(completion ?? new FakeCompletionClient(),
            options,
            "system",
            ConvId,
            store,
            seed ?? Array.Empty<StoredTurn>(),
            tools ?? Array.Empty<IChatTool>(),
            NullLogger<StreamingChatService>.Instance);

    private static async Task<string> Collect(IAsyncEnumerable<string> stream)
    {
        var parts = new List<string>();
        await foreach (string s in stream)
            parts.Add(s);
        return string.Concat(parts);
    }

    [Fact]
    public void Clamps_invalid_options_to_defaults()
    {
        var svc = NewService(new ChatOptions { Model = "  ", MaxTokens = 0 }, new FakeConversationStore());
        Assert.Equal("claude-opus-4-8", svc.Model);
        Assert.Equal(4096, svc.MaxTokens);
    }

    [Fact]
    public void Trims_model_and_honors_overrides()
    {
        var svc = NewService(new ChatOptions { Model = "  claude-haiku-4-5 ", MaxTokens = 1024 }, new FakeConversationStore());
        Assert.Equal("claude-haiku-4-5", svc.Model);
        Assert.Equal(1024, svc.MaxTokens);
    }

    [Fact]
    public void Seeds_history_from_seed()
    {
        var seed = new[] { new StoredTurn("user", "hi"), new StoredTurn("assistant", "hello") };
        var svc = NewService(new ChatOptions(), new FakeConversationStore(), seed: seed);

        Assert.Equal(2, svc.History.Count);
        Assert.Equal("hello", svc.History[1].Text);
    }

    [Fact]
    public async Task Clear_empties_history_and_store()
    {
        var store = new FakeConversationStore();
        var svc = NewService(new ChatOptions(), store, seed: new[] { new StoredTurn("user", "hi") });

        await svc.ClearAsync();

        Assert.Empty(svc.History);
        Assert.Empty(store.SavedFor(ConvId)); // conversation kept, but emptied
    }

    [Fact]
    public void Conversation_id_is_exposed()
    {
        var svc = NewService(new ChatOptions(), new FakeConversationStore());
        Assert.Equal(ConvId, svc.ConversationId);
    }

    [Fact]
    public void No_usage_before_first_turn()
    {
        var svc = NewService(new ChatOptions(), new FakeConversationStore());
        Assert.Null(svc.LastTurnUsage);
    }

    [Fact]
    public async Task Plain_reply_updates_history_usage_and_store()
    {
        var store = new FakeConversationStore();
        var completion = new FakeCompletionClient(FakeTurn.Reply("hello there", inTok: 12, outTok: 7));
        var svc = NewService(new ChatOptions(), store, completion);

        string output = await Collect(svc.SendAsync("hi"));

        Assert.Equal("hello there", output);
        Assert.Equal(2, svc.History.Count);
        Assert.Equal("hi", svc.History[0].Text);
        Assert.Equal("hello there", svc.History[1].Text);
        Assert.Equal(12, svc.LastTurnUsage!.Value.InputTokens);
        Assert.Equal(7, svc.LastTurnUsage!.Value.OutputTokens);
        Assert.Equal(2, store.SavedFor(ConvId).Count); // persisted on success
    }

    [Fact]
    public async Task Tool_loop_executes_tool_then_continues()
    {
        var tool = new RecordingTool("echo");
        var store = new FakeConversationStore();
        var completion = new FakeCompletionClient(
            FakeTurn.ToolUse("", new ToolCall("call_1", "echo", Json.Args(new { value = "x" }))),
            FakeTurn.Reply("all done", inTok: 20, outTok: 4));

        var svc = NewService(new ChatOptions(), store, completion, new IChatTool[] { tool });

        string output = await Collect(svc.SendAsync("please echo"));

        Assert.Contains("[tool: echo]", output);
        Assert.Contains("all done", output);
        Assert.True(tool.Invoked);
        Assert.True(tool.LastInput!.ContainsKey("value"));
        Assert.Equal(2, completion.CallCount); // one tool round-trip = two API calls

        Assert.Equal(2, svc.History.Count);
        Assert.Equal("all done", svc.History[1].Text);

        // Usage summed across both iterations.
        Assert.Equal(30, svc.LastTurnUsage!.Value.InputTokens);
        Assert.Equal(9, svc.LastTurnUsage!.Value.OutputTokens);
    }

    [Fact]
    public async Task Unknown_tool_is_reported_and_loop_still_completes()
    {
        var store = new FakeConversationStore();
        var completion = new FakeCompletionClient(
            FakeTurn.ToolUse("", new ToolCall("call_1", "nope", Json.Args(new { }))),
            FakeTurn.Reply("recovered"));

        // No tools registered → the call resolves to an error result fed back to the model.
        var svc = NewService(new ChatOptions(), store, completion);

        string output = await Collect(svc.SendAsync("do a thing"));

        Assert.Contains("recovered", output);
        Assert.Equal(2, completion.CallCount);
        Assert.Equal("recovered", svc.History[1].Text);
    }
}
