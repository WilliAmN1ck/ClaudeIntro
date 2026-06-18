namespace ChatBot;

/// <summary>
/// Parses a raw console line into a <see cref="ChatCommand"/>. Pure (no I/O), so the host's
/// command handling is driven by a single well-tested function.
/// </summary>
/// <remarks>
/// The bare words <c>exit</c>/<c>quit</c>/<c>clear</c> are preserved for continuity with the
/// original host; all conversation-management verbs use a leading <c>/</c> so they never
/// shadow a message the user actually wants to send.
/// </remarks>
public static class ChatCommandParser
{
    public static ChatCommand Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
            return new ChatCommand.Empty();

        string trimmed = input.Trim();

        if (trimmed.Equals("exit", StringComparison.OrdinalIgnoreCase) ||
            trimmed.Equals("quit", StringComparison.OrdinalIgnoreCase))
            return new ChatCommand.Exit();
        if (trimmed.Equals("clear", StringComparison.OrdinalIgnoreCase))
            return new ChatCommand.ClearCurrent();

        if (trimmed[0] != '/')
            return new ChatCommand.Send(trimmed);

        // Slash command: split the verb from its argument remainder.
        string body = trimmed[1..];
        int space = body.IndexOf(' ');
        string verb = (space < 0 ? body : body[..space]).ToLowerInvariant();
        string arg = (space < 0 ? string.Empty : body[(space + 1)..]).Trim();

        return verb switch
        {
            "help" or "h" or "?" => new ChatCommand.Help(),
            "exit" or "quit" or "q" => new ChatCommand.Exit(),
            "clear" => new ChatCommand.ClearCurrent(),
            "list" or "ls" => new ChatCommand.List(),
            "new" => new ChatCommand.New(arg.Length == 0 ? null : arg),
            "switch" or "use" => arg.Length == 0
                ? new ChatCommand.Unknown(trimmed)
                : new ChatCommand.Switch(arg),
            "rename" => arg.Length == 0
                ? new ChatCommand.Unknown(trimmed)
                : new ChatCommand.Rename(arg),
            "delete" or "del" or "rm" => new ChatCommand.Delete(arg.Length == 0 ? null : arg),
            _ => new ChatCommand.Unknown(trimmed),
        };
    }
}
