using System.Text.Json;
using ChatBot;
using ChatBot.Tools;
using Xunit;

namespace ChatBot.Tests;

public class ToolTests
{
    private static IReadOnlyDictionary<string, JsonElement> Args(object value) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(JsonSerializer.Serialize(value))!;

    [Fact]
    public async Task CurrentTime_returns_parseable_timestamp()
    {
        var tool = new CurrentTimeTool();
        string result = await tool.ExecuteAsync(Args(new { }), CancellationToken.None);

        Assert.True(DateTimeOffset.TryParse(result, out _));
        Assert.Empty(tool.Required);
    }

    [Fact]
    public async Task RollDice_returns_value_in_range()
    {
        var tool = new RollDiceTool();
        for (int i = 0; i < 50; i++)
        {
            string result = await tool.ExecuteAsync(Args(new { sides = 6 }), CancellationToken.None);
            int roll = int.Parse(result);
            Assert.InRange(roll, 1, 6);
        }
    }

    [Fact]
    public async Task RollDice_rejects_invalid_sides()
    {
        var tool = new RollDiceTool();

        string tooFew = await tool.ExecuteAsync(Args(new { sides = 1 }), CancellationToken.None);
        string missing = await tool.ExecuteAsync(Args(new { }), CancellationToken.None);

        Assert.StartsWith("Error", tooFew);
        Assert.StartsWith("Error", missing);
    }

    [Fact]
    public void Tool_metadata_is_well_formed()
    {
        var dice = new RollDiceTool();
        Assert.Equal("roll_dice", dice.Name);
        Assert.False(string.IsNullOrWhiteSpace(dice.Description));
        Assert.Contains("sides", dice.Properties.Keys);
        Assert.Contains("sides", dice.Required);
    }
}
