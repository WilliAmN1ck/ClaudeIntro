namespace ChatBot;

/// <summary>
/// A console-agnostic chat engine. Implementations own the conversation history
/// and talk to the Claude API; hosts (console, web, GUI) drive them via
/// <see cref="SendAsync"/> and render the streamed text however they like.
/// </summary>
public interface IChatService
{
    /// <summary>Effective model id in use.</summary>
    string Model { get; }

    /// <summary>Effective max output tokens per reply.</summary>
    long MaxTokens { get; }

    /// <summary>Resolved system prompt for this session.</summary>
    string SystemPrompt { get; }

    /// <summary>The conversation so far, as plain role/text turns (for persistence/inspection).</summary>
    IReadOnlyList<StoredTurn> History { get; }

    /// <summary>
    /// Sends a user message and streams the assistant reply as text chunks.
    /// The user and (accumulated) assistant turns are appended to <see cref="History"/>.
    /// </summary>
    IAsyncEnumerable<string> SendAsync(string userMessage, CancellationToken cancellationToken = default);

    /// <summary>Drops all in-memory conversation context.</summary>
    void Clear();
}
