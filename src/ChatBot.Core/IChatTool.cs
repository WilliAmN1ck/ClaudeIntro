using System.Text.Json;

namespace ChatBot;

/// <summary>
/// A tool Claude can call. Implement this and register it in DI (as
/// <see cref="IChatTool"/>) to let the model invoke your code mid-conversation.
/// Only the default streaming engine runs tools.
/// </summary>
public interface IChatTool
{
    /// <summary>Unique tool name (snake_case), e.g. <c>get_current_time</c>.</summary>
    string Name { get; }

    /// <summary>What the tool does — Claude uses this to decide when to call it.</summary>
    string Description { get; }

    /// <summary>JSON-schema property definitions, keyed by parameter name (see <see cref="ToolSchema"/>).</summary>
    IReadOnlyDictionary<string, JsonElement> Properties { get; }

    /// <summary>Names of required parameters.</summary>
    IReadOnlyList<string> Required { get; }

    /// <summary>Executes the tool and returns a textual result for the model.</summary>
    Task<string> ExecuteAsync(IReadOnlyDictionary<string, JsonElement> arguments, CancellationToken cancellationToken);
}
