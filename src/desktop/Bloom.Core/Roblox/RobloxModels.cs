namespace Bloom.Core.Roblox;

/// <summary>Resolved experience metadata (public Roblox APIs, cached locally).</summary>
public sealed record ExperienceInfo(long UniverseId, long RootPlaceId, string Name, string? IconUrl, DateTimeOffset FetchedAt);

/// <summary>A Roblox process appeared.</summary>
public sealed record RobloxProcessStarted(int ProcessId, string? ExecutablePath, DateTimeOffset StartedAt);

/// <summary>A Roblox process ended.</summary>
public sealed record RobloxProcessExited(int ProcessId, DateTimeOffset ExitedAt);

/// <summary>Experimental (log-based): the client joined a server.</summary>
public sealed record ExperienceJoined(long PlaceId, string? JobId, long? UniverseId, string? ServerAddress, DateTimeOffset JoinedAt);

/// <summary>Experimental (log-based): the client left or lost the server.</summary>
public sealed record ExperienceLeft(DateTimeOffset LeftAt);

/// <summary>Current Roblox activity as shown in the UI and used by presence features.</summary>
public sealed record ActivityState(
    bool RobloxRunning,
    DateTimeOffset? SessionStartedAt,
    long? PlaceId,
    string? JobId,
    ExperienceInfo? Experience)
{
    public static ActivityState Idle { get; } = new(false, null, null, null, null);
}

public sealed record ActivityStateChanged(ActivityState State);

/// <summary>Persisted experience metadata cache.</summary>
public interface IExperienceCache
{
    ExperienceInfo? GetByPlace(long placeId);
    void Put(long placeId, ExperienceInfo info);
}

public sealed record PlaySession(long Id, DateTimeOffset StartedAt, DateTimeOffset? EndedAt, long? PlaceId, long? UniverseId, string? JobId, string? ExperienceName);

/// <summary>Persisted play history.</summary>
public interface ISessionStore
{
    long Start(DateTimeOffset startedAt);
    void SetExperience(long sessionId, long placeId, long? universeId, string? jobId, string? name);
    void End(long sessionId, DateTimeOffset endedAt);
    IReadOnlyList<PlaySession> Recent(int count);
}

public sealed record Favorite(long Id, long PlaceId, string Name, string? IconUrl, int SortOrder);

public interface IFavoritesStore
{
    IReadOnlyList<Favorite> All();
    Favorite Add(long placeId, string name, string? iconUrl);
    void Remove(long id);
    bool Contains(long placeId);
}
