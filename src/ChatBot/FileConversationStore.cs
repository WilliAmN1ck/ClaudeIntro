using System.Text.Json;
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

    public FileConversationStore(IOptions<ChatOptions> options)
    {
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
            Console.Error.WriteLine($"Warning: could not save history: {ex.Message}");
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
            Console.Error.WriteLine($"Warning: could not clear history: {ex.Message}");
        }
    }
}
