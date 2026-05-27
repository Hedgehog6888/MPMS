namespace MPMS.Services;

/// <summary>
/// Хранит поиск и фильтры страниц между навигациями (ViewModel остаётся Transient).
/// </summary>
public interface IPageUiStateStore
{
    string? GetString(string pageKey, string fieldKey);
    void SetString(string pageKey, string fieldKey, string? value);

    Guid? GetGuid(string pageKey, string fieldKey);
    void SetGuid(string pageKey, string fieldKey, Guid? value);

    DateOnly? GetDate(string pageKey, string fieldKey);
    void SetDate(string pageKey, string fieldKey, DateOnly? value);
}

/// <summary>Ключи страниц в <see cref="IPageUiStateStore"/>.</summary>
public static class PageUiKeys
{
    public const string Warehouse = "Warehouse";
    public const string Tasks = "Tasks";
    public const string Stages = "Stages";
    public const string Projects = "Projects";
    public const string ClosedProjects = "ClosedProjects";
    public const string Files = "Files";
    public const string Catalogs = "Catalogs";
    public const string Admin = "Admin";
    public const string Timeline = "Timeline";
}
