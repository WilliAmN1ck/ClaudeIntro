using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace ChatBot;

/// <summary>
/// Stores the conversation in PostgreSQL as a single <c>jsonb</c> row keyed by
/// <see cref="ChatOptions.ConversationId"/>. Uses Npgsql's synchronous APIs so it
/// satisfies <see cref="IConversationStore"/> without changing the engine.
/// </summary>
public sealed class PostgresConversationStore : IConversationStore
{
    private readonly string _connectionString;
    private readonly string _conversationId;
    private readonly ILogger<PostgresConversationStore> _logger;

    public PostgresConversationStore(IOptions<ChatOptions> options, ILogger<PostgresConversationStore> logger)
    {
        _logger = logger;
        ChatOptions o = options.Value;

        if (string.IsNullOrWhiteSpace(o.PostgresConnectionString))
            throw new InvalidOperationException(
                "PostgresConnectionString is required when Store is 'postgres'.");

        _connectionString = o.PostgresConnectionString;
        _conversationId = string.IsNullOrWhiteSpace(o.ConversationId) ? "default" : o.ConversationId;

        // Create the table up front. A connection/credential problem surfaces here as
        // a clear startup error rather than silently producing empty history later.
        try
        {
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();
            using var cmd = new NpgsqlCommand(
                """
                CREATE TABLE IF NOT EXISTS conversations (
                    id text PRIMARY KEY,
                    turns jsonb NOT NULL,
                    updated_at timestamptz NOT NULL DEFAULT now()
                )
                """, conn);
            cmd.ExecuteNonQuery();
        }
        catch (NpgsqlException ex)
        {
            throw new InvalidOperationException($"Could not connect to PostgreSQL: {ex.Message}", ex);
        }
    }

    public bool Exists()
    {
        try
        {
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();
            using var cmd = new NpgsqlCommand(
                "SELECT jsonb_array_length(turns) FROM conversations WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("id", _conversationId);
            return cmd.ExecuteScalar() is int length && length > 0;
        }
        catch (NpgsqlException ex)
        {
            _logger.LogWarning(ex, "Could not check conversation existence");
            return false;
        }
    }

    public List<StoredTurn> Load()
    {
        try
        {
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();
            using var cmd = new NpgsqlCommand("SELECT turns FROM conversations WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("id", _conversationId);
            if (cmd.ExecuteScalar() is string json)
                return JsonSerializer.Deserialize<List<StoredTurn>>(json) ?? new List<StoredTurn>();
            return new List<StoredTurn>();
        }
        catch (Exception ex) when (ex is NpgsqlException or JsonException)
        {
            _logger.LogWarning(ex, "Could not load conversation; starting fresh");
            return new List<StoredTurn>();
        }
    }

    public void Save(IEnumerable<StoredTurn> turns)
    {
        try
        {
            string json = JsonSerializer.Serialize(turns);
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();
            using var cmd = new NpgsqlCommand(
                """
                INSERT INTO conversations (id, turns, updated_at) VALUES (@id, @turns, now())
                ON CONFLICT (id) DO UPDATE SET turns = @turns, updated_at = now()
                """, conn);
            cmd.Parameters.AddWithValue("id", _conversationId);
            cmd.Parameters.Add(new NpgsqlParameter("turns", NpgsqlDbType.Jsonb) { Value = json });
            cmd.ExecuteNonQuery();
        }
        catch (NpgsqlException ex)
        {
            _logger.LogWarning(ex, "Could not save conversation");
        }
    }

    public void Clear()
    {
        try
        {
            using var conn = new NpgsqlConnection(_connectionString);
            conn.Open();
            using var cmd = new NpgsqlCommand("DELETE FROM conversations WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("id", _conversationId);
            cmd.ExecuteNonQuery();
        }
        catch (NpgsqlException ex)
        {
            _logger.LogWarning(ex, "Could not clear conversation");
        }
    }
}
