using ChatBot;
using Xunit;

namespace ChatBot.Tests;

public class ConversationSlugTests
{
    [Theory]
    [InlineData("default", "default")]
    [InlineData("Trip Planning", "trip-planning")]
    [InlineData("  Hello   World  ", "hello-world")]
    [InlineData("My Trip!!!", "my-trip")]
    [InlineData("a/b\\c", "a-b-c")]
    [InlineData("***", "")]
    [InlineData("", "")]
    public void Slugify_normalizes(string input, string expected) =>
        Assert.Equal(expected, ConversationSlug.Slugify(input));

    [Fact]
    public void MakeUnique_returns_slug_when_free() =>
        Assert.Equal("trip", ConversationSlug.MakeUnique("trip", new[] { "default", "work" }));

    [Fact]
    public void MakeUnique_appends_suffix_on_collision() =>
        Assert.Equal("trip-2", ConversationSlug.MakeUnique("trip", new[] { "trip" }));

    [Fact]
    public void MakeUnique_skips_taken_suffixes() =>
        Assert.Equal("trip-3", ConversationSlug.MakeUnique("trip", new[] { "trip", "trip-2" }));

    [Fact]
    public void MakeUnique_blank_falls_back_to_chat() =>
        Assert.Equal("chat-1", ConversationSlug.MakeUnique("", new[] { "default" }));

    [Fact]
    public void MakeUnique_dedupes_case_insensitively() =>
        Assert.Equal("trip-2", ConversationSlug.MakeUnique("Trip", new[] { "trip" }));
}
