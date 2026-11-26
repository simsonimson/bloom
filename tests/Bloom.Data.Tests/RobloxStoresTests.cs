using Bloom.Core.Roblox;

namespace Bloom.Data.Tests;

public sealed class RobloxStoresTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bloom-tests", Guid.NewGuid().ToString("N"));
    private readonly BloomDatabase _db;

    public RobloxStoresTests()
    {
        Directory.CreateDirectory(_dir);
        _db = new BloomDatabase(Path.Combine(_dir, "bloom.db"));
        _db.Migrate();
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    [Fact]
    public void Favorites_add_is_idempotent_and_removable()
    {
        var store = new FavoritesStore(_db);
        var a = store.Add(1, "One", null);
        var again = store.Add(1, "One renamed", "https://tr.rbxcdn.com/x");
        store.Add(2, "Two", null);

        Assert.Equal(a.Id, again.Id);
        Assert.Equal("One renamed", again.Name);
        Assert.Equal(2, store.All().Count);
        Assert.True(store.Contains(1));

        store.Remove(a.Id);
        Assert.False(store.Contains(1));
        Assert.Single(store.All());
    }

    [Fact]
    public void Sessions_record_experience_and_end()
    {
        var store = new SessionStore(_db);
        var start = DateTimeOffset.UtcNow.AddMinutes(-30);
        var id = store.Start(start);
        store.SetExperience(id, 920587237, 383310974, "job", "Adopt Me!");
        store.End(id, start.AddMinutes(25));

        var recent = Assert.Single(store.Recent(10));
        Assert.Equal(920587237, recent.PlaceId);
        Assert.Equal("Adopt Me!", recent.ExperienceName);
        Assert.NotNull(recent.EndedAt);
        Assert.Equal(TimeSpan.FromMinutes(25), recent.EndedAt!.Value - recent.StartedAt);
    }

    [Fact]
    public void Experience_cache_round_trips()
    {
        var cache = new ExperienceCacheStore(_db);
        Assert.Null(cache.GetByPlace(5));
        var info = new ExperienceInfo(10, 5, "Game", "https://tr.rbxcdn.com/icon", DateTimeOffset.UtcNow);
        cache.Put(5, info);
        var loaded = cache.GetByPlace(5);
        Assert.NotNull(loaded);
        Assert.Equal("Game", loaded!.Name);
        Assert.Equal(10, loaded.UniverseId);
    }
}
