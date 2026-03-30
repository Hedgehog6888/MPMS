using System.IO;

namespace MPMS.Data;

/// <summary>
/// Путь к локальной SQLite: %LocalAppData%\MPMS\mpms_local.db (не удаляется при Clean/Rebuild).
/// При первом запуске копирует базу из папки с exe, если там ещё лежит старый файл.
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
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MPMS");
        Directory.CreateDirectory(dir);
        var target = Path.Combine(dir, "mpms_local.db");

        try
        {
            var legacy = Path.Combine(AppContext.BaseDirectory, "mpms_local.db");
            if (!File.Exists(target) && File.Exists(legacy))
                File.Copy(legacy, target, overwrite: false);
        }
        catch
        {
            /* игнорируем — используем только целевой путь */
        }

        return target;
    }
}
