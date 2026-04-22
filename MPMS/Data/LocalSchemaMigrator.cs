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
        AddSubRoleColumn(conn);
        AddAdditionalSubRolesColumn(conn);
        AddBirthDateAndAddressColumns(conn);
        AddSessionPasswordProtectedColumn(conn);
        ApplyMaterialsInventorySchema(conn);
        ApplyWarehouseSchema(conn);
        ApplyServicesSchema(conn);
        
        TryAlterTable(conn, "ALTER TABLE \"Files\" ADD COLUMN \"FileData\" BLOB NULL;");
        TryAlterTable(conn, "ALTER TABLE \"Files\" ADD COLUMN \"OriginalCreatedAt\" TEXT NULL;");
    }

    private static void ApplyMaterialsInventorySchema(SqliteConnection conn)
    {
        Execute(conn, """
            CREATE TABLE IF NOT EXISTS "MaterialCategories" (
                "Id"   TEXT NOT NULL CONSTRAINT "PK_MaterialCategories" PRIMARY KEY,
                "Name" TEXT NOT NULL DEFAULT ''
            );
            """);
        Execute(conn, """
            CREATE TABLE IF NOT EXISTS "EquipmentCategories" (
                "Id"   TEXT NOT NULL CONSTRAINT "PK_EquipmentCategories" PRIMARY KEY,
                "Name" TEXT NOT NULL DEFAULT ''
            );
            """);
        TryAlterTable(conn, "ALTER TABLE \"Materials\" ADD COLUMN \"Quantity\" TEXT NOT NULL DEFAULT '0';");
        TryAlterTable(conn, "ALTER TABLE \"Materials\" ADD COLUMN \"CategoryId\" TEXT NULL;");
        TryAlterTable(conn, "ALTER TABLE \"Materials\" ADD COLUMN \"CategoryName\" TEXT NULL;");
        TryAlterTable(conn, "ALTER TABLE \"Materials\" ADD COLUMN \"ImagePath\" TEXT NULL;");
        TryAlterTable(conn, "ALTER TABLE \"Materials\" ADD COLUMN \"UpdatedAt\" TEXT NOT NULL DEFAULT '0001-01-01 00:00:00';");
        try
        {
            Execute(conn, """
                UPDATE "Materials" SET "UpdatedAt" = "CreatedAt"
                WHERE "UpdatedAt" IS NULL OR "UpdatedAt" = '' OR "UpdatedAt" = '0001-01-01 00:00:00'
                """);
        }
        catch (SqliteException) { /* ignore */ }

        SeedDefaultWarehouseCategories(conn);

        Execute(conn, """
            CREATE TABLE IF NOT EXISTS "MaterialStockMovements" (
                "Id"            TEXT NOT NULL CONSTRAINT "PK_MaterialStockMovements" PRIMARY KEY,
                "MaterialId"    TEXT NOT NULL,
                "OccurredAt"    TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
                "Delta"         TEXT NOT NULL DEFAULT '0',
                "QuantityAfter" TEXT NOT NULL DEFAULT '0',
                "OperationType" TEXT NOT NULL DEFAULT '',
                "Comment"       TEXT NULL,
                "UserId"        TEXT NULL,
                "ProjectId"     TEXT NULL,
                "TaskId"        TEXT NULL
            );
            """);
        Execute(conn, """
            CREATE TABLE IF NOT EXISTS "Equipments" (
                "Id"                   TEXT NOT NULL CONSTRAINT "PK_Equipments" PRIMARY KEY,
                "Name"                 TEXT NOT NULL DEFAULT '',
                "Description"          TEXT NULL,
                "CategoryId"           TEXT NULL,
                "CategoryName"         TEXT NULL,
                "ImagePath"            TEXT NULL,
                "Status"               TEXT NOT NULL DEFAULT 'Available',
                "Condition"            TEXT NOT NULL DEFAULT 'Good',
                "InventoryNumber"      TEXT NULL,
                "CreatedAt"            TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
                "UpdatedAt"            TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
                "CheckedOutProjectId"  TEXT NULL,
                "CheckedOutTaskId"     TEXT NULL,
                "IsSynced"             INTEGER NOT NULL DEFAULT 1,
                "LastModifiedLocally"  TEXT NOT NULL DEFAULT '0001-01-01 00:00:00'
            );
            """);
        Execute(conn, """
            CREATE TABLE IF NOT EXISTS "EquipmentHistoryEntries" (
                "Id"             TEXT NOT NULL CONSTRAINT "PK_EquipmentHistoryEntries" PRIMARY KEY,
                "EquipmentId"    TEXT NOT NULL,
                "OccurredAt"     TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
                "EventType"      TEXT NOT NULL DEFAULT '',
                "PreviousStatus" TEXT NULL,
                "NewStatus"      TEXT NULL,
                "ProjectId"      TEXT NULL,
                "TaskId"         TEXT NULL,
                "UserId"         TEXT NULL,
                "Comment"        TEXT NULL
            );
            """);
        Execute(conn, """
            CREATE TABLE IF NOT EXISTS "StageEquipments" (
                "Id"                  TEXT NOT NULL CONSTRAINT "PK_StageEquipments" PRIMARY KEY,
                "StageId"             TEXT NOT NULL,
                "EquipmentId"         TEXT NOT NULL,
                "EquipmentName"       TEXT NOT NULL DEFAULT '',
                "InventoryNumber"     TEXT NULL,
                "IsSynced"            INTEGER NOT NULL DEFAULT 0,
                "LastModifiedLocally" TEXT NOT NULL DEFAULT '0001-01-01 00:00:00'
            );
            """);
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

    private static void AddSessionPasswordProtectedColumn(SqliteConnection conn)
    {
        TryAlterTable(conn,
            "ALTER TABLE \"AuthSessions\" ADD COLUMN \"SessionPasswordProtected\" TEXT NULL;");
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

    private static void AddSubRoleColumn(SqliteConnection conn)
    {
        TryAlterTable(conn, "ALTER TABLE \"Users\" ADD COLUMN \"SubRole\" TEXT NULL;");
    }

    private static void AddAdditionalSubRolesColumn(SqliteConnection conn)
    {
        TryAlterTable(conn, "ALTER TABLE \"Users\" ADD COLUMN \"AdditionalSubRoles\" TEXT NULL;");
    }

    private static void AddBirthDateAndAddressColumns(SqliteConnection conn)
    {
        TryAlterTable(conn, "ALTER TABLE \"Users\" ADD COLUMN \"BirthDate\" TEXT NULL;");
        TryAlterTable(conn, "ALTER TABLE \"Users\" ADD COLUMN \"HomeAddress\" TEXT NULL;");
    }

    private static void ApplyWarehouseSchema(SqliteConnection conn)
    {
        TryAlterTable(conn, "ALTER TABLE \"Materials\" ADD COLUMN \"IsWrittenOff\" INTEGER NOT NULL DEFAULT 0;");
        TryAlterTable(conn, "ALTER TABLE \"Materials\" ADD COLUMN \"WrittenOffAt\" TEXT NULL;");
        TryAlterTable(conn, "ALTER TABLE \"Materials\" ADD COLUMN \"WrittenOffComment\" TEXT NULL;");
        TryAlterTable(conn, "ALTER TABLE \"Materials\" ADD COLUMN \"IsArchived\" INTEGER NOT NULL DEFAULT 0;");
        TryAlterTable(conn, "ALTER TABLE \"Equipments\" ADD COLUMN \"IsWrittenOff\" INTEGER NOT NULL DEFAULT 0;");
        TryAlterTable(conn, "ALTER TABLE \"Equipments\" ADD COLUMN \"WrittenOffAt\" TEXT NULL;");
        TryAlterTable(conn, "ALTER TABLE \"Equipments\" ADD COLUMN \"WrittenOffComment\" TEXT NULL;");
        TryAlterTable(conn, "ALTER TABLE \"Equipments\" ADD COLUMN \"IsArchived\" INTEGER NOT NULL DEFAULT 0;");
        TryAlterTable(conn, "ALTER TABLE \"MaterialStockMovements\" ADD COLUMN \"UserName\" TEXT NULL;");
        TryAlterTable(conn, "ALTER TABLE \"EquipmentHistoryEntries\" ADD COLUMN \"UserName\" TEXT NULL;");
        TryAlterTable(conn, "ALTER TABLE \"Materials\" ADD COLUMN \"Cost\" TEXT NULL;");
        TryAlterTable(conn, "ALTER TABLE \"Materials\" ADD COLUMN \"InventoryNumber\" TEXT NULL;");
        TryAlterTable(conn, "ALTER TABLE \"Equipments\" ADD COLUMN \"Condition\" TEXT NOT NULL DEFAULT 'Good';");
        try
        {
            Execute(conn, """
                UPDATE "Equipments"
                SET "Status" = CASE
                    WHEN "Status" = 'CheckedOut' THEN 'InUse'
                    WHEN "Status" = 'InMaintenance' THEN 'Available'
                    ELSE "Status"
                END
                """);
        }
        catch (SqliteException) { /* ignore */ }
    }

    private static void ApplyServicesSchema(SqliteConnection conn)
    {
        Execute(conn, """
            CREATE TABLE IF NOT EXISTS "ServiceCategories" (
                "Id"          TEXT NOT NULL CONSTRAINT "PK_ServiceCategories" PRIMARY KEY,
                "Name"        TEXT NOT NULL DEFAULT '',
                "Description" TEXT NULL,
                "SortOrder"   INTEGER NOT NULL DEFAULT 0,
                "IsActive"    INTEGER NOT NULL DEFAULT 1
            );
            """);

        Execute(conn, """
            CREATE TABLE IF NOT EXISTS "ServiceTemplates" (
                "Id"                 TEXT NOT NULL CONSTRAINT "PK_ServiceTemplates" PRIMARY KEY,
                "Name"               TEXT NOT NULL DEFAULT '',
                "Description"        TEXT NULL,
                "Unit"               TEXT NULL,
                "Article"            TEXT NULL,
                "BasePrice"          TEXT NOT NULL DEFAULT '0',
                "CategoryId"         TEXT NOT NULL,
                "CategoryName"       TEXT NOT NULL DEFAULT '',
                "IsActive"           INTEGER NOT NULL DEFAULT 1,
                "CreatedAt"          TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
                "UpdatedAt"          TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
                "IsSynced"           INTEGER NOT NULL DEFAULT 1,
                "LastModifiedLocally" TEXT NOT NULL DEFAULT '0001-01-01 00:00:00'
            );
            """);

        TryAlterTable(conn, "ALTER TABLE \"TaskStages\" ADD COLUMN \"ServiceTemplateId\" TEXT NULL;");
        TryAlterTable(conn, "ALTER TABLE \"TaskStages\" ADD COLUMN \"ServiceNameSnapshot\" TEXT NULL;");
        TryAlterTable(conn, "ALTER TABLE \"TaskStages\" ADD COLUMN \"ServiceDescriptionSnapshot\" TEXT NULL;");
        TryAlterTable(conn, "ALTER TABLE \"TaskStages\" ADD COLUMN \"WorkUnitSnapshot\" TEXT NULL;");
        TryAlterTable(conn, "ALTER TABLE \"TaskStages\" ADD COLUMN \"WorkQuantity\" TEXT NOT NULL DEFAULT '0';");
        TryAlterTable(conn, "ALTER TABLE \"TaskStages\" ADD COLUMN \"WorkPricePerUnit\" TEXT NOT NULL DEFAULT '0';");
        TryAlterTable(conn, "ALTER TABLE \"StageMaterials\" ADD COLUMN \"PricePerUnit\" TEXT NOT NULL DEFAULT '0';");
        Execute(conn, """
            CREATE TABLE IF NOT EXISTS "StageServices" (
                "Id"                TEXT NOT NULL CONSTRAINT "PK_StageServices" PRIMARY KEY,
                "StageId"           TEXT NOT NULL,
                "ServiceTemplateId" TEXT NOT NULL,
                "ServiceName"       TEXT NOT NULL DEFAULT '',
                "ServiceDescription" TEXT NULL,
                "Unit"              TEXT NULL,
                "Quantity"          TEXT NOT NULL DEFAULT '0',
                "PricePerUnit"      TEXT NOT NULL DEFAULT '0',
                "IsSynced"          INTEGER NOT NULL DEFAULT 1,
                "LastModifiedLocally" TEXT NOT NULL DEFAULT '0001-01-01 00:00:00'
            );
            """);

        SeedDefaultServiceCategories(conn);
        SeedDefaultServiceTemplates(conn);
    }

    private static void TryAlterTable(SqliteConnection conn, string sql)
    {
        try { Execute(conn, sql); }
        catch (SqliteException) { /* column already exists */ }
    }

    /// <summary>Примерные категории для пустой БД (по одному разу на таблицу).</summary>
    private static void SeedDefaultWarehouseCategories(SqliteConnection conn)
    {
        if (IsCategoryTableEmpty(conn, "MaterialCategories"))
        {
            foreach (var name in DefaultMaterialCategoryNames)
                InsertCategoryRow(conn, "MaterialCategories", Guid.NewGuid(), name);
        }

        if (IsCategoryTableEmpty(conn, "EquipmentCategories"))
        {
            foreach (var name in DefaultEquipmentCategoryNames)
                InsertCategoryRow(conn, "EquipmentCategories", Guid.NewGuid(), name);
        }
    }

    private static readonly string[] DefaultMaterialCategoryNames =
    [
        "Крепёж и метизы",
        "Электрика и кабель",
        "Сантехника",
        "Отделочные материалы",
        "ЛКМ и герметики",
        "Расходники для инструмента",
        "Пиломатериалы",
        "Цемент и сухие смеси",
        "Изоляция и утеплители",
        "Хозяйственные товары"
    ];

    private static readonly string[] DefaultEquipmentCategoryNames =
    [
        "Электроинструмент",
        "Бензоинструмент",
        "Измерительные приборы",
        "Опалубка и леса",
        "Компрессоры и генераторы",
        "Садовая техника",
        "Сварочное оборудование",
        "Подъёмное оборудование",
        "Малая механизация",
        "Прочее оборудование"
    ];

    private static bool IsCategoryTableEmpty(SqliteConnection conn, string table)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
        var scalar = cmd.ExecuteScalar();
        return scalar is long l ? l == 0 : Convert.ToInt64(scalar, System.Globalization.CultureInfo.InvariantCulture) == 0;
    }

    private static void InsertCategoryRow(SqliteConnection conn, string table, Guid id, string name)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"""
            INSERT INTO "{table}" ("Id", "Name") VALUES (@id, @name)
            """;
        cmd.Parameters.AddWithValue("@id", id.ToString());
        cmd.Parameters.AddWithValue("@name", name);
        cmd.ExecuteNonQuery();
    }

    private static void SeedDefaultServiceCategories(SqliteConnection conn)
    {
        if (!IsCategoryTableEmpty(conn, "ServiceCategories"))
            return;

        for (var i = 0; i < DefaultServiceCategoryNames.Length; i++)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                INSERT INTO "ServiceCategories" ("Id", "Name", "SortOrder", "IsActive")
                VALUES (@id, @name, @sort, 1)
                """;
            cmd.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
            cmd.Parameters.AddWithValue("@name", DefaultServiceCategoryNames[i]);
            cmd.Parameters.AddWithValue("@sort", i + 1);
            cmd.ExecuteNonQuery();
        }
    }

    private static void SeedDefaultServiceTemplates(SqliteConnection conn)
    {
        using var countCmd = conn.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM \"ServiceTemplates\"";
        var count = Convert.ToInt32(countCmd.ExecuteScalar());
        if (count > 0) return;

        var categories = new List<(string Id, string Name)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT \"Id\", \"Name\" FROM \"ServiceCategories\" ORDER BY \"SortOrder\", \"Name\"";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                categories.Add((reader.GetString(0), reader.GetString(1)));
        }
        if (categories.Count == 0) return;

        var now = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss");
        for (var i = 0; i < 100; i++)
        {
            var category = categories[i % categories.Count];
            using var insert = conn.CreateCommand();
            insert.CommandText = """
                INSERT INTO "ServiceTemplates"
                ("Id","Name","Description","Unit","Article","BasePrice","CategoryId","CategoryName","IsActive","CreatedAt","UpdatedAt","IsSynced","LastModifiedLocally")
                VALUES
                (@id,@name,@description,@unit,@article,@price,@categoryId,@categoryName,1,@createdAt,@updatedAt,1,@lastModified)
                """;
            insert.Parameters.AddWithValue("@id", Guid.NewGuid().ToString());
            insert.Parameters.AddWithValue("@name", $"{DefaultServiceTemplateNames[i % DefaultServiceTemplateNames.Length]} #{i + 1:000}");
            insert.Parameters.AddWithValue("@description", "Шаблонная услуга для формирования этапов и расчета сметы.");
            insert.Parameters.AddWithValue("@unit", i % 3 == 0 ? "м" : i % 3 == 1 ? "м2" : "шт");
            insert.Parameters.AddWithValue("@article", $"SRV-{i + 1:0000}");
            insert.Parameters.AddWithValue("@price", (120 + (i * 17 % 980)).ToString(System.Globalization.CultureInfo.InvariantCulture));
            insert.Parameters.AddWithValue("@categoryId", category.Id);
            insert.Parameters.AddWithValue("@categoryName", category.Name);
            insert.Parameters.AddWithValue("@createdAt", now);
            insert.Parameters.AddWithValue("@updatedAt", now);
            insert.Parameters.AddWithValue("@lastModified", now);
            insert.ExecuteNonQuery();
        }
    }

    private static readonly string[] DefaultServiceCategoryNames =
    [
        "Электромонтаж",
        "Слаботочные системы",
        "Сантехника",
        "Отопление",
        "Вентиляция",
        "Кондиционирование",
        "Отделочные работы",
        "Малярные работы",
        "Штукатурные работы",
        "Напольные покрытия",
        "Плиточные работы",
        "Гипсокартон",
        "Фасадные работы",
        "Кровельные работы",
        "Бетонные работы",
        "Кладочные работы",
        "Монтаж дверей",
        "Монтаж окон",
        "Демонтажные работы",
        "Пуско-наладка"
    ];

    private static readonly string[] DefaultServiceTemplateNames =
    [
        "Прокладка кабеля ВВГ-Пнг(А)-LS 3х1.5",
        "Прокладка кабеля ВВГ-Пнг(А)-LS 3х2.5",
        "Монтаж распределительной коробки",
        "Установка автоматического выключателя",
        "Монтаж розетки внутренней",
        "Монтаж выключателя одноклавишного",
        "Монтаж светильника потолочного",
        "Прокладка гофры ПВХ",
        "Штробление стен под кабель",
        "Заделка штробы",
        "Прокладка витой пары UTP cat.6",
        "Установка слаботочного щита",
        "Обжим и тестирование линии",
        "Монтаж трубы ППР",
        "Монтаж трубы металлопластик",
        "Установка смесителя",
        "Установка унитаза",
        "Монтаж радиатора отопления",
        "Опрессовка системы отопления",
        "Монтаж воздуховода",
        "Установка решетки вентиляции",
        "Монтаж внутреннего блока сплит-системы",
        "Монтаж наружного блока сплит-системы",
        "Шпаклевка стен под покраску",
        "Грунтование поверхности",
        "Покраска стен в 2 слоя",
        "Штукатурка стен по маякам",
        "Укладка ламината",
        "Укладка керамогранита",
        "Затирка плиточных швов",
        "Монтаж каркаса ГКЛ",
        "Обшивка стен ГКЛ",
        "Монтаж фасадного утеплителя",
        "Монтаж кровельной мембраны",
        "Армирование монолитной плиты",
        "Заливка бетонной стяжки",
        "Кладка перегородок из газоблока",
        "Монтаж межкомнатной двери",
        "Монтаж ПВХ окна",
        "Демонтаж перегородки"
    ];

    private static void Execute(SqliteConnection conn, string sql)
    {
        using var cmd = conn.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }
}
