using ChatBot;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace ChatBot.Tests;

public class FileConversationStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"chat_{Guid.NewGuid():N}");

    private string LegacyPath => Path.Combine(_dir, "history.json");

    private FileConversationStore NewStore() =>
        new(Options.Create(new ChatOptions { HistoryPath = LegacyPath }),
            NullLogger<FileConversationStore>.Instance);

    [Fact]
    public async Task Create_then_get_round_trips()
    {
        var store = NewStore();
        ConversationInfo created = await store.CreateAsync(null, "Trip Planning");

        Assert.Equal("trip-planning", created.Id);
        Assert.Equal("Trip Planning", created.Title);

        ConversationInfo? got = await store.GetAsync("trip-planning");
        Assert.NotNull(got);
        Assert.Equal("Trip Planning", got!.Title);
        Assert.Equal(0, got.TurnCount);
    }

    [Fact]
    public async Task Save_then_load_round_trips()
    {
        var store = NewStore();
        await store.CreateAsync("default", null);
        var turns = new List<StoredTurn> { new("user", "hi"), new("assistant", "hello") };

        await store.SaveAsync("default", turns);

        var loaded = await store.LoadAsync("default");
        Assert.Equal(2, loaded.Count);
        Assert.Equal("hello", loaded[1].Text);
        Assert.Equal(2, (await store.GetAsync("default"))!.TurnCount);
    }

    [Fact]
    public async Task Save_creates_conversation_when_absent()
    {
        var store = NewStore();
        await store.SaveAsync("adhoc", new[] { new StoredTurn("user", "hi") });
        Assert.NotNull(await store.GetAsync("adhoc"));
    }

    [Fact]
    public async Task List_orders_newest_updated_first()
    {
        var store = NewStore();
        await store.CreateAsync("first", null);
        await store.CreateAsync("second", null);
        await store.SaveAsync("first", new[] { new StoredTurn("user", "x") }); // touch 'first'

        var list = await store.ListAsync();
        Assert.Equal("first", list[0].Id);
        Assert.Equal(2, list.Count);
    }

    [Fact]
    public async Task Rename_changes_title_keeps_id()
    {
        var store = NewStore();
        await store.CreateAsync("default", null);

        await store.RenameAsync("default", "My Chat");

        ConversationInfo? info = await store.GetAsync("default");
        Assert.Equal("default", info!.Id);
        Assert.Equal("My Chat", info.Title);
    }

    [Fact]
    public async Task Delete_removes_conversation()
    {
        var store = NewStore();
        await store.CreateAsync("default", null);

        await store.DeleteAsync("default");

        Assert.Null(await store.GetAsync("default"));
        Assert.Empty(await store.ListAsync());
    }

    [Fact]
    public async Task Create_derives_unique_id_on_title_collision()
    {
        var store = NewStore();
        ConversationInfo a = await store.CreateAsync(null, "Trip");
        ConversationInfo b = await store.CreateAsync(null, "Trip");

        Assert.Equal("trip", a.Id);
        Assert.Equal("trip-2", b.Id);
    }

    [Fact]
    public async Task Create_with_existing_id_is_idempotent()
    {
        var store = NewStore();
        await store.SaveAsync("default", new[] { new StoredTurn("user", "hi") });

        ConversationInfo ensured = await store.CreateAsync("default", null); // must not wipe turns

        Assert.Equal(1, ensured.TurnCount);
        Assert.Single(await store.LoadAsync("default"));
    }

    [Fact]
    public async Task Ids_are_path_safe()
    {
        var store = NewStore();
        // A hostile id must not escape the conversations directory.
        await store.SaveAsync("../escape", new[] { new StoredTurn("user", "x") });

        Assert.False(File.Exists(Path.Combine(_dir, "escape.json")));
        Assert.NotNull(await store.GetAsync("../escape")); // normalized consistently on both ends
    }

    [Fact]
    public async Task Legacy_history_is_migrated_to_default()
    {
        Directory.CreateDirectory(_dir);
        await File.WriteAllTextAsync(LegacyPath,
            """[{"Role":"user","Text":"old hi"},{"Role":"assistant","Text":"old reply"}]""");

        var store = NewStore();
        var list = await store.ListAsync();

        ConversationInfo def = Assert.Single(list);
        Assert.Equal("default", def.Id);
        Assert.Equal(2, def.TurnCount);
        Assert.Equal("old hi", (await store.LoadAsync("default"))[0].Text);
    }

    [Fact]
    public async Task Migration_does_not_resurrect_deleted_default()
    {
        Directory.CreateDirectory(_dir);
        await File.WriteAllTextAsync(LegacyPath, """[{"Role":"user","Text":"old"}]""");

        var store1 = NewStore();
        await store1.ListAsync();          // triggers migration
        await store1.DeleteAsync("default");

        var store2 = NewStore();           // fresh instance, same directory
        Assert.Null(await store2.GetAsync("default")); // not re-migrated
    }

    [Fact]
    public async Task Corrupt_conversation_file_is_skipped()
    {
        Directory.CreateDirectory(Path.Combine(_dir, "conversations"));
        await File.WriteAllTextAsync(Path.Combine(_dir, "conversations", "bad.json"), "{ not valid json ]");

        var store = NewStore();
        Assert.Empty(await store.ListAsync());          // unreadable file ignored, no throw
        Assert.Empty(await store.LoadAsync("bad"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_dir))
            Directory.Delete(_dir, recursive: true);
    }
}
