namespace ChatBot;

/// <summary>
/// Persistence for a conversation. Implementations decide where/how turns are
/// stored (JSON file, PostgreSQL, …); the location is fixed at construction, not
/// passed per call. Async so database-backed stores don't block the calling thread.
/// </summary>
public interface IConversationStore
{
    /// <summary>True if a non-empty saved conversation exists.</summary>
    Task<bool> ExistsAsync(CancellationToken cancellationToken = default);

    /// <summary>Loads saved turns, or an empty list if none/unreadable.</summary>
    Task<List<StoredTurn>> LoadAsync(CancellationToken cancellationToken = default);

    /// <summary>Persists the given turns, replacing any prior contents.</summary>
    Task SaveAsync(IEnumerable<StoredTurn> turns, CancellationToken cancellationToken = default);

    /// <summary>Deletes the saved conversation, if any.</summary>
    Task ClearAsync(CancellationToken cancellationToken = default);
}
