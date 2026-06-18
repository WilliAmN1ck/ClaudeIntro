using ChatBot;
using Xunit;

namespace ChatBot.Tests;

public class ChatCommandParserTests
{
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Blank_is_empty(string input) =>
        Assert.IsType<ChatCommand.Empty>(ChatCommandParser.Parse(input));

    [Theory]
    [InlineData("exit")]
    [InlineData("QUIT")]
    [InlineData("/exit")]
    [InlineData("/quit")]
    public void Exit_variants(string input) =>
        Assert.IsType<ChatCommand.Exit>(ChatCommandParser.Parse(input));

    [Theory]
    [InlineData("clear")]
    [InlineData("/clear")]
    public void Clear_variants(string input) =>
        Assert.IsType<ChatCommand.ClearCurrent>(ChatCommandParser.Parse(input));

    [Fact]
    public void Plain_text_is_send()
    {
        var cmd = Assert.IsType<ChatCommand.Send>(ChatCommandParser.Parse("  hello there "));
        Assert.Equal("hello there", cmd.Text);
    }

    [Theory]
    [InlineData("/list")]
    [InlineData("/ls")]
    public void List_variants(string input) =>
        Assert.IsType<ChatCommand.List>(ChatCommandParser.Parse(input));

    [Fact]
    public void New_without_title()
    {
        var cmd = Assert.IsType<ChatCommand.New>(ChatCommandParser.Parse("/new"));
        Assert.Null(cmd.Title);
    }

    [Fact]
    public void New_with_title_preserves_case_and_spaces()
    {
        var cmd = Assert.IsType<ChatCommand.New>(ChatCommandParser.Parse("/new Trip Planning"));
        Assert.Equal("Trip Planning", cmd.Title);
    }

    [Fact]
    public void Switch_requires_id()
    {
        Assert.IsType<ChatCommand.Unknown>(ChatCommandParser.Parse("/switch"));
        var cmd = Assert.IsType<ChatCommand.Switch>(ChatCommandParser.Parse("/switch  trip "));
        Assert.Equal("trip", cmd.Id);
    }

    [Fact]
    public void Rename_requires_title()
    {
        Assert.IsType<ChatCommand.Unknown>(ChatCommandParser.Parse("/rename"));
        var cmd = Assert.IsType<ChatCommand.Rename>(ChatCommandParser.Parse("/rename New Name"));
        Assert.Equal("New Name", cmd.Title);
    }

    [Fact]
    public void Delete_optional_id()
    {
        Assert.Null(Assert.IsType<ChatCommand.Delete>(ChatCommandParser.Parse("/delete")).Id);
        Assert.Equal("trip", Assert.IsType<ChatCommand.Delete>(ChatCommandParser.Parse("/delete trip")).Id);
    }

    [Theory]
    [InlineData("/help")]
    [InlineData("/?")]
    public void Help_variants(string input) =>
        Assert.IsType<ChatCommand.Help>(ChatCommandParser.Parse(input));

    [Fact]
    public void Unknown_slash_command_keeps_input()
    {
        var cmd = Assert.IsType<ChatCommand.Unknown>(ChatCommandParser.Parse("/frobnicate now"));
        Assert.Equal("/frobnicate now", cmd.Input);
    }
}
