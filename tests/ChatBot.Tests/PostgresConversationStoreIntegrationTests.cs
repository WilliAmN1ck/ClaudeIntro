using ChatBot;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ChatBot.Tests;

/// <summary>
/// Integration tests for <see cref="PostgresConversationStore"/>. They run only when
/// the <c>CHATBOT_TEST_POSTGRES</c> environment variable holds a connection string,
/// and are skipped otherwise (e.g. in CI without a database).
///
/// Local Postgres: <c>docker compose up -d</c>, then set
/// CHATBOT_TEST_POSTGRES="Host=localhost;Username=chatbot;Password=chatbot;Database=chatbot".
/// </summary>
public class PostgresConversationStoreIntegrationTests
{
    private static string? ConnectionString => Environment.GetEnvironmentVariable("CHATBOT_TEST_POSTGRES");

    private static PostgresConversationStore NewStore(string conversationId)
    {
        var options = Options.Create(new ChatOptions
        {
            Store = "postgres",
            PostgresConnectionString = ConnectionString,
            ConversationId = conversationId,
        });
        return new PostgresConversationStore(options, NullLogger<PostgresConversationStore>.Instance);
    }

    [SkippableFact]
    public void Save_load_clear_round_trips()
    {
        Skip.If(string.IsNullOrWhiteSpace(ConnectionString), "CHATBOT_TEST_POSTGRES not set.");

        string id = $"test_{Guid.NewGuid():N}";
        PostgresConversationStore store = NewStore(id);
        try
        {
            Assert.False(store.Exists());

            store.Save(new List<StoredTurn> { new("user", "hi"), new("assistant", "hello") });

            Assert.True(store.Exists());
            var loaded = store.Load();
            Assert.Equal(2, loaded.Count);
            Assert.Equal("user", loaded[0].Role);
            Assert.Equal("hello", loaded[1].Text);

            // Save replaces prior contents.
            store.Save(new List<StoredTurn> { new("user", "again") });
            Assert.Single(store.Load());
        }
        finally
        {
            store.Clear();
            Assert.False(store.Exists());
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
