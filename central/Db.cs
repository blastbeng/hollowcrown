using Microsoft.Data.Sqlite;

namespace Hollowcrown.Central;

/// <summary>SQLite storage. Schema is created/migrated in code at startup (vision Section 4).</summary>
public static class Db
{
    public static string Path { get; private set; } = "hollowcrown.db";

    public static void Init(string? path = null)
    {
        if (!string.IsNullOrWhiteSpace(path)) Path = path;
        using var conn = Open();
        conn.Exec("""
            CREATE TABLE IF NOT EXISTS users(
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                username TEXT NOT NULL COLLATE NOCASE UNIQUE,
                pass_salt BLOB NOT NULL,
                pass_hash BLOB NOT NULL,
                created_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS tokens(
                token TEXT PRIMARY KEY,
                user_id INTEGER NOT NULL REFERENCES users(id),
                expires_at TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS characters(
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_id INTEGER NOT NULL REFERENCES users(id),
                name TEXT NOT NULL,
                class_id TEXT NOT NULL,
                level INTEGER NOT NULL DEFAULT 1,
                xp INTEGER NOT NULL DEFAULT 0,
                mmr INTEGER NOT NULL DEFAULT 1000,
                gear_json TEXT NOT NULL DEFAULT '[]',
                created_at TEXT NOT NULL,
                UNIQUE(user_id, name)
            );
            CREATE TABLE IF NOT EXISTS servers(
                server_id TEXT PRIMARY KEY,
                name TEXT NOT NULL,
                mode TEXT NOT NULL,
                host TEXT NOT NULL,
                port INTEGER NOT NULL,
                players INTEGER NOT NULL DEFAULT 0,
                max_players INTEGER NOT NULL DEFAULT 8,
                has_password INTEGER NOT NULL DEFAULT 0,
                last_seen TEXT NOT NULL
            );
            """);
    }

    public static SqliteConnection Open()
    {
        var conn = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = Path,
            Mode = SqliteOpenMode.ReadWriteCreate,
        }.ToString());
        conn.Open();
        return conn;
    }

    public static string Now() => DateTime.UtcNow.ToString("o");
}
