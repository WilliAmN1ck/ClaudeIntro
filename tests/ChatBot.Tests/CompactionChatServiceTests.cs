using ChatBot;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace ChatBot.Tests;

public class CompactionChatServiceTests
{
    private const string ConvId = "test";

    private static CompactionChatService NewService(
        IConversationStore store,
        IBetaCompletionClient? completion = null,
        IEnumerable<IChatTool>? tools = null,
        IEnumerable<StoredTurn>? seed = null) =>
        new(completion ?? new FakeBetaCompletionClient(),
            new ChatOptions(),
            "system",
            ConvId,
            store,
            seed ?? Array.Empty<StoredTurn>(),
            tools ?? Array.Empty<IChatTool>(),
            NullLogger<CompactionChatService>.Instance);

    private static async Task<string> Collect(IAsyncEnumerable<string> stream)
    {
        var parts = new List<string>();
        await foreach (string s in stream)
            parts.Add(s);
        return string.Concat(parts);
    }

    [Fact]
    public void Exposes_conversation_id_and_seeds_history()
    {
        var seed = new[] { new StoredTurn("user", "hi"), new StoredTurn("assistant", "hello") };
        var svc = NewService(new FakeConversationStore(), seed: seed);

        Assert.Equal(ConvId, svc.ConversationId);
        Assert.Equal(2, svc.History.Count);
        Assert.Equal("hello", svc.History[1].Text);
    }

    [Fact]
    public async Task Plain_reply_updates_history_usage_and_store()
    {
        var store = new FakeConversationStore();
        var completion = new FakeBetaCompletionClient(BetaFakeTurn.Reply("hello there", inTok: 12, outTok: 7));
        var svc = NewService(store, completion);

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
        var completion = new FakeBetaCompletionClient(
            BetaFakeTurn.ToolUse("", new ToolCall("call_1", "echo", Json.Args(new { value = "x" })), inTok: 10, outTok: 5),
            BetaFakeTurn.Reply("all done", inTok: 20, outTok: 4));

        var svc = NewService(store, completion, new IChatTool[] { tool });

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
        var completion = new FakeBetaCompletionClient(
            BetaFakeTurn.ToolUse("", new ToolCall("call_1", "nope", Json.Args(new { }))),
            BetaFakeTurn.Reply("recovered"));

        // No tools registered → the call resolves to an error result fed back to the model.
        var svc = NewService(store, completion);

        string output = await Collect(svc.SendAsync("do a thing"));

        Assert.Contains("recovered", output);
        Assert.Equal(2, completion.CallCount);
        Assert.Equal("recovered", svc.History[1].Text);
    }

    [Fact]
    public async Task Clear_empties_history_and_store()
    {
        var store = new FakeConversationStore();
        var svc = NewService(store, seed: new[] { new StoredTurn("user", "hi") });

        await svc.ClearAsync();

        Assert.Empty(svc.History);
        Assert.Empty(store.SavedFor(ConvId));
    }
}
