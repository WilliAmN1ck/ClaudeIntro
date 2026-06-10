using System.Text.Json;

namespace ChatBot.Tools;

/// <summary>Returns the current date and time — information the model can't know on its own.</summary>
public sealed class CurrentTimeTool : IChatTool
{
    public string Name => "get_current_time";
    public string Description => "Returns the current local date and time in ISO 8601 format.";
    public IReadOnlyDictionary<string, JsonElement> Properties => new Dictionary<string, JsonElement>();
    public IReadOnlyList<string> Required => Array.Empty<string>();

    public Task<string> ExecuteAsync(
        IReadOnlyDictionary<string, JsonElement> arguments, CancellationToken cancellationToken) =>
        Task.FromResult(DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz"));
}
