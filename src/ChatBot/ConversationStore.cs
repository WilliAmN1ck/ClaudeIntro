using System.Text.Json;
using Anthropic.Models.Messages;

namespace ChatBot;

/// <summary>One persisted conversation turn.</summary>
public sealed record StoredTurn(string Role, string Text)
{
    /// <summary>Converts this turn into an SDK <see cref="MessageParam"/> for sending.</summary>
    public MessageParam ToMessage()
    {
        Role role = Role.Equals("assistant", StringComparison.OrdinalIgnoreCase)
            ? Anthropic.Models.Messages.Role.Assistant
            : Anthropic.Models.Messages.Role.User;
        return new MessageParam { Role = role, Content = Text };
    }
}

/// <summary>
/// Loads and saves conversation history as JSON. Works in terms of a simple
/// <see cref="StoredTurn"/> DTO so we never depend on the SDK's internal
/// serialization shape.
/// </summary>
public static class ConversationStore
{
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

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

    /// <summary>True if a non-empty saved conversation exists at <paramref name="path"/>.</summary>
    public static bool Exists(string path) => File.Exists(path) && new FileInfo(path).Length > 0;

    /// <summary>
    /// Loads saved turns. Returns an empty list if the file is missing or unreadable
    /// (corrupt files are treated as "start fresh" rather than crashing).
    /// </summary>
    public static List<StoredTurn> Load(string path)
    {
        try
        {
            if (!Exists(path))
                return new List<StoredTurn>();

            return JsonSerializer.Deserialize<List<StoredTurn>>(File.ReadAllText(path))
                   ?? new List<StoredTurn>();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new List<StoredTurn>();
        }
    }

    /// <summary>Serializes the conversation to disk, creating the directory if needed.</summary>
    public static void Save(string path, IEnumerable<StoredTurn> turns)
    {
        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(path, JsonSerializer.Serialize(turns, Options));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Warning: could not save history: {ex.Message}");
        }
    }

    /// <summary>Deletes the saved conversation, if present.</summary>
    public static void Clear(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Warning: could not clear history: {ex.Message}");
        }
    }
}
