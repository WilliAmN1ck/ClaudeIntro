using ChatBot;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ChatBot.Tests;

/// <summary>
/// Integration tests for <see cref="PostgresConversationStore"/>. They run only when the
/// <c>CHATBOT_TEST_POSTGRES</c> environment variable holds a connection string, and are
/// skipped otherwise (e.g. in CI without a database).
///
/// Local Postgres: <c>docker compose up -d</c>, then set
/// CHATBOT_TEST_POSTGRES="Host=localhost;Username=chatbot;Password=chatbot;Database=chatbot".
/// </summary>
public class PostgresConversationStoreIntegrationTests
{
    private static string? ConnectionString => Environment.GetEnvironmentVariable("CHATBOT_TEST_POSTGRES");

    private static PostgresConversationStore NewStore()
    {
        var options = Options.Create(new ChatOptions
        {
            Store = "postgres",
            PostgresConnectionString = ConnectionString,
        });
        return new PostgresConversationStore(options, NullLogger<PostgresConversationStore>.Instance);
    }

    [SkippableFact]
    public async Task Multi_conversation_round_trips()
    {
        Skip.If(string.IsNullOrWhiteSpace(ConnectionString), "CHATBOT_TEST_POSTGRES not set.");

        PostgresConversationStore store = NewStore();
        string id = $"test_{Guid.NewGuid():N}";
        try
        {
            Assert.Null(await store.GetAsync(id));

            ConversationInfo created = await store.CreateAsync(id, "My Title");
            Assert.Equal(id, created.Id);
            Assert.Equal("My Title", created.Title);

            await store.SaveAsync(id, new List<StoredTurn> { new("user", "hi"), new("assistant", "hello") });

            var loaded = await store.LoadAsync(id);
            Assert.Equal(2, loaded.Count);
            Assert.Equal("hello", loaded[1].Text);
            Assert.Equal(2, (await store.GetAsync(id))!.TurnCount);

            await store.RenameAsync(id, "Renamed");
            Assert.Equal("Renamed", (await store.GetAsync(id))!.Title);

            // Save replaces prior turns rather than appending.
            await store.SaveAsync(id, new List<StoredTurn> { new("user", "again") });
            Assert.Single(await store.LoadAsync(id));

            Assert.Contains(await store.ListAsync(), c => c.Id == id);
        }
        finally
        {
            await store.DeleteAsync(id);
            Assert.Null(await store.GetAsync(id));
        }
    }

    [Fact]
    public void Missing_connection_string_throws()
    {
        // No database needed — the guard runs before any connection attempt.
        var options = Options.Create(new ChatOptions { Store = "postgres", PostgresConnectionString = null });
        Assert.Throws<InvalidOperationException>(() =>
            new PostgresConversationStore(options, NullLogger<PostgresConversationStore>.Instance));
    }
}
