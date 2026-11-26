using System.Globalization;
using Bloom.Core.Roblox;

namespace Bloom.Data;

public sealed class ExperienceCacheStore(BloomDatabase db) : IExperienceCache
{
    public ExperienceInfo? GetByPlace(long placeId)
    {
        using var connection = db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT universe_id, root_place_id, name, icon_url, fetched_at FROM experience_cache WHERE place_id = $p;";
        cmd.Parameters.AddWithValue("$p", placeId);
        using var r = cmd.ExecuteReader();
        if (!r.Read()) return null;
        return new ExperienceInfo(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), Time.Parse(r.GetString(4)));
    }

    public void Put(long placeId, ExperienceInfo info)
    {
        using var connection = db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO experience_cache (place_id, universe_id, root_place_id, name, icon_url, fetched_at)
            VALUES ($p, $u, $r, $n, $i, $f)
            ON CONFLICT(place_id) DO UPDATE SET universe_id = excluded.universe_id, root_place_id = excluded.root_place_id,
                name = excluded.name, icon_url = excluded.icon_url, fetched_at = excluded.fetched_at;
            """;
        cmd.Parameters.AddWithValue("$p", placeId);
        cmd.Parameters.AddWithValue("$u", info.UniverseId);
        cmd.Parameters.AddWithValue("$r", info.RootPlaceId);
        cmd.Parameters.AddWithValue("$n", info.Name);
        cmd.Parameters.AddWithValue("$i", (object?)info.IconUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$f", Time.Format(info.FetchedAt));
        cmd.ExecuteNonQuery();
    }
}

public sealed class SessionStore(BloomDatabase db) : ISessionStore
{
    public long Start(DateTimeOffset startedAt)
    {
        using var connection = db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "INSERT INTO sessions (started_at) VALUES ($s); SELECT last_insert_rowid();";
        cmd.Parameters.AddWithValue("$s", Time.Format(startedAt));
        return Convert.ToInt64(cmd.ExecuteScalar(), CultureInfo.InvariantCulture);
    }

    public void SetExperience(long sessionId, long placeId, long? universeId, string? jobId, string? name)
    {
        using var connection = db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "UPDATE sessions SET place_id = $p, universe_id = $u, job_id = $j, experience_name = $n WHERE id = $id;";
        cmd.Parameters.AddWithValue("$p", placeId);
        cmd.Parameters.AddWithValue("$u", (object?)universeId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$j", (object?)jobId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$n", (object?)name ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$id", sessionId);
        cmd.ExecuteNonQuery();
    }

    public void End(long sessionId, DateTimeOffset endedAt)
    {
        using var connection = db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            UPDATE sessions SET ended_at = $e WHERE id = $id;
            DELETE FROM sessions WHERE started_at < $cutoff;
            """;
        cmd.Parameters.AddWithValue("$e", Time.Format(endedAt));
        cmd.Parameters.AddWithValue("$id", sessionId);
        cmd.Parameters.AddWithValue("$cutoff", Time.Format(endedAt.AddDays(-180)));
        cmd.ExecuteNonQuery();
    }

    public IReadOnlyList<PlaySession> Recent(int count)
    {
        using var connection = db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            SELECT id, started_at, ended_at, place_id, universe_id, job_id, experience_name
            FROM sessions ORDER BY started_at DESC LIMIT $n;
            """;
        cmd.Parameters.AddWithValue("$n", count);
        using var r = cmd.ExecuteReader();
        var list = new List<PlaySession>();
        while (r.Read())
        {
            list.Add(new PlaySession(
                r.GetInt64(0),
                Time.Parse(r.GetString(1)),
                r.IsDBNull(2) ? null : Time.Parse(r.GetString(2)),
                r.IsDBNull(3) ? null : r.GetInt64(3),
                r.IsDBNull(4) ? null : r.GetInt64(4),
                r.IsDBNull(5) ? null : r.GetString(5),
                r.IsDBNull(6) ? null : r.GetString(6)));
        }
        return list;
    }
}

public sealed class FavoritesStore(BloomDatabase db) : IFavoritesStore
{
    public IReadOnlyList<Favorite> All()
    {
        using var connection = db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT id, place_id, name, icon_url, sort_order FROM favorites ORDER BY sort_order, id;";
        using var r = cmd.ExecuteReader();
        var list = new List<Favorite>();
        while (r.Read())
            list.Add(new Favorite(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), r.GetInt32(4)));
        return list;
    }

    public Favorite Add(long placeId, string name, string? iconUrl)
    {
        using var connection = db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO favorites (place_id, name, icon_url, sort_order, added_at)
            VALUES ($p, $n, $i, COALESCE((SELECT MAX(sort_order) + 1 FROM favorites), 0), $t)
            ON CONFLICT(place_id) DO UPDATE SET name = excluded.name, icon_url = excluded.icon_url;
            SELECT id, sort_order FROM favorites WHERE place_id = $p;
            """;
        cmd.Parameters.AddWithValue("$p", placeId);
        cmd.Parameters.AddWithValue("$n", name);
        cmd.Parameters.AddWithValue("$i", (object?)iconUrl ?? DBNull.Value);
        cmd.Parameters.AddWithValue("$t", Time.Format(DateTimeOffset.UtcNow));
        using var r = cmd.ExecuteReader();
        r.Read();
        return new Favorite(r.GetInt64(0), placeId, name, iconUrl, r.GetInt32(1));
    }

    public void Remove(long id)
    {
        using var connection = db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "DELETE FROM favorites WHERE id = $id;";
        cmd.Parameters.AddWithValue("$id", id);
        cmd.ExecuteNonQuery();
    }

    public bool Contains(long placeId)
    {
        using var connection = db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT 1 FROM favorites WHERE place_id = $p;";
        cmd.Parameters.AddWithValue("$p", placeId);
        return cmd.ExecuteScalar() is not null;
    }
}

internal static class Time
{
    public static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
    public static DateTimeOffset Parse(string value) => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
