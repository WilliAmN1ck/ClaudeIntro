namespace ChatBot;

/// <summary>
/// Context-window management: keep only the most recent turns. Pure and
/// side-effect-free so it can be unit-tested without the API.
/// </summary>
public static class HistoryTrimmer
{
    /// <summary>
    /// Returns the most recent <paramref name="max"/> turns, ensuring the result
    /// starts with a user turn (the API requires the first message to be the user's).
    /// <paramref name="max"/> &lt;= 0 means no trimming.
    /// </summary>
    public static IReadOnlyList<StoredTurn> Trim(IReadOnlyList<StoredTurn> turns, int max)
    {
        if (max <= 0 || turns.Count <= max)
            return turns;

        int start = turns.Count - max;
        while (start < turns.Count &&
               turns[start].Role.Equals("assistant", StringComparison.OrdinalIgnoreCase))
        {
            start++;
        }

        return turns.Skip(start).ToList();
    }
}
