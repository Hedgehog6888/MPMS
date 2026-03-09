using Microsoft.Data.Sqlite;

namespace MPMS.Data;

/// <summary>
/// Applies incremental schema changes to the existing local SQLite database.
/// Safe to call on every startup — all operations are idempotent.
/// </summary>
public static class LocalSchemaMigrator
{
    public static void Apply(string connectionString)
    {
        using var conn = new SqliteConnection(connectionString);
        conn.Open();

        CreateRecentAccountsTable(conn);
        AddUsernameColumnToAuthSessions(conn);
    }

    private static void CreateRecentAccountsTable(SqliteConnection conn)
    {
        Execute(conn, """
            CREATE TABLE IF NOT EXISTS "RecentAccounts" (
                "Id"          INTEGER NOT NULL CONSTRAINT "PK_RecentAccounts" PRIMARY KEY AUTOINCREMENT,
                "Username"    TEXT    NOT NULL DEFAULT '',
                "DisplayName" TEXT    NOT NULL DEFAULT '',
                "Role"        TEXT    NOT NULL DEFAULT '',
                "AvatarColor" TEXT    NOT NULL DEFAULT '#1B6EC2',
                "Initials"    TEXT    NOT NULL DEFAULT '?',
                "LastLoginAt" TEXT    NOT NULL DEFAULT '0001-01-01 00:00:00'
            );
            """);
    }

    private static void AddUsernameColumnToAuthSessions(SqliteConnection conn)
    {
        try
        {
            Execute(conn,
                "ALTER TABLE \"AuthSessions\" ADD COLUMN \"Username\" TEXT NOT NULL DEFAULT '';");
        }
        catch (SqliteException)
        {
            // Column already exists — nothing to do
        }
    }

    private static void Execute(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
