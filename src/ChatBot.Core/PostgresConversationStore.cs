using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace ChatBot;

/// <summary>
/// Stores the conversation in PostgreSQL as a single <c>jsonb</c> row keyed by
/// <see cref="ChatOptions.ConversationId"/>. Uses Npgsql's async APIs so it never
/// blocks the calling thread. The table is created on first use.
/// </summary>
public sealed class PostgresConversationStore : IConversationStore
{
    private readonly string _connectionString;
    private readonly string _conversationId;
    private readonly ILogger<PostgresConversationStore> _logger;
    private readonly SemaphoreSlim _initGate = new(1, 1);
    private bool _initialized;

    public PostgresConversationStore(IOptions<ChatOptions> options, ILogger<PostgresConversationStore> logger)
    {
        _logger = logger;
        ChatOptions o = options.Value;

        if (string.IsNullOrWhiteSpace(o.PostgresConnectionString))
            throw new InvalidOperationException(
                "PostgresConnectionString is required when Store is 'postgres'.");

        _connectionString = o.PostgresConnectionString;
        _conversationId = string.IsNullOrWhiteSpace(o.ConversationId) ? "default" : o.ConversationId;
    }

    // Creates the table once. A connection/credential problem surfaces here as a clear
    // error (the host catches InvalidOperationException) rather than empty history later.
    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
            return;

        await _initGate.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
                return;

            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);
            await using var cmd = new NpgsqlCommand(
                """
                CREATE TABLE IF NOT EXISTS conversations (
                    id text PRIMARY KEY,
                    turns jsonb NOT NULL,
                    updated_at timestamptz NOT NULL DEFAULT now()
                )
                """, conn);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
            _initialized = true;
        }
        catch (NpgsqlException ex)
        {
            throw new InvalidOperationException($"Could not connect to PostgreSQL: {ex.Message}", ex);
        }
        finally
        {
            _initGate.Release();
        }
    }

    public async Task<bool> ExistsAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);
            await using var cmd = new NpgsqlCommand(
                "SELECT jsonb_array_length(turns) FROM conversations WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("id", _conversationId);
            return await cmd.ExecuteScalarAsync(cancellationToken) is int length && length > 0;
        }
        catch (NpgsqlException ex)
        {
            _logger.LogWarning(ex, "Could not check conversation existence");
            return false;
        }
    }

    public async Task<List<StoredTurn>> LoadAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);
            await using var cmd = new NpgsqlCommand("SELECT turns FROM conversations WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("id", _conversationId);
            if (await cmd.ExecuteScalarAsync(cancellationToken) is string json)
                return JsonSerializer.Deserialize<List<StoredTurn>>(json) ?? new List<StoredTurn>();
            return new List<StoredTurn>();
        }
        catch (Exception ex) when (ex is NpgsqlException or JsonException)
        {
            _logger.LogWarning(ex, "Could not load conversation; starting fresh");
            return new List<StoredTurn>();
        }
    }

    public async Task SaveAsync(IEnumerable<StoredTurn> turns, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        try
        {
            string json = JsonSerializer.Serialize(turns);
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO conversations (id, turns, updated_at) VALUES (@id, @turns, now())
                ON CONFLICT (id) DO UPDATE SET turns = @turns, updated_at = now()
                """, conn);
            cmd.Parameters.AddWithValue("id", _conversationId);
            cmd.Parameters.Add(new NpgsqlParameter("turns", NpgsqlDbType.Jsonb) { Value = json });
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (NpgsqlException ex)
        {
            _logger.LogWarning(ex, "Could not save conversation");
        }
    }

    public async Task ClearAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);
            await using var cmd = new NpgsqlCommand("DELETE FROM conversations WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("id", _conversationId);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (NpgsqlException ex)
        {
            _logger.LogWarning(ex, "Could not clear conversation");
        }
    }
}
