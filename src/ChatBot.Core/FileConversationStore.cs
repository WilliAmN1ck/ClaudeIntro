using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChatBot;

/// <summary>
/// Stores each conversation as a JSON document under a <c>conversations</c> directory beside
/// <see cref="ChatOptions.HistoryPath"/> (default <c>%APPDATA%/ClaudeIntro/conversations/</c>).
/// On first use it migrates a pre-existing single-file <c>history.json</c> into a conversation
/// named <c>default</c>, so upgrading users keep their chat.
/// </summary>
public sealed class FileConversationStore : IConversationStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _legacyPath;
    private readonly string _directory;
    private readonly ILogger<FileConversationStore> _logger;
    private readonly SemaphoreSlim _migrateGate = new(1, 1);
    private bool _migrated;

    public FileConversationStore(IOptions<ChatOptions> options, ILogger<FileConversationStore> logger)
    {
        _logger = logger;
        string? configured = options.Value.HistoryPath;
        _legacyPath = string.IsNullOrWhiteSpace(configured) ? DefaultPath : configured;
        string? baseDir = Path.GetDirectoryName(_legacyPath);
        _directory = Path.Combine(string.IsNullOrEmpty(baseDir) ? "." : baseDir, "conversations");
    }

    /// <summary>Legacy single-file history path: %APPDATA%/ClaudeIntro/history.json.</summary>
    public static string DefaultPath
    {
        get
        {
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "ClaudeIntro");
            return Path.Combine(dir, "history.json");
        }
    }

    /// <summary>Persisted shape of one conversation file.</summary>
    private sealed record ConversationDocument(
        string Id,
        string Title,
        DateTimeOffset CreatedAt,
        DateTimeOffset UpdatedAt,
        List<StoredTurn> Turns);

    public async Task<IReadOnlyList<ConversationInfo>> ListAsync(CancellationToken cancellationToken = default)
    {
        await EnsureMigratedAsync(cancellationToken);

        var infos = new List<ConversationInfo>();
        if (!Directory.Exists(_directory))
            return infos;

        foreach (string file in Directory.EnumerateFiles(_directory, "*.json"))
        {
            ConversationDocument? doc = await ReadDocumentAsync(file, cancellationToken);
            if (doc is not null)
                infos.Add(ToInfo(doc));
        }

        infos.Sort((a, b) => b.UpdatedAt.CompareTo(a.UpdatedAt));
        return infos;
    }

    public async Task<ConversationInfo?> GetAsync(string id, CancellationToken cancellationToken = default)
    {
        await EnsureMigratedAsync(cancellationToken);
        ConversationDocument? doc = await ReadDocumentAsync(PathFor(NormalizeId(id)), cancellationToken);
        return doc is null ? null : ToInfo(doc);
    }

    public async Task<ConversationInfo> CreateAsync(
        string? id, string? title, CancellationToken cancellationToken = default)
    {
        await EnsureMigratedAsync(cancellationToken);

        (string resolvedId, string resolvedTitle) = ConversationSlug.Resolve(id, title, ExistingIds());

        ConversationDocument? existing = await ReadDocumentAsync(PathFor(resolvedId), cancellationToken);
        if (existing is not null)
            return ToInfo(existing); // idempotent ensure

        DateTimeOffset now = DateTimeOffset.UtcNow;
        var doc = new ConversationDocument(resolvedId, resolvedTitle, now, now, new List<StoredTurn>());
        await WriteDocumentAsync(doc, cancellationToken);
        return ToInfo(doc);
    }

    public async Task<List<StoredTurn>> LoadAsync(string id, CancellationToken cancellationToken = default)
    {
        await EnsureMigratedAsync(cancellationToken);
        ConversationDocument? doc = await ReadDocumentAsync(PathFor(NormalizeId(id)), cancellationToken);
        return doc?.Turns ?? new List<StoredTurn>();
    }

    public async Task SaveAsync(
        string id, IEnumerable<StoredTurn> turns, CancellationToken cancellationToken = default)
    {
        await EnsureMigratedAsync(cancellationToken);
        string convId = NormalizeId(id);
        var list = turns.ToList();
        DateTimeOffset now = DateTimeOffset.UtcNow;

        ConversationDocument? existing = await ReadDocumentAsync(PathFor(convId), cancellationToken);
        ConversationDocument doc = existing is null
            ? new ConversationDocument(convId, convId, now, now, list)
            : existing with { Turns = list, UpdatedAt = now };

        await WriteDocumentAsync(doc, cancellationToken);
    }

    public async Task RenameAsync(string id, string title, CancellationToken cancellationToken = default)
    {
        await EnsureMigratedAsync(cancellationToken);
        if (string.IsNullOrWhiteSpace(title))
            return;

        ConversationDocument? existing = await ReadDocumentAsync(PathFor(NormalizeId(id)), cancellationToken);
        if (existing is null)
            return;

        await WriteDocumentAsync(existing with { Title = title.Trim() }, cancellationToken);
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        try
        {
            string path = PathFor(NormalizeId(id));
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not delete conversation {Id}", id);
        }

        return Task.CompletedTask;
    }

    // --- helpers ---------------------------------------------------------------

    private static ConversationInfo ToInfo(ConversationDocument d) =>
        new(d.Id, d.Title, d.CreatedAt, d.UpdatedAt, d.Turns.Count);

    // Ids double as file names; each public method slugifies its id at the boundary so paths
    // are on-disk-safe (no traversal) and consistent with the other stores.
    private static string NormalizeId(string id) => ConversationSlug.Slugify(id);

    // Assumes an already-normalized id (callers normalize once, at the boundary).
    private string PathFor(string normalizedId) => Path.Combine(_directory, normalizedId + ".json");

    private IEnumerable<string> ExistingIds()
    {
        if (!Directory.Exists(_directory))
            return Array.Empty<string>();
        return Directory.EnumerateFiles(_directory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(name => !string.IsNullOrEmpty(name))!
            .Cast<string>();
    }

    private async Task<ConversationDocument?> ReadDocumentAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
            return null;
        try
        {
            string json = await File.ReadAllTextAsync(path, cancellationToken);
            return JsonSerializer.Deserialize<ConversationDocument>(json);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not read conversation file {Path}; skipping", path);
            return null;
        }
    }

    private async Task WriteDocumentAsync(ConversationDocument doc, CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(_directory);
            string json = JsonSerializer.Serialize(doc, SerializerOptions);
            await File.WriteAllTextAsync(PathFor(doc.Id), json, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not save conversation {Id}", doc.Id);
        }
    }

    // Runs once. The conversations directory's absence marks a first run: if a legacy
    // history file exists, fold it into a 'default' conversation. Keying migration on the
    // directory (not on default.json) means deleting 'default' later never resurrects it.
    private async Task EnsureMigratedAsync(CancellationToken cancellationToken)
    {
        if (_migrated)
            return;

        await _migrateGate.WaitAsync(cancellationToken);
        try
        {
            if (_migrated)
                return;

            bool firstRun = !Directory.Exists(_directory);
            Directory.CreateDirectory(_directory);

            if (firstRun && File.Exists(_legacyPath) && new FileInfo(_legacyPath).Length > 0)
                await MigrateLegacyHistoryAsync(cancellationToken);

            _migrated = true;
        }
        finally
        {
            _migrateGate.Release();
        }
    }

    private async Task MigrateLegacyHistoryAsync(CancellationToken cancellationToken)
    {
        try
        {
            string json = await File.ReadAllTextAsync(_legacyPath, cancellationToken);
            var turns = JsonSerializer.Deserialize<List<StoredTurn>>(json) ?? new List<StoredTurn>();
            if (turns.Count == 0)
                return;

            DateTimeOffset now = DateTimeOffset.UtcNow;
            await WriteDocumentAsync(new ConversationDocument("default", "default", now, now, turns), cancellationToken);
            _logger.LogInformation(
                "Migrated legacy history ({Count} turns) into conversation 'default'", turns.Count);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not migrate legacy history from {Path}", _legacyPath);
        }
    }
}
