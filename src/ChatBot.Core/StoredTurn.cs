using Anthropic.Models.Messages;

namespace ChatBot;

/// <summary>One persisted conversation turn (role + text).</summary>
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
