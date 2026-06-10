namespace ChatBot;

/// <summary>
/// Strongly-typed chatbot settings, bound from the <c>ChatBot</c> configuration
/// section (appsettings.json, environment variables, and command-line flags).
/// </summary>
public sealed class ChatOptions
{
    public const string SectionName = "ChatBot";

    /// <summary>Claude model id, e.g. <c>claude-opus-4-8</c>.</summary>
    public string Model { get; set; } = "claude-opus-4-8";

    /// <summary>Maximum output tokens per reply (must be >= 1).</summary>
    public long MaxTokens { get; set; } = 4096;

    /// <summary>Recent-message cap sent per request; <c>0</c> means unlimited.</summary>
    public int MaxHistoryMessages { get; set; } = 40;

    /// <summary>Inline system prompt. Takes precedence over <see cref="SystemPromptFile"/>.</summary>
    public string? SystemPrompt { get; set; }

    /// <summary>Path to a file whose contents become the system prompt.</summary>
    public string? SystemPromptFile { get; set; }

    /// <summary>Where the conversation is saved/loaded; defaults to the app-data history file.</summary>
    public string? HistoryPath { get; set; }

    /// <summary>Use beta server-side compaction instead of the count-based history trim.</summary>
    public bool Compaction { get; set; }
}
