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
        // Login username (case-sensitive column name to avoid conflict with UserName/UserDisplayName)
        TryAlterTable(conn,
            "ALTER TABLE \"AuthSessions\" ADD COLUMN \"Username\" TEXT NOT NULL DEFAULT '';");
        TryAlterTable(conn,
            "ALTER TABLE \"AuthSessions\" ADD COLUMN \"LocalPasswordHash\" TEXT NOT NULL DEFAULT '';");
        TryAlterTable(conn,
            "ALTER TABLE \"AuthSessions\" ADD COLUMN \"ApiBaseUrl\" TEXT NOT NULL DEFAULT 'http://localhost:5147/';");
        // EF Core maps AuthSession.UserName → column "UserDisplayName" to avoid SQLite case-insensitive clash
        TryAlterTable(conn,
            "ALTER TABLE \"AuthSessions\" ADD COLUMN \"UserDisplayName\" TEXT NOT NULL DEFAULT '';");
        // Kept on logout so the same account can re-login offline (1 = active, 0 = logged out)
        TryAlterTable(conn,
            "ALTER TABLE \"AuthSessions\" ADD COLUMN \"IsActiveSession\" INTEGER NOT NULL DEFAULT 1;");
    }

    private static void TryAlterTable(SqliteConnection conn, string sql)
    {
        try { Execute(conn, sql); }
        catch (SqliteException) { /* column already exists */ }
    }

    private static void Execute(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
