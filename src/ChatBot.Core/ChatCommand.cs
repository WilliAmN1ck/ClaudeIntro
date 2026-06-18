namespace ChatBot;

/// <summary>
/// A parsed console line: either a message to send to the model or a conversation-management
/// command. Produced by <see cref="ChatCommandParser"/> so the host can dispatch without
/// re-parsing strings, and so parsing is unit-testable in isolation.
/// </summary>
public abstract record ChatCommand
{
    /// <summary>Send <see cref="Text"/> to the model as a user message.</summary>
    public sealed record Send(string Text) : ChatCommand;

    /// <summary>End the session.</summary>
    public sealed record Exit : ChatCommand;

    /// <summary>Print the command help.</summary>
    public sealed record Help : ChatCommand;

    /// <summary>Empty the current conversation's history (keep the conversation).</summary>
    public sealed record ClearCurrent : ChatCommand;

    /// <summary>List all conversations.</summary>
    public sealed record List : ChatCommand;

    /// <summary>Create and switch to a new conversation; <see cref="Title"/> is optional.</summary>
    public sealed record New(string? Title) : ChatCommand;

    /// <summary>Switch to the conversation with the given id.</summary>
    public sealed record Switch(string Id) : ChatCommand;

    /// <summary>Rename the current conversation.</summary>
    public sealed record Rename(string Title) : ChatCommand;

    /// <summary>Delete a conversation; null <see cref="Id"/> means the current one.</summary>
    public sealed record Delete(string? Id) : ChatCommand;

    /// <summary>An unrecognized slash command (the host shows a hint).</summary>
    public sealed record Unknown(string Input) : ChatCommand;

    /// <summary>Blank input — the host ignores it.</summary>
    public sealed record Empty : ChatCommand;
}
