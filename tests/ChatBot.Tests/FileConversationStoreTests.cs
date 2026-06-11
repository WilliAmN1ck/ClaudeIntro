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
    public async Task Save_then_load_round_trips()
    {
        var store = NewStore();
        var turns = new List<StoredTurn> { new("user", "hi"), new("assistant", "hello") };

        await store.SaveAsync(turns);
        var loaded = await store.LoadAsync();

        Assert.Equal(2, loaded.Count);
        Assert.Equal("user", loaded[0].Role);
        Assert.Equal("hello", loaded[1].Text);
    }

    [Fact]
    public async Task Exists_reflects_file_state()
    {
        var store = NewStore();
        Assert.False(await store.ExistsAsync());

        await store.SaveAsync(new List<StoredTurn> { new("user", "hi") });
        Assert.True(await store.ExistsAsync());
    }

    [Fact]
    public async Task Clear_removes_saved_history()
    {
        var store = NewStore();
        await store.SaveAsync(new List<StoredTurn> { new("user", "hi") });

        await store.ClearAsync();

        Assert.False(await store.ExistsAsync());
        Assert.Empty(await store.LoadAsync());
    }

    [Fact]
    public async Task Corrupt_file_loads_as_empty()
    {
        await File.WriteAllTextAsync(_path, "{ this is not valid json ]");
        var store = NewStore();

        Assert.Empty(await store.LoadAsync());
    }

    public void Dispose()
    {
        if (File.Exists(_path))
            File.Delete(_path);
    }
}
