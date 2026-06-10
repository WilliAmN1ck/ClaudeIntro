namespace ChatBot;

/// <summary>
/// Persistence for a conversation. Implementations decide where/how turns are
/// stored (JSON file, SQLite, a database, …); the location is fixed at construction,
/// not passed per call.
/// </summary>
public interface IConversationStore
{
    /// <summary>True if a non-empty saved conversation exists.</summary>
    bool Exists();

    /// <summary>Loads saved turns, or an empty list if none/unreadable.</summary>
    List<StoredTurn> Load();

    /// <summary>Persists the given turns, replacing any prior contents.</summary>
    void Save(IEnumerable<StoredTurn> turns);

    /// <summary>Deletes the saved conversation, if any.</summary>
    void Clear();
}
