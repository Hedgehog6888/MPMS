namespace MPMS.Services;

public sealed class PageUiStateStore : IPageUiStateStore
{
    private readonly Dictionary<string, Dictionary<string, string>> _strings = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, Guid?>> _guids = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<string, DateOnly?>> _dates = new(StringComparer.Ordinal);

    public string? GetString(string pageKey, string fieldKey)
    {
        if (!_strings.TryGetValue(pageKey, out var page)) return null;
        return page.TryGetValue(fieldKey, out var v) ? v : null;
    }

    public void SetString(string pageKey, string fieldKey, string? value)
    {
        if (!_strings.TryGetValue(pageKey, out var page))
            _strings[pageKey] = page = new Dictionary<string, string>(StringComparer.Ordinal);

        if (string.IsNullOrEmpty(value))
            page.Remove(fieldKey);
        else
            page[fieldKey] = value;
    }

    public Guid? GetGuid(string pageKey, string fieldKey)
    {
        if (!_guids.TryGetValue(pageKey, out var page)) return null;
        return page.TryGetValue(fieldKey, out var v) ? v : null;
    }

    public void SetGuid(string pageKey, string fieldKey, Guid? value)
    {
        if (!_guids.TryGetValue(pageKey, out var page))
            _guids[pageKey] = page = new Dictionary<string, Guid?>(StringComparer.Ordinal);

        if (value is null)
            page.Remove(fieldKey);
        else
            page[fieldKey] = value;
    }

    public DateOnly? GetDate(string pageKey, string fieldKey)
    {
        if (!_dates.TryGetValue(pageKey, out var page)) return null;
        return page.TryGetValue(fieldKey, out var v) ? v : null;
    }

    public void SetDate(string pageKey, string fieldKey, DateOnly? value)
    {
        if (!_dates.TryGetValue(pageKey, out var page))
            _dates[pageKey] = page = new Dictionary<string, DateOnly?>(StringComparer.Ordinal);

        if (value is null)
            page.Remove(fieldKey);
        else
            page[fieldKey] = value;
    }
}
