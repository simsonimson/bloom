using Microsoft.Data.Sqlite;

namespace Bloom.Data;

/// <summary>
/// Local SQLite database (Blueprint 7.1). Opens one connection per operation;
/// SQLite pooling in Microsoft.Data.Sqlite keeps this cheap.
/// </summary>
public sealed class BloomDatabase
{
    private readonly string _connectionString;

    public BloomDatabase(string path)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = true,
        }.ToString();
    }

    public SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var pragma = connection.CreateCommand();
        pragma.CommandText = "PRAGMA journal_mode = WAL; PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 3000;";
        pragma.ExecuteNonQuery();
        return connection;
    }

    /// <summary>Applies all pending migrations in order. Safe to call on every start.</summary>
    public int Migrate()
    {
        using var connection = Open();
        using (var create = connection.CreateCommand())
        {
            create.CommandText = "CREATE TABLE IF NOT EXISTS schema_version (version INTEGER PRIMARY KEY, applied_at TEXT NOT NULL);";
            create.ExecuteNonQuery();
        }

        int current;
        using (var read = connection.CreateCommand())
        {
            read.CommandText = "SELECT COALESCE(MAX(version), 0) FROM schema_version;";
            current = Convert.ToInt32(read.ExecuteScalar());
        }

        foreach (var migration in Migrations.All.Where(m => m.Version > current).OrderBy(m => m.Version))
        {
            using var tx = connection.BeginTransaction();
            using (var cmd = connection.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = migration.Sql;
                cmd.ExecuteNonQuery();
            }
            using (var mark = connection.CreateCommand())
            {
                mark.Transaction = tx;
                mark.CommandText = "INSERT INTO schema_version (version, applied_at) VALUES ($v, $t);";
                mark.Parameters.AddWithValue("$v", migration.Version);
                mark.Parameters.AddWithValue("$t", DateTimeOffset.UtcNow.ToString("O"));
                mark.ExecuteNonQuery();
            }
            tx.Commit();
            current = migration.Version;
        }
        return current;
    }
}

public sealed record Migration(int Version, string Sql);

public static class Migrations
{
    public static readonly IReadOnlyList<Migration> All =
    [
        new(1, """
            CREATE TABLE settings (
                key TEXT PRIMARY KEY,
                value_json TEXT NOT NULL,
                updated_at TEXT NOT NULL
            );
            """),
    ];
}
