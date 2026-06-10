using System.Text.Json;
using Anthropic.Models.Messages;

namespace ChatBot;

/// <summary>
/// Holds the registered tools, builds their SDK definitions, and dispatches calls.
/// Pure (no I/O beyond the tool itself) so it can be unit-tested directly.
/// </summary>
public sealed class ToolInvoker
{
    private readonly Dictionary<string, IChatTool> _byName;

    public IReadOnlyList<IChatTool> Tools { get; }

    /// <summary>SDK tool definitions for the request's <c>Tools</c> parameter.</summary>
    public IReadOnlyList<ToolUnion> ToolUnions { get; }

    public ToolInvoker(IEnumerable<IChatTool> tools)
    {
        Tools = tools.ToList();
        _byName = Tools.ToDictionary(t => t.Name, StringComparer.Ordinal);

        var unions = new List<ToolUnion>(Tools.Count);
        foreach (IChatTool tool in Tools)
            unions.Add(ToSdkTool(tool));
        ToolUnions = unions;
    }

    /// <summary>
    /// Executes the named tool and returns its textual result. Unknown tools and tool
    /// exceptions become error strings for the model rather than throwing
    /// (cancellation still propagates).
    /// </summary>
    public async Task<string> InvokeAsync(
        string name, IReadOnlyDictionary<string, JsonElement> input, CancellationToken cancellationToken)
    {
        if (!_byName.TryGetValue(name, out IChatTool? tool))
            return $"Error: unknown tool '{name}'.";

        try
        {
            return await tool.ExecuteAsync(input, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return $"Error executing '{name}': {ex.Message}";
        }
    }

    private static Tool ToSdkTool(IChatTool tool) => new()
    {
        Name = tool.Name,
        Description = tool.Description,
        InputSchema = new()
        {
            Properties = tool.Properties.ToDictionary(kv => kv.Key, kv => kv.Value),
            Required = tool.Required.ToList(),
        },
    };
}
