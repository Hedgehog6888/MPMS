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
        CreateActivityLogsTable(conn);
        CreateProjectMembersTable(conn);
        CreateTaskAssigneesTable(conn);
        CreateStageAssigneesTable(conn);
        CreateMessagesTable(conn);
        AddIsMarkedForDeletionColumns(conn);
        AddAvatarPathColumn(conn);
        AddActionTypeToActivityLogs(conn);
        AddUserIdToActivityLogs(conn);
        AddUserBlockingColumns(conn);
        AddAvatarDataColumn(conn);
        AddIsArchivedColumn(conn);
        AddTaskStagesDueDateColumn(conn);
        AddPasswordHashColumn(conn);
        SplitUserNameToFirstLast(conn);
        CreateDeletedUserIdsTable(conn);
        CreateAppFlagsTable(conn);
    }

    private static void CreateAppFlagsTable(SqliteConnection conn)
    {
        Execute(conn, """
            CREATE TABLE IF NOT EXISTS "AppFlags" (
                "Key"   TEXT NOT NULL CONSTRAINT "PK_AppFlags" PRIMARY KEY,
                "Value" TEXT NOT NULL DEFAULT ''
            );
            """);
    }

    private static void CreateDeletedUserIdsTable(SqliteConnection conn)
    {
        Execute(conn, """
            CREATE TABLE IF NOT EXISTS "DeletedUserIds" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_DeletedUserIds" PRIMARY KEY
            );
            """);
    }

    private static void AddActionTypeToActivityLogs(SqliteConnection conn)
    {
        TryAlterTable(conn, "ALTER TABLE \"ActivityLogs\" ADD COLUMN \"ActionType\" TEXT NULL;");
    }

    private static void AddUserIdToActivityLogs(SqliteConnection conn)
    {
        TryAlterTable(conn, "ALTER TABLE \"ActivityLogs\" ADD COLUMN \"UserId\" TEXT NULL;");
        TryAlterTable(conn, "ALTER TABLE \"ActivityLogs\" ADD COLUMN \"ActorRole\" TEXT NULL;");
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

    private static void CreateActivityLogsTable(SqliteConnection conn)
    {
        Execute(conn, """
            CREATE TABLE IF NOT EXISTS "ActivityLogs" (
                "Id"          TEXT    NOT NULL CONSTRAINT "PK_ActivityLogs" PRIMARY KEY,
                "UserName"    TEXT    NOT NULL DEFAULT '',
                "UserInitials" TEXT   NOT NULL DEFAULT '?',
                "UserColor"   TEXT    NOT NULL DEFAULT '#1B6EC2',
                "ActionText"  TEXT    NOT NULL DEFAULT '',
                "EntityType"  TEXT    NOT NULL DEFAULT '',
                "EntityId"    TEXT    NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000',
                "CreatedAt"   TEXT    NOT NULL DEFAULT '0001-01-01 00:00:00'
            );
            """);
    }

    private static void CreateProjectMembersTable(SqliteConnection conn)
    {
        Execute(conn, """
            CREATE TABLE IF NOT EXISTS "ProjectMembers" (
                "Id"        TEXT NOT NULL CONSTRAINT "PK_ProjectMembers" PRIMARY KEY,
                "ProjectId" TEXT NOT NULL,
                "UserId"    TEXT NOT NULL,
                "UserName"  TEXT NOT NULL DEFAULT '',
                "UserRole"  TEXT NOT NULL DEFAULT ''
            );
            """);
    }

    private static void CreateTaskAssigneesTable(SqliteConnection conn)
    {
        Execute(conn, """
            CREATE TABLE IF NOT EXISTS "TaskAssignees" (
                "Id"      TEXT NOT NULL CONSTRAINT "PK_TaskAssignees" PRIMARY KEY,
                "TaskId"  TEXT NOT NULL,
                "UserId"  TEXT NOT NULL,
                "UserName" TEXT NOT NULL DEFAULT ''
            );
            """);
    }

    private static void CreateStageAssigneesTable(SqliteConnection conn)
    {
        Execute(conn, """
            CREATE TABLE IF NOT EXISTS "StageAssignees" (
                "Id"      TEXT NOT NULL CONSTRAINT "PK_StageAssignees" PRIMARY KEY,
                "StageId" TEXT NOT NULL,
                "UserId"  TEXT NOT NULL,
                "UserName" TEXT NOT NULL DEFAULT ''
            );
            """);
    }

    private static void CreateMessagesTable(SqliteConnection conn)
    {
        Execute(conn, """
            CREATE TABLE IF NOT EXISTS "Messages" (
                "Id"          TEXT NOT NULL CONSTRAINT "PK_Messages" PRIMARY KEY,
                "TaskId"      TEXT,
                "ProjectId"   TEXT,
                "UserId"      TEXT NOT NULL,
                "UserName"    TEXT NOT NULL DEFAULT '',
                "UserInitials" TEXT NOT NULL DEFAULT '?',
                "UserColor"   TEXT NOT NULL DEFAULT '#1B6EC2',
                "UserRole"    TEXT NOT NULL DEFAULT '',
                "Text"        TEXT NOT NULL DEFAULT '',
                "CreatedAt"   TEXT NOT NULL DEFAULT '0001-01-01 00:00:00'
            );
            """);
    }

    private static void AddAvatarPathColumn(SqliteConnection conn)
    {
        TryAlterTable(conn, "ALTER TABLE \"Users\" ADD COLUMN \"AvatarPath\" TEXT NULL;");
    }

    private static void AddIsMarkedForDeletionColumns(SqliteConnection conn)
    {
        TryAlterTable(conn, "ALTER TABLE \"Projects\" ADD COLUMN \"IsMarkedForDeletion\" INTEGER NOT NULL DEFAULT 0;");
        TryAlterTable(conn, "ALTER TABLE \"Tasks\" ADD COLUMN \"IsMarkedForDeletion\" INTEGER NOT NULL DEFAULT 0;");
        TryAlterTable(conn, "ALTER TABLE \"TaskStages\" ADD COLUMN \"IsMarkedForDeletion\" INTEGER NOT NULL DEFAULT 0;");
    }

    private static void AddUserBlockingColumns(SqliteConnection conn)
    {
        TryAlterTable(conn, "ALTER TABLE \"Users\" ADD COLUMN \"IsBlocked\" INTEGER NOT NULL DEFAULT 0;");
        TryAlterTable(conn, "ALTER TABLE \"Users\" ADD COLUMN \"BlockedAt\" TEXT NULL;");
        TryAlterTable(conn, "ALTER TABLE \"Users\" ADD COLUMN \"BlockedReason\" TEXT NULL;");
    }

    private static void AddAvatarDataColumn(SqliteConnection conn)
    {
        TryAlterTable(conn, "ALTER TABLE \"Users\" ADD COLUMN \"AvatarData\" BLOB NULL;");
    }

    private static void AddIsArchivedColumn(SqliteConnection conn)
    {
        TryAlterTable(conn, "ALTER TABLE \"Projects\" ADD COLUMN \"IsArchived\" INTEGER NOT NULL DEFAULT 0;");
        TryAlterTable(conn, "ALTER TABLE \"Tasks\" ADD COLUMN \"IsArchived\" INTEGER NOT NULL DEFAULT 0;");
        TryAlterTable(conn, "ALTER TABLE \"TaskStages\" ADD COLUMN \"IsArchived\" INTEGER NOT NULL DEFAULT 0;");
    }

    private static void AddTaskStagesDueDateColumn(SqliteConnection conn)
    {
        TryAlterTable(conn, "ALTER TABLE \"TaskStages\" ADD COLUMN \"DueDate\" TEXT NULL;");
    }

    private static void AddPasswordHashColumn(SqliteConnection conn)
    {
        TryAlterTable(conn, "ALTER TABLE \"Users\" ADD COLUMN \"PasswordHash\" TEXT NULL;");
    }

    private static void SplitUserNameToFirstLast(SqliteConnection conn)
    {
        TryAlterTable(conn, "ALTER TABLE \"Users\" ADD COLUMN \"FirstName\" TEXT NULL;");
        TryAlterTable(conn, "ALTER TABLE \"Users\" ADD COLUMN \"LastName\" TEXT NULL;");
        TryAlterTable(conn, "ALTER TABLE \"Users\" ADD COLUMN \"Name\" TEXT NULL;");
        try
        {
            Execute(conn, """
                UPDATE "Users" SET "Name" = trim("FirstName" || ' ' || "LastName")
                WHERE ("Name" IS NULL OR "Name" = '') AND ("FirstName" IS NOT NULL OR "LastName" IS NOT NULL)
                """);
        }
        catch (SqliteException) { /* ignore */ }
        try
        {
            Execute(conn, """
                UPDATE "Users" SET
                    "FirstName" = CASE WHEN instr(COALESCE("Name",'') || ' ', ' ') > 1 THEN substr("Name", 1, instr("Name" || ' ', ' ') - 1) ELSE COALESCE("Name",'') END,
                    "LastName" = CASE WHEN instr(COALESCE("Name",'') || ' ', ' ') > 1 THEN trim(substr("Name", instr("Name" || ' ', ' '))) ELSE '' END
                WHERE "FirstName" IS NULL OR "FirstName" = ''
                """);
        }
        catch (SqliteException) { /* ignore */ }
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
