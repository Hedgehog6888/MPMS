using System.IO;
using Microsoft.EntityFrameworkCore;
using MPMS.Data;
using MPMS.Models;

namespace MPMS.Services;

/// <summary>Фильтр файлов, доступных в общем списке (без закрытых проектов).</summary>
public static class AvailableFilesQuery
{
    public static async Task<IQueryable<LocalFile>> ApplyGlobalFilterAsync(
        IQueryable<LocalFile> query,
        LocalDbContext db,
        IAuthService auth)
    {
        var userRole = auth.UserRole;
        var userId = auth.UserId;

        if (userRole is "Administrator" or "Admin")
        {
            var closedProjectIds = await db.Projects.AsNoTracking()
                .Where(p => p.IsClosed)
                .Select(p => p.Id)
                .ToListAsync();

            return query.Where(f =>
                !f.StageId.HasValue &&
                (!f.ProjectId.HasValue || !closedProjectIds.Contains(f.ProjectId.Value)));
        }

        if (userRole is "Worker" or "Работник")
            return query.Where(_ => false);

        if (userId is null)
            return query.Where(_ => false);

        var userProjectIds = await db.ProjectMembers.AsNoTracking()
            .Where(pm => pm.UserId == userId)
            .Select(pm => pm.ProjectId)
            .ToListAsync();

        var closedIds = await db.Projects.AsNoTracking()
            .Where(p => p.IsClosed)
            .Select(p => p.Id)
            .ToListAsync();

        return query.Where(f =>
            !f.StageId.HasValue &&
            (!f.ProjectId.HasValue ||
             (userProjectIds.Contains(f.ProjectId.Value) && !closedIds.Contains(f.ProjectId.Value))));
    }

    private static readonly string[] ImageExtensions =
        [".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff", ".svg"];

    public static bool IsImageFileName(string fileName)
    {
        var ext = Path.GetExtension(fileName)?.ToLower();
        return ext != null && ImageExtensions.Contains(ext);
    }
}
