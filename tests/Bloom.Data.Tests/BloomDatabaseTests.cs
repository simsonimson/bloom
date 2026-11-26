using Bloom.Core.Settings;

namespace Bloom.Data.Tests;

public sealed class BloomDatabaseTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bloom-tests", Guid.NewGuid().ToString("N"));

    public BloomDatabaseTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private BloomDatabase NewDb() => new(Path.Combine(_dir, "bloom.db"));

    [Fact]
    public void Migrate_is_idempotent()
    {
        var db = NewDb();
        var first = db.Migrate();
        var second = db.Migrate();
        Assert.Equal(Migrations.All.Max(m => m.Version), first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Settings_round_trip_through_sqlite()
    {
        var db = NewDb();
        db.Migrate();
        var settings = new AppSettings(new SqliteSettingsStore(db))
        {
            IntroMode = IntroMode.Short,
            StartWithWindows = true,
        };
        settings.IntroMode = IntroMode.Off; // upsert

        var reloaded = new AppSettings(new SqliteSettingsStore(NewDb()));
        Assert.Equal(IntroMode.Off, reloaded.IntroMode);
        Assert.True(reloaded.StartWithWindows);
    }

    [Fact]
    public void Missing_key_returns_false()
    {
        var db = NewDb();
        db.Migrate();
        Assert.False(new SqliteSettingsStore(db).TryGetRaw("nope", out _));
    }
}
