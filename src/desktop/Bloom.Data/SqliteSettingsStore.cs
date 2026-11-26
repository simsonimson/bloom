using Bloom.Core.Settings;

namespace Bloom.Data;

public sealed class SqliteSettingsStore : ISettingsStore
{
    private readonly BloomDatabase _db;

    public SqliteSettingsStore(BloomDatabase db) => _db = db;

    public bool TryGetRaw(string key, out string json)
    {
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT value_json FROM settings WHERE key = $k;";
        cmd.Parameters.AddWithValue("$k", key);
        if (cmd.ExecuteScalar() is string value)
        {
            json = value;
            return true;
        }
        json = "";
        return false;
    }

    public IReadOnlyDictionary<string, string> GetAll()
    {
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT key, value_json FROM settings ORDER BY key;";
        using var reader = cmd.ExecuteReader();
        var result = new Dictionary<string, string>();
        while (reader.Read()) result[reader.GetString(0)] = reader.GetString(1);
        return result;
    }

    public void SetRaw(string key, string json)
    {
        using var connection = _db.Open();
        using var cmd = connection.CreateCommand();
        cmd.CommandText = """
            INSERT INTO settings (key, value_json, updated_at) VALUES ($k, $v, $t)
            ON CONFLICT(key) DO UPDATE SET value_json = excluded.value_json, updated_at = excluded.updated_at;
            """;
        cmd.Parameters.AddWithValue("$k", key);
        cmd.Parameters.AddWithValue("$v", json);
        cmd.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }
}
