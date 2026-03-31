using System.IO;

namespace MPMS.Data;

/// <summary>
/// Локальная SQLite:
/// 1) portable-режим: если рядом с exe есть mpms_local.db, используем его;
/// 2) иначе используем %LocalAppData%\MPMS\mpms_local.db.
/// Это позволяет переносить состояние простым копированием папки приложения.
/// </summary>
public static class LocalDbPaths
{
    public static string GetConnectionString()
    {
        var path = GetDatabaseFilePath();
        return $"Data Source={path}";
    }

    public static string GetDatabaseFilePath()
    {
        var portable = Path.Combine(AppContext.BaseDirectory, "mpms_local.db");
        if (File.Exists(portable))
            return portable;

        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MPMS");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, "mpms_local.db");
    }
}
