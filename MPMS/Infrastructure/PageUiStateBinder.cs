using MPMS.Services;

namespace MPMS.Infrastructure;

/// <summary>Вспомогатель для сохранения/восстановления фильтров страницы.</summary>
public sealed class PageUiStateBinder
{
    private readonly IPageUiStateStore _store;
    private readonly string _pageKey;

    public bool IsRestoring { get; private set; }

    public PageUiStateBinder(IPageUiStateStore store, string pageKey)
    {
        _store = store;
        _pageKey = pageKey;
    }

    public static string TabField(string tab, string field) => $"{tab}:{field}";

    public IDisposable BeginRestore()
    {
        IsRestoring = true;
        return new RestoreScope(() => IsRestoring = false);
    }

    public string GetString(string key, string defaultValue = "")
        => _store.GetString(_pageKey, key) ?? defaultValue;

    public Guid? GetGuid(string key) => _store.GetGuid(_pageKey, key);

    public DateOnly? GetDate(string key) => _store.GetDate(_pageKey, key);

    public void SetString(string key, string? value)
    {
        if (!IsRestoring) _store.SetString(_pageKey, key, value);
    }

    public void SetGuid(string key, Guid? value)
    {
        if (!IsRestoring) _store.SetGuid(_pageKey, key, value);
    }

    public void SetDate(string key, DateOnly? value)
    {
        if (!IsRestoring) _store.SetDate(_pageKey, key, value);
    }

    private sealed class RestoreScope(Action onEnd) : IDisposable
    {
        public void Dispose() => onEnd();
    }
}
