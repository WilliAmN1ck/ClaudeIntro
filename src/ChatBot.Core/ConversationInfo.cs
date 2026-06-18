namespace ChatBot;

/// <summary>
/// Metadata describing one stored conversation, independent of its turns. Returned by
/// <see cref="IConversationStore"/> listing/lookup so hosts can render and select conversations.
/// </summary>
public sealed record ConversationInfo(
    string Id,
    string Title,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int TurnCount);
