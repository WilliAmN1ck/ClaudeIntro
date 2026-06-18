using ChatBot;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ChatBot.Tests;

public class ChatServiceFactoryTests
{
    [Fact]
    public async Task CreateAsync_binds_engine_to_conversation_and_seeds_history()
    {
        var store = new FakeConversationStore();
        store.Seed("trip", new[] { new StoredTurn("user", "hi"), new StoredTurn("assistant", "hello") });

        var factory = new ChatServiceFactory(
            new FakeCompletionClient(),
            new FakeBetaCompletionClient(),
            Options.Create(new ChatOptions()),
            store,
            Array.Empty<IChatTool>(),
            NullLoggerFactory.Instance);

        IChatService chat = await factory.CreateAsync("trip");

        Assert.Equal("trip", chat.ConversationId);
        Assert.Equal(2, chat.History.Count);
        Assert.Equal("hello", chat.History[1].Text);
    }

    [Fact]
    public void Inline_prompt_wins()
    {
        var options = new ChatOptions { SystemPrompt = "  Be terse.  ", SystemPromptFile = "ignored.txt" };
        Assert.Equal("Be terse.", ChatServiceFactory.ResolveSystemPrompt(options));
    }

    [Fact]
    public void Reads_prompt_from_file_when_no_inline()
    {
        string path = Path.Combine(Path.GetTempPath(), $"prompt_{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, "  You are a pirate.  ");
        try
        {
            var options = new ChatOptions { SystemPromptFile = path };
            Assert.Equal("You are a pirate.", ChatServiceFactory.ResolveSystemPrompt(options));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Falls_back_to_default()
    {
        var result = ChatServiceFactory.ResolveSystemPrompt(new ChatOptions());
        Assert.Equal("You are a helpful, concise assistant.", result);
    }

    [Fact]
    public void Missing_file_falls_back_to_default()
    {
        var options = new ChatOptions { SystemPromptFile = "does-not-exist-xyz.txt" };
        Assert.Equal("You are a helpful, concise assistant.", ChatServiceFactory.ResolveSystemPrompt(options));
    }
}
