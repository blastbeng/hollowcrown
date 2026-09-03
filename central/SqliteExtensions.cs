using Microsoft.Data.Sqlite;

namespace Hollowcrown.Central;

/// <summary>Parameterized SQL helpers shared by the endpoint handlers.</summary>
public static class SqliteExtensions
{
    public static int Exec(this SqliteConnection conn, string sql, params (string Name, object? Value)[] parameters)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        Bind(cmd, parameters);
        return cmd.ExecuteNonQuery();
    }

    public static object? Scalar(this SqliteConnection conn, string sql, params (string Name, object? Value)[] parameters)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        Bind(cmd, parameters);
        return cmd.ExecuteScalar();
    }

    public static SqliteCommand Command(this SqliteConnection conn, string sql, params (string Name, object? Value)[] parameters)
    {
        var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        Bind(cmd, parameters);
        return cmd;
    }

    private static void Bind(SqliteCommand cmd, (string Name, object? Value)[] parameters)
    {
        foreach (var (name, value) in parameters)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
    }

    public static long LastInsertRowId(this SqliteConnection conn)
        => Convert.ToInt64(conn.Scalar("SELECT last_insert_rowid()"));
}
