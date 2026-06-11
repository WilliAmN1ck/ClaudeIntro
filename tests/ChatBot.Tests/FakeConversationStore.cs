using ChatBot;

namespace ChatBot.Tests;

/// <summary>In-memory <see cref="IConversationStore"/> for tests.</summary>
internal sealed class FakeConversationStore : IConversationStore
{
    private List<StoredTurn> _data;

    public bool Cleared { get; private set; }
    public IReadOnlyList<StoredTurn> Saved => _data;

    public FakeConversationStore(IEnumerable<StoredTurn>? seed = null) => _data = seed?.ToList() ?? new();

    public Task<bool> ExistsAsync(CancellationToken cancellationToken = default) => Task.FromResult(_data.Count > 0);

    public Task<List<StoredTurn>> LoadAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new List<StoredTurn>(_data));

    public Task SaveAsync(IEnumerable<StoredTurn> turns, CancellationToken cancellationToken = default)
    {
        _data = turns.ToList();
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken = default)
    {
        _data.Clear();
        Cleared = true;
        return Task.CompletedTask;
    }
}
