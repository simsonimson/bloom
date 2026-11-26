using Bloom.Core.Voice;

namespace Bloom.Voice.Tests;

public sealed class VoiceHelpersTests
{
    [Fact]
    public void Button_position_survives_dpi_changes()
    {
        var stored = ButtonGeometry.FromClient(150, 60, 1920, 1080, scale: 1.5);
        Assert.Equal(new ButtonPosition(100, 40), stored);
        Assert.Equal((100, 40), ButtonGeometry.ToClient(stored!, 1280, 720, scale: 1.0));
        Assert.Equal((200, 80), ButtonGeometry.ToClient(stored!, 2560, 1440, scale: 2.0));
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(10, -1)]
    [InlineData(800, 10)]
    [InlineData(10, 600)]
    public void Points_outside_the_window_are_rejected(int x, int y)
    {
        Assert.Null(ButtonGeometry.FromClient(x, y, 800, 600, 1.0));
        Assert.Null(ButtonGeometry.ToClient(new ButtonPosition(x, y), 800, 600, 1.0));
    }

    [Fact]
    public void Journal_round_trips_and_deletes()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bloom-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new JournalStore(Path.Combine(dir, "audio-journal.json"));
            Assert.Null(store.Read());
            var journal = new AudioJournal(PttMethod.SystemWide, "{0.0.1.00000000}.{abc}", false, true, false, 1234, DateTimeOffset.UtcNow);
            store.Write(journal);
            Assert.Equal(journal, store.Read());
            store.Write(journal with { Active = false });
            Assert.False(store.Read()!.Active);
            store.Delete();
            Assert.Null(store.Read());
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch (IOException) { }
        }
    }

    [Fact]
    public void Broken_journal_reads_as_missing()
    {
        var path = Path.Combine(Path.GetTempPath(), $"bloom-journal-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ nope");
        try { Assert.Null(new JournalStore(path).Read()); }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Roblox_process_set()
    {
        var set = new RobloxProcesses();
        Assert.False(set.Any);
        set.Reset([1, 2]);
        set.Add(3);
        set.Remove(1);
        Assert.Equal(new[] { 2, 3 }, set.Snapshot().Order());
        Assert.True(set.Contains(3));
    }

    [Fact]
    public void Guard_names_are_per_process()
    {
        Assert.NotEqual(GuardNames.CleanEvent(1), GuardNames.CleanEvent(2));
        Assert.StartsWith(@"Local\", GuardNames.CleanEvent(1));
    }
}
