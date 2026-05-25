using Microsoft.EntityFrameworkCore;
using MPMS.Data;

namespace MPMS.Infrastructure;

/// <summary>Сквозная нумерация артикулов для видов работ (SRV-).</summary>
public static class ArticleNumbers
{
    public const string WorkTypePrefix = "SRV-";

    /// <summary>Следующий свободный артикул. Если <paramref name="preferred"/> задан и не занят — возвращает его.</summary>
    public static async Task<string> NextWorkTypeAsync(LocalDbContext db, string? preferred = null)
    {
        if (!string.IsNullOrWhiteSpace(preferred))
        {
            var p = preferred.Trim();
            var taken = await db.WorkTypeTemplates.AnyAsync(w => w.Article == p);
            if (!taken) return p;
        }

        var max = await MaxWorkTypeSequenceAsync(db);
        return $"{WorkTypePrefix}{(max + 1):D6}";
    }

    private static async Task<int> MaxWorkTypeSequenceAsync(LocalDbContext db)
    {
        var articles = await db.WorkTypeTemplates
            .Where(w => w.Article != null && w.Article.StartsWith(WorkTypePrefix))
            .Select(w => w.Article!)
            .ToListAsync();
        return MaxSuffixAfterPrefix(articles, WorkTypePrefix);
    }

    private static int MaxSuffixAfterPrefix(IEnumerable<string> values, string prefix)
    {
        var pl = prefix.Length;
        var max = 0;
        foreach (var s in values)
        {
            if (s.Length <= pl) continue;
            if (!int.TryParse(s.AsSpan(pl), System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var n))
                continue;
            if (n > max) max = n;
        }

        return max;
    }
}
