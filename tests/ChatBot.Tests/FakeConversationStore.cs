using ChatBot;

namespace ChatBot.Tests;

/// <summary>In-memory <see cref="IConversationStore"/> for tests.</summary>
internal sealed class FakeConversationStore : IConversationStore
{
    private readonly Dictionary<string, (ConversationInfo Info, List<StoredTurn> Turns)> _store
        = new(StringComparer.OrdinalIgnoreCase);

    public FakeConversationStore()
    {
    }

    /// <summary>Convenience: seed a single conversation up front.</summary>
    public FakeConversationStore(string id, IEnumerable<StoredTurn> turns) => Seed(id, turns);

    /// <summary>Pre-populate a conversation (test setup helper).</summary>
    public void Seed(string id, IEnumerable<StoredTurn> turns, string? title = null)
    {
        var list = turns.ToList();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        _store[id] = (new ConversationInfo(id, title ?? id, now, now, list.Count), list);
    }

    /// <summary>The turns last persisted for <paramref name="id"/> (empty if none).</summary>
    public IReadOnlyList<StoredTurn> SavedFor(string id) =>
        _store.TryGetValue(id, out var entry) ? entry.Turns : Array.Empty<StoredTurn>();

    public Task<IReadOnlyList<ConversationInfo>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ConversationInfo>>(
            _store.Values.Select(e => e.Info).OrderByDescending(i => i.UpdatedAt).ToList());

    public Task<ConversationInfo?> GetAsync(string id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_store.TryGetValue(id, out var entry) ? entry.Info : null);

    public Task<ConversationInfo> CreateAsync(
        string? id, string? title, CancellationToken cancellationToken = default)
    {
        string resolvedId = id ?? ConversationSlug.MakeUnique(ConversationSlug.Slugify(title), _store.Keys);
        if (_store.TryGetValue(resolvedId, out var existing))
            return Task.FromResult(existing.Info);

        DateTimeOffset now = DateTimeOffset.UtcNow;
        var info = new ConversationInfo(
            resolvedId, string.IsNullOrWhiteSpace(title) ? resolvedId : title.Trim(), now, now, 0);
        _store[resolvedId] = (info, new List<StoredTurn>());
        return Task.FromResult(info);
    }

    public Task<List<StoredTurn>> LoadAsync(string id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_store.TryGetValue(id, out var entry)
            ? new List<StoredTurn>(entry.Turns)
            : new List<StoredTurn>());

    public Task SaveAsync(string id, IEnumerable<StoredTurn> turns, CancellationToken cancellationToken = default)
    {
        var list = turns.ToList();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        ConversationInfo info = _store.TryGetValue(id, out var entry)
            ? entry.Info with { UpdatedAt = now, TurnCount = list.Count }
            : new ConversationInfo(id, id, now, now, list.Count);
        _store[id] = (info, list);
        return Task.CompletedTask;
    }

    public Task RenameAsync(string id, string title, CancellationToken cancellationToken = default)
    {
        if (_store.TryGetValue(id, out var entry))
            _store[id] = (entry.Info with { Title = title }, entry.Turns);
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        _store.Remove(id);
        return Task.CompletedTask;
    }
}
