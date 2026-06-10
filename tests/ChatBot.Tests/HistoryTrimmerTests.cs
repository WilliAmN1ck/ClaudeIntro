using ChatBot;
using Xunit;

namespace ChatBot.Tests;

public class HistoryTrimmerTests
{
    private static List<StoredTurn> Conversation(int turns)
    {
        var list = new List<StoredTurn>();
        for (int i = 0; i < turns; i++)
            list.Add(new StoredTurn(i % 2 == 0 ? "user" : "assistant", $"m{i}"));
        return list;
    }

    [Fact]
    public void Max_zero_means_no_trim()
    {
        var turns = Conversation(6);
        Assert.Same(turns, HistoryTrimmer.Trim(turns, 0));
    }

    [Fact]
    public void Returns_same_when_within_limit()
    {
        var turns = Conversation(4);
        Assert.Same(turns, HistoryTrimmer.Trim(turns, 4));
        Assert.Same(turns, HistoryTrimmer.Trim(turns, 10));
    }

    [Fact]
    public void Trims_to_most_recent_and_starts_on_user()
    {
        // 6 turns: user, assistant, user, assistant, user, assistant
        var turns = Conversation(6);

        // Last 3 would be assistant, user, assistant → drop the leading assistant.
        var trimmed = HistoryTrimmer.Trim(turns, 3);

        Assert.Equal("user", trimmed[0].Role);
        Assert.Equal(2, trimmed.Count);
        Assert.Equal("m4", trimmed[0].Text);
        Assert.Equal("m5", trimmed[1].Text);
    }

    [Fact]
    public void Keeps_window_when_it_already_starts_on_user()
    {
        var turns = Conversation(4); // user, assistant, user, assistant
        var trimmed = HistoryTrimmer.Trim(turns, 2); // assistant, ... → adjusts

        // Last 2 = user(m2), assistant(m3) → starts on user, kept as-is.
        Assert.Equal(2, trimmed.Count);
        Assert.Equal("m2", trimmed[0].Text);
    }
}
