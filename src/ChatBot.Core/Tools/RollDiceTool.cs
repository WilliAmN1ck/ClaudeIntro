using System.Text.Json;

namespace ChatBot.Tools;

/// <summary>Rolls a die with the given number of sides — a simple tool with one argument.</summary>
public sealed class RollDiceTool : IChatTool
{
    public string Name => "roll_dice";
    public string Description => "Rolls a single die and returns the result (1 to the given number of sides).";

    public IReadOnlyDictionary<string, JsonElement> Properties => new Dictionary<string, JsonElement>
    {
        ["sides"] = ToolSchema.Integer("Number of sides on the die (2-1000)."),
    };

    public IReadOnlyList<string> Required => new[] { "sides" };

    public Task<string> ExecuteAsync(
        IReadOnlyDictionary<string, JsonElement> arguments, CancellationToken cancellationToken)
    {
        if (!arguments.TryGetValue("sides", out JsonElement sidesElement) ||
            !sidesElement.TryGetInt32(out int sides) || sides < 2 || sides > 1000)
        {
            return Task.FromResult("Error: 'sides' must be an integer between 2 and 1000.");
        }

        int roll = Random.Shared.Next(1, sides + 1);
        return Task.FromResult(roll.ToString());
    }
}
