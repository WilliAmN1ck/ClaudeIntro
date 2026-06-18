namespace ChatBot;

/// <summary>
/// Persistence for named conversations. Each conversation has a stable id, carries metadata
/// (<see cref="ConversationInfo"/>), and owns a list of turns. Implementations choose the
/// medium (JSON files, PostgreSQL, …). Async so database-backed stores never block the caller.
/// </summary>
public interface IConversationStore
{
    /// <summary>All conversations, most-recently-updated first.</summary>
    Task<IReadOnlyList<ConversationInfo>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Metadata for one conversation, or <c>null</c> if it does not exist.</summary>
    Task<ConversationInfo?> GetAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a conversation and returns its metadata. When <paramref name="id"/> is
    /// <c>null</c> a unique id is derived from <paramref name="title"/> (falling back to
    /// <c>chat-N</c>); when an explicit <paramref name="id"/> already exists the existing
    /// conversation is returned unchanged — an idempotent "ensure it exists".
    /// </summary>
    Task<ConversationInfo> CreateAsync(string? id, string? title, CancellationToken cancellationToken = default);

    /// <summary>Loads a conversation's turns, or an empty list if it is missing/unreadable.</summary>
    Task<List<StoredTurn>> LoadAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists <paramref name="turns"/> for a conversation, replacing its prior turns and
    /// bumping its updated timestamp. Creates the conversation if it does not yet exist.
    /// </summary>
    Task SaveAsync(string id, IEnumerable<StoredTurn> turns, CancellationToken cancellationToken = default);

    /// <summary>Changes a conversation's title; its id is unchanged. No-op if it does not exist.</summary>
    Task RenameAsync(string id, string title, CancellationToken cancellationToken = default);

    /// <summary>Deletes a conversation and its turns. No-op if it does not exist.</summary>
    Task DeleteAsync(string id, CancellationToken cancellationToken = default);
}
