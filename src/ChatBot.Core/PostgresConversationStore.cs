using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace ChatBot;

/// <summary>
/// Stores conversations in PostgreSQL — one <c>jsonb</c> row per conversation in the
/// <c>conversations</c> table, keyed by id. Uses Npgsql's async APIs so it never blocks the
/// calling thread. The table (and the columns added for metadata) are created on first use,
/// so an existing single-conversation table is upgraded in place and its row is preserved.
/// </summary>
public sealed class PostgresConversationStore : IConversationStore
{
    private readonly string _connectionString;
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
    }

    // Creates/upgrades the schema once. A connection/credential problem surfaces here as a clear
    // error (the host catches InvalidOperationException) rather than empty results later.
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
                );
                ALTER TABLE conversations ADD COLUMN IF NOT EXISTS title text;
                ALTER TABLE conversations ADD COLUMN IF NOT EXISTS created_at timestamptz NOT NULL DEFAULT now();
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

    // Projection shared by ListAsync/ReadInfoAsync. The CASE guards a row whose turns jsonb is
    // somehow not an array, so one bad row can't make jsonb_array_length abort the whole list.
    private const string InfoColumns =
        "id, COALESCE(title, id), created_at, updated_at, " +
        "CASE WHEN jsonb_typeof(turns) = 'array' THEN jsonb_array_length(turns) ELSE 0 END";

    public async Task<IReadOnlyList<ConversationInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        var infos = new List<ConversationInfo>();
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);
            await using var cmd = new NpgsqlCommand(
                $"SELECT {InfoColumns} FROM conversations ORDER BY updated_at DESC", conn);
            await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                infos.Add(ReadInfo(reader));
        }
        catch (NpgsqlException ex)
        {
            _logger.LogWarning(ex, "Could not list conversations");
        }

        return infos;
    }

    public async Task<ConversationInfo?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);
            return await ReadInfoAsync(conn, NormalizeId(id), cancellationToken);
        }
        catch (NpgsqlException ex)
        {
            _logger.LogWarning(ex, "Could not get conversation {Id}", id);
            return null;
        }
    }

    public async Task<ConversationInfo> CreateAsync(
        string? id, string? title, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);

            (string resolvedId, string resolvedTitle) = ConversationSlug.Resolve(
                id, title, await ExistingIdsAsync(conn, cancellationToken));

            // Insert an empty conversation; if the id already exists, leave it untouched (ensure).
            await using (var cmd = new NpgsqlCommand(
                """
                INSERT INTO conversations (id, title, turns, created_at, updated_at)
                VALUES (@id, @title, '[]'::jsonb, now(), now())
                ON CONFLICT (id) DO NOTHING
                """, conn))
            {
                cmd.Parameters.AddWithValue("id", resolvedId);
                cmd.Parameters.AddWithValue("title", resolvedTitle);
                await cmd.ExecuteNonQueryAsync(cancellationToken);
            }

            ConversationInfo? info = await ReadInfoAsync(conn, resolvedId, cancellationToken);
            return info ?? new ConversationInfo(resolvedId, resolvedTitle,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 0);
        }
        catch (NpgsqlException ex)
        {
            // Match the store's convention: surface DB failures as InvalidOperationException so the
            // host reports a clean error (at startup or around a command) instead of crashing.
            throw new InvalidOperationException($"Could not create conversation: {ex.Message}", ex);
        }
    }

    public async Task<List<StoredTurn>> LoadAsync(string id, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);
            await using var cmd = new NpgsqlCommand("SELECT turns FROM conversations WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("id", NormalizeId(id));
            if (await cmd.ExecuteScalarAsync(cancellationToken) is string json)
                return JsonSerializer.Deserialize<List<StoredTurn>>(json) ?? new List<StoredTurn>();
            return new List<StoredTurn>();
        }
        catch (Exception ex) when (ex is NpgsqlException or JsonException)
        {
            _logger.LogWarning(ex, "Could not load conversation {Id}; starting fresh", id);
            return new List<StoredTurn>();
        }
    }

    public async Task SaveAsync(
        string id, IEnumerable<StoredTurn> turns, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        try
        {
            string json = JsonSerializer.Serialize(turns);
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);
            // Upsert turns; preserve created_at/title on conflict, default title to the id on insert.
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO conversations (id, title, turns, created_at, updated_at)
                VALUES (@id, @id, @turns, now(), now())
                ON CONFLICT (id) DO UPDATE SET turns = @turns, updated_at = now()
                """, conn);
            cmd.Parameters.AddWithValue("id", NormalizeId(id));
            cmd.Parameters.Add(new NpgsqlParameter("turns", NpgsqlDbType.Jsonb) { Value = json });
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (NpgsqlException ex)
        {
            _logger.LogWarning(ex, "Could not save conversation {Id}", id);
        }
    }

    public async Task RenameAsync(string id, string title, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(title))
            return;
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);
            await using var cmd = new NpgsqlCommand(
                "UPDATE conversations SET title = @title WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("id", NormalizeId(id));
            cmd.Parameters.AddWithValue("title", title.Trim());
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (NpgsqlException ex)
        {
            _logger.LogWarning(ex, "Could not rename conversation {Id}", id);
        }
    }

    public async Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        try
        {
            await using var conn = new NpgsqlConnection(_connectionString);
            await conn.OpenAsync(cancellationToken);
            await using var cmd = new NpgsqlCommand("DELETE FROM conversations WHERE id = @id", conn);
            cmd.Parameters.AddWithValue("id", NormalizeId(id));
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (NpgsqlException ex)
        {
            _logger.LogWarning(ex, "Could not delete conversation {Id}", id);
        }
    }

    // --- helpers ---------------------------------------------------------------

    // Ids are slugified at every entry point, consistent with FileConversationStore, so the same
    // user input (e.g. '/switch Default') resolves to the same conversation across both backends.
    private static string NormalizeId(string id) => ConversationSlug.Slugify(id);

    private static ConversationInfo ReadInfo(NpgsqlDataReader reader) => new(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetFieldValue<DateTimeOffset>(2),
        reader.GetFieldValue<DateTimeOffset>(3),
        reader.GetInt32(4));

    // Expects an already-normalized id (callers normalize at the boundary).
    private static async Task<ConversationInfo?> ReadInfoAsync(
        NpgsqlConnection conn, string normalizedId, CancellationToken cancellationToken)
    {
        await using var cmd = new NpgsqlCommand(
            $"SELECT {InfoColumns} FROM conversations WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("id", normalizedId);
        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? ReadInfo(reader) : null;
    }

    private static async Task<IEnumerable<string>> ExistingIdsAsync(
        NpgsqlConnection conn, CancellationToken cancellationToken)
    {
        var ids = new List<string>();
        await using var cmd = new NpgsqlCommand("SELECT id FROM conversations", conn);
        await using NpgsqlDataReader reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            ids.Add(reader.GetString(0));
        return ids;
    }
}
