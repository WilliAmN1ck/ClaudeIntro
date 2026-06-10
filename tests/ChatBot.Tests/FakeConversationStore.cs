using ChatBot;

namespace ChatBot.Tests;

/// <summary>In-memory <see cref="IConversationStore"/> for tests.</summary>
internal sealed class FakeConversationStore : IConversationStore
{
    private List<StoredTurn> _data;

    public bool Cleared { get; private set; }
    public IReadOnlyList<StoredTurn> Saved => _data;

    public FakeConversationStore(IEnumerable<StoredTurn>? seed = null) => _data = seed?.ToList() ?? new();

    public bool Exists() => _data.Count > 0;
    public List<StoredTurn> Load() => new(_data);
    public void Save(IEnumerable<StoredTurn> turns) => _data = turns.ToList();
    public void Clear()
    {
        _data.Clear();
        Cleared = true;
    }
}
