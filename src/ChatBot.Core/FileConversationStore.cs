using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ChatBot;

/// <summary>
/// Stores the conversation as a JSON file. Path comes from
/// <see cref="ChatOptions.HistoryPath"/>, defaulting to the per-user app-data file.
/// </summary>
public sealed class FileConversationStore : IConversationStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };
    private readonly string _path;
    private readonly ILogger<FileConversationStore> _logger;

    public FileConversationStore(IOptions<ChatOptions> options, ILogger<FileConversationStore> logger)
    {
        _logger = logger;
        string? configured = options.Value.HistoryPath;
        _path = string.IsNullOrWhiteSpace(configured) ? DefaultPath : configured;
    }

    /// <summary>Default path: %APPDATA%/ClaudeIntro/history.json (or platform equivalent).</summary>
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

    public bool Exists() => File.Exists(_path) && new FileInfo(_path).Length > 0;

    public List<StoredTurn> Load()
    {
        try
        {
            if (!Exists())
                return new List<StoredTurn>();

            return JsonSerializer.Deserialize<List<StoredTurn>>(File.ReadAllText(_path))
                   ?? new List<StoredTurn>();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // Corrupt or unreadable history — start fresh rather than crash.
            _logger.LogWarning(ex, "Could not load history from {Path}; starting fresh", _path);
            return new List<StoredTurn>();
        }
    }

    public void Save(IEnumerable<StoredTurn> turns)
    {
        try
        {
            string? dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(_path, JsonSerializer.Serialize(turns, SerializerOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not save history to {Path}", _path);
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(_path))
                File.Delete(_path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.LogWarning(ex, "Could not clear history at {Path}", _path);
        }
    }
}
