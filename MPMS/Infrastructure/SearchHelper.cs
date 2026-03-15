namespace MPMS.Infrastructure;

/// <summary>
/// Единообразная логика поиска: регистронезависимость, trim, экранирование спецсимволов.
/// </summary>
public static class SearchHelper
{
    /// <summary>Возвращает нормализованный поисковый терм (trim) или null если пусто.</summary>
    public static string? Normalize(string? search) =>
        string.IsNullOrWhiteSpace(search) ? null : search.Trim();

    /// <summary>Экранирует % и _ для использования в EF.Functions.Like.</summary>
    public static string EscapeLikePattern(string s)
    {
        if (string.IsNullOrEmpty(s)) return s;
        return s.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
    }

    /// <summary>Создаёт паттерн для LIKE: %term% с экранированием.</summary>
    public static string ToLikePattern(string? term)
    {
        var n = Normalize(term);
        return n is null ? "" : "%" + EscapeLikePattern(n) + "%";
    }

    /// <summary>Проверка Contains без учёта регистра (для in-memory фильтрации).</summary>
    public static bool ContainsIgnoreCase(string? text, string term) =>
        !string.IsNullOrEmpty(term) && text is not null &&
        text.Contains(term, StringComparison.OrdinalIgnoreCase);
}
