using ChatBot;
using ChatBot.Tools;
using Xunit;

namespace ChatBot.Tests;

public class ToolInvokerTests
{
    [Fact]
    public async Task Invokes_registered_tool()
    {
        var tool = new RecordingTool("echo", result: "ok");
        var invoker = new ToolInvoker(new IChatTool[] { tool });

        string result = await invoker.InvokeAsync("echo", Json.Args(new { a = 1 }), CancellationToken.None);

        Assert.Equal("ok", result);
        Assert.True(tool.Invoked);
    }

    [Fact]
    public async Task Unknown_tool_returns_error_string()
    {
        var invoker = new ToolInvoker(Array.Empty<IChatTool>());

        string result = await invoker.InvokeAsync("missing", Json.Args(new { }), CancellationToken.None);

        Assert.StartsWith("Error: unknown tool", result);
    }

    [Fact]
    public async Task Tool_exception_becomes_error_string()
    {
        var invoker = new ToolInvoker(new IChatTool[] { new ThrowingTool() });

        string result = await invoker.InvokeAsync("boom", Json.Args(new { }), CancellationToken.None);

        Assert.StartsWith("Error executing 'boom'", result);
    }

    [Fact]
    public void Builds_sdk_tool_unions_and_exposes_tools()
    {
        var invoker = new ToolInvoker(new IChatTool[] { new CurrentTimeTool(), new RollDiceTool() });

        Assert.Equal(2, invoker.Tools.Count);
        Assert.Equal(2, invoker.ToolUnions.Count);
    }

    [Fact]
    public void Duplicate_tool_names_throw()
    {
        Assert.Throws<ArgumentException>(() =>
            new ToolInvoker(new IChatTool[] { new RecordingTool("dup"), new RecordingTool("dup") }));
    }

    private sealed class ThrowingTool : IChatTool
    {
        public string Name => "boom";
        public string Description => "throws";
        public IReadOnlyDictionary<string, System.Text.Json.JsonElement> Properties =>
            new Dictionary<string, System.Text.Json.JsonElement>();
        public IReadOnlyList<string> Required => Array.Empty<string>();
        public Task<string> ExecuteAsync(
            IReadOnlyDictionary<string, System.Text.Json.JsonElement> arguments, CancellationToken ct) =>
            throw new InvalidOperationException("kaboom");
    }
}
