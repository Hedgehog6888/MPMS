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
        return $"Data Source={path};Default Timeout=5000";
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

/// <summary>
/// Управление путями к папке изображений MPMS.
/// Все редактируемые изображения хранятся в Documents/MPMS/images
/// </summary>
public static class MpmsImagesPaths
{
    public static string GetImagesDirectory()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "MPMS",
            "images");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string GetImageFilePath(Guid fileId, string fileName)
    {
        return Path.Combine(GetImagesDirectory(), $"{fileId}{Path.GetExtension(fileName)}");
    }

    /// <summary>
    /// Копирует файл в папку MPMS/images, если он там еще не существует
    /// </summary>
    public static string EnsureImageCopy(Guid fileId, string originalPath, string fileName)
    {
        var targetPath = GetImageFilePath(fileId, fileName);

        if (File.Exists(targetPath))
            return targetPath;

        if (File.Exists(originalPath))
        {
            File.Copy(originalPath, targetPath);
            return targetPath;
        }

        return originalPath;
    }
}

/// <summary>
/// Управление путями к папке документов MPMS.
/// Все редактируемые документы хранятся в Documents/MPMS/documents
/// </summary>
public static class MpmsDocumentPaths
{
    public static string GetDocumentsDirectory()
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "MPMS",
            "documents");
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string GetDocumentFilePath(Guid fileId, string fileName)
    {
        return Path.Combine(GetDocumentsDirectory(), $"{fileId}{Path.GetExtension(fileName)}");
    }

    /// <summary>
    /// Копирует файл в папку MPMS/documents, если он там еще не существует
    /// </summary>
    public static string EnsureDocumentCopy(Guid fileId, string originalPath, string fileName)
    {
        var targetPath = GetDocumentFilePath(fileId, fileName);

        if (File.Exists(targetPath))
            return targetPath;

        if (File.Exists(originalPath))
        {
            File.Copy(originalPath, targetPath);
            return targetPath;
        }

        return originalPath;
    }
}
