using ChatBot;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ChatBot.Tests;

public class FileConversationStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"chat_{Guid.NewGuid():N}.json");

    private FileConversationStore NewStore() =>
        new(Options.Create(new ChatOptions { HistoryPath = _path }), NullLogger<FileConversationStore>.Instance);

    [Fact]
    public void Save_then_load_round_trips()
    {
        var store = NewStore();
        var turns = new List<StoredTurn> { new("user", "hi"), new("assistant", "hello") };

        store.Save(turns);
        var loaded = store.Load();

        Assert.Equal(2, loaded.Count);
        Assert.Equal("user", loaded[0].Role);
        Assert.Equal("hello", loaded[1].Text);
    }

    [Fact]
    public void Exists_reflects_file_state()
    {
        var store = NewStore();
        Assert.False(store.Exists());

        store.Save(new List<StoredTurn> { new("user", "hi") });
        Assert.True(store.Exists());
    }

    [Fact]
    public void Clear_removes_saved_history()
    {
        var store = NewStore();
        store.Save(new List<StoredTurn> { new("user", "hi") });

        store.Clear();

        Assert.False(store.Exists());
        Assert.Empty(store.Load());
    }

    [Fact]
    public void Corrupt_file_loads_as_empty()
    {
        File.WriteAllText(_path, "{ this is not valid json ]");
        var store = NewStore();

        Assert.Empty(store.Load());
    }

    public void Dispose()
    {
        if (File.Exists(_path))
            File.Delete(_path);
    }
}
