using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MPMS.Data;
using MPMS.Infrastructure;
using MPMS.Models;
using MPMS.Services;
using MPMS.Views;
using MPMS.Views.Overlays;
using Microsoft.EntityFrameworkCore;
using System.Collections.ObjectModel;

namespace MPMS.ViewModels;

public partial class CatalogsViewModel : ViewModelBase, ILoadable
{
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;
    private readonly PageUiStateBinder _ui;

    // Search texts
    [ObservableProperty] private string _workTypeSearchText = string.Empty;
    [ObservableProperty] private string _categorySearchText = string.Empty;
    [ObservableProperty] private string _equipmentCategorySearchText = string.Empty;
    [ObservableProperty] private string _materialCategorySearchText = string.Empty;
    [ObservableProperty] private string _workTypeCategoryFilter = "Все категории";

    // Original collections (loaded from DB)
    [ObservableProperty] private ObservableCollection<LocalWorkTypeTemplate> _workTypes = new();
    [ObservableProperty] private ObservableCollection<LocalWorkTypeCategory> _workTypeCategories = new();
    [ObservableProperty] private ObservableCollection<LocalEquipmentCategory> _equipmentCategories = new();
    [ObservableProperty] private ObservableCollection<LocalMaterialCategory> _materialCategories = new();

    // Filtered collections (bound to UI)
    [ObservableProperty] private ObservableCollection<LocalWorkTypeTemplate> _filteredWorkTypes = new();
    [ObservableProperty] private ObservableCollection<LocalWorkTypeCategory> _filteredWorkTypeCategories = new();
    [ObservableProperty] private ObservableCollection<LocalEquipmentCategory> _filteredEquipmentCategories = new();
    [ObservableProperty] private ObservableCollection<LocalMaterialCategory> _filteredMaterialCategories = new();

    public List<string> WorkTypeCategoryFilterOptions => new() { "Все категории" };

    public CatalogsViewModel(IDbContextFactory<LocalDbContext> dbFactory, IPageUiStateStore uiState)
    {
        _dbFactory = dbFactory;
        _ui = new PageUiStateBinder(uiState, PageUiKeys.Catalogs);
    }

    private void RestorePageUi()
    {
        using var _ = _ui.BeginRestore();
        WorkTypeSearchText = _ui.GetString("WorkTypeSearchText");
        CategorySearchText = _ui.GetString("CategorySearchText");
        EquipmentCategorySearchText = _ui.GetString("EquipmentCategorySearchText");
        MaterialCategorySearchText = _ui.GetString("MaterialCategorySearchText");
        WorkTypeCategoryFilter = _ui.GetString("WorkTypeCategoryFilter", "Все категории");
    }

    public async Task LoadAsync()
    {
        RestorePageUi();
        await LoadWorkTypesAsync();
        await LoadWorkTypeCategoriesAsync();
        await LoadEquipmentCategoriesAsync();
        await LoadMaterialCategoriesAsync();
    }

    private async Task LoadWorkTypesAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var items = await db.WorkTypeTemplates
            .AsNoTracking()
            .OrderBy(w => w.Name)
            .ToListAsync();

        WorkTypes = new ObservableCollection<LocalWorkTypeTemplate>(items);
        ApplyWorkTypeFilter();

        // Update category filter options
        var categories = WorkTypes.Select(w => w.CategoryName).Distinct().OrderBy(c => c).ToList();
        var options = new List<string> { "Все категории" };
        options.AddRange(categories);
        OnPropertyChanged(nameof(WorkTypeCategoryFilterOptions));
    }

    private async Task LoadWorkTypeCategoriesAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var items = await db.WorkTypeCategories
            .AsNoTracking()
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync();

        // Подсчет количества видов работ для каждой категории
        var counts = await db.WorkTypeTemplates
            .AsNoTracking()
            .GroupBy(w => w.CategoryId)
            .Select(g => new { CategoryId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CategoryId, x => x.Count);

        foreach (var item in items)
        {
            counts.TryGetValue(item.Id, out var count);
            item.WorkTypeCount = count;
        }

        WorkTypeCategories = new ObservableCollection<LocalWorkTypeCategory>(items);
        ApplyWorkTypeCategoryFilter();
    }

    private async Task LoadEquipmentCategoriesAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var items = await db.EquipmentCategories
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .ToListAsync();

        EquipmentCategories = new ObservableCollection<LocalEquipmentCategory>(items);
        ApplyEquipmentCategoryFilter();
    }

    private async Task LoadMaterialCategoriesAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var items = await db.MaterialCategories
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .ToListAsync();

        MaterialCategories = new ObservableCollection<LocalMaterialCategory>(items);
        ApplyMaterialCategoryFilter();
    }

    partial void OnWorkTypeSearchTextChanged(string value)
    {
        _ui.SetString("WorkTypeSearchText", value);
        ApplyWorkTypeFilter();
    }

    partial void OnWorkTypeCategoryFilterChanged(string value)
    {
        _ui.SetString("WorkTypeCategoryFilter", value);
        ApplyWorkTypeFilter();
    }

    partial void OnCategorySearchTextChanged(string value)
    {
        _ui.SetString("CategorySearchText", value);
        ApplyWorkTypeCategoryFilter();
    }

    partial void OnEquipmentCategorySearchTextChanged(string value)
    {
        _ui.SetString("EquipmentCategorySearchText", value);
        ApplyEquipmentCategoryFilter();
    }

    partial void OnMaterialCategorySearchTextChanged(string value)
    {
        _ui.SetString("MaterialCategorySearchText", value);
        ApplyMaterialCategoryFilter();
    }

    private void ApplyWorkTypeFilter()
    {
        var filtered = WorkTypes.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(WorkTypeSearchText))
        {
            var search = WorkTypeSearchText.Trim().ToLowerInvariant();
            filtered = filtered.Where(w =>
                (w.Name != null && w.Name.ToLowerInvariant().Contains(search)) ||
                (w.Article != null && w.Article.ToLowerInvariant().Contains(search)));
        }

        if (WorkTypeCategoryFilter != "Все категории")
        {
            filtered = filtered.Where(w => w.CategoryName == WorkTypeCategoryFilter);
        }

        FilteredWorkTypes = new ObservableCollection<LocalWorkTypeTemplate>(filtered);
    }

    private void ApplyWorkTypeCategoryFilter()
    {
        var filtered = WorkTypeCategories.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(CategorySearchText))
        {
            var search = CategorySearchText.Trim().ToLowerInvariant();
            filtered = filtered.Where(c =>
                c.Name != null && c.Name.ToLowerInvariant().Contains(search));
        }

        FilteredWorkTypeCategories = new ObservableCollection<LocalWorkTypeCategory>(filtered);
    }

    private void ApplyEquipmentCategoryFilter()
    {
        var filtered = EquipmentCategories.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(EquipmentCategorySearchText))
        {
            var search = EquipmentCategorySearchText.Trim().ToLowerInvariant();
            filtered = filtered.Where(c =>
                c.Name != null && c.Name.ToLowerInvariant().Contains(search));
        }

        FilteredEquipmentCategories = new ObservableCollection<LocalEquipmentCategory>(filtered);
    }

    private void ApplyMaterialCategoryFilter()
    {
        var filtered = MaterialCategories.AsEnumerable();

        if (!string.IsNullOrWhiteSpace(MaterialCategorySearchText))
        {
            var search = MaterialCategorySearchText.Trim().ToLowerInvariant();
            filtered = filtered.Where(c =>
                c.Name != null && c.Name.ToLowerInvariant().Contains(search));
        }

        FilteredMaterialCategories = new ObservableCollection<LocalMaterialCategory>(filtered);
    }

    // Work Types commands
    [RelayCommand]
    private void AddWorkType()
    {
        var overlay = new WorkTypeFormOverlay();
        overlay.SetupForCreate(WorkTypeCategories.ToList(), async newWorkType =>
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            db.WorkTypeTemplates.Add(newWorkType);
            await db.SaveChangesAsync();
            await LoadWorkTypesAsync();
        }, _dbFactory);
        MainWindow.Instance?.ShowCenteredOverlay(overlay, MainWindow.CenteredFormOverlayWidth);
    }

    [RelayCommand]
    private void ViewWorkType(LocalWorkTypeTemplate? workType)
    {
        if (workType == null) return;

        var overlay = new WorkTypeDetailsOverlay();
        overlay.ShowWorkType(workType, wt =>
        {
            MainWindow.Instance?.HideDrawer();
            EditWorkType(wt);
        });
        MainWindow.Instance?.ShowDrawer(overlay, 480);
    }

    [RelayCommand]
    private void EditWorkType(LocalWorkTypeTemplate? workType)
    {
        if (workType == null) return;

        var overlay = new WorkTypeFormOverlay();
        overlay.SetupForEdit(workType, WorkTypeCategories.ToList(), async updatedWorkType =>
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var entity = await db.WorkTypeTemplates.FindAsync(updatedWorkType.Id);
            if (entity != null)
            {
                entity.Name = updatedWorkType.Name;
                entity.CategoryId = updatedWorkType.CategoryId;
                entity.CategoryName = updatedWorkType.CategoryName;
                entity.Article = updatedWorkType.Article;
                entity.Unit = updatedWorkType.Unit;
                entity.BasePrice = updatedWorkType.BasePrice;
                entity.Description = updatedWorkType.Description;
                entity.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
                await LoadWorkTypesAsync();
            }
        }, _dbFactory);
        MainWindow.Instance?.ShowCenteredOverlay(overlay, MainWindow.CenteredFormOverlayWidth);
    }

    [RelayCommand]
    private async Task DeleteWorkType(LocalWorkTypeTemplate? workType)
    {
        if (workType == null) return;

        var owner = System.Windows.Application.Current.MainWindow;
        var confirmed = ConfirmDeleteDialog.Show(owner, "вид работ", workType.Name);
        if (!confirmed) return;

        await using var db = await _dbFactory.CreateDbContextAsync();
        var entity = await db.WorkTypeTemplates.FindAsync(workType.Id);
        if (entity != null)
        {
            db.WorkTypeTemplates.Remove(entity);
            await db.SaveChangesAsync();
            await LoadWorkTypesAsync();
        }
    }

    // Work Type Categories commands
    [RelayCommand]
    private void AddWorkTypeCategory()
    {
        var overlay = new CreateCategoryOverlay("WorkType", this, _dbFactory, async name =>
        {
            await LoadWorkTypeCategoriesAsync();
        });
        MainWindow.Instance?.ShowStackedModal(overlay, 420);
    }

    [RelayCommand]
    private void ViewWorkTypeCategory(LocalWorkTypeCategory? category)
    {
        if (category == null) return;
        System.Diagnostics.Debug.WriteLine($"ViewWorkTypeCategory: {category.Name}");
        // TODO: Show view dialog
    }

    [RelayCommand]
    private void EditWorkTypeCategory(LocalWorkTypeCategory? category)
    {
        if (category == null) return;
        System.Diagnostics.Debug.WriteLine($"EditWorkTypeCategory: {category.Name}");
        // TODO: Show edit dialog
    }

    [RelayCommand]
    private async Task DeleteWorkTypeCategory(LocalWorkTypeCategory? category)
    {
        if (category == null) return;

        await using var db = await _dbFactory.CreateDbContextAsync();
        var relatedWorkTypes = await db.WorkTypeTemplates
            .AsNoTracking()
            .Where(w => w.CategoryId == category.Id)
            .CountAsync();

        string? blockMessage = null;
        if (relatedWorkTypes > 0)
        {
            blockMessage = $"Невозможно удалить категорию \"{category.Name}\", так как с ней связано {relatedWorkTypes} вид(ов) работ. Сначала удалите или переназначьте связанные виды работ.";
        }

        var owner = System.Windows.Application.Current.MainWindow;
        var confirmed = ConfirmDeleteDialog.Show(owner, "категорию работ", category.Name, null, blockMessage);
        if (!confirmed) return;

        var entity = await db.WorkTypeCategories.FindAsync(category.Id);
        if (entity != null)
        {
            db.WorkTypeCategories.Remove(entity);
            await db.SaveChangesAsync();
            await LoadWorkTypeCategoriesAsync();
        }
    }

    // Equipment Categories commands
    [RelayCommand]
    private void AddEquipmentCategory()
    {
        var overlay = new CreateCategoryOverlay("Equipment", this, _dbFactory, async name =>
        {
            await LoadEquipmentCategoriesAsync();
        });
        MainWindow.Instance?.ShowStackedModal(overlay, 420);
    }

    [RelayCommand]
    private void ViewEquipmentCategory(LocalEquipmentCategory? category)
    {
        if (category == null) return;
        System.Diagnostics.Debug.WriteLine($"ViewEquipmentCategory: {category.Name}");
        // TODO: Show view dialog
    }

    [RelayCommand]
    private void EditEquipmentCategory(LocalEquipmentCategory? category)
    {
        if (category == null) return;
        System.Diagnostics.Debug.WriteLine($"EditEquipmentCategory: {category.Name}");
        // TODO: Show edit dialog
    }

    [RelayCommand]
    private async Task DeleteEquipmentCategory(LocalEquipmentCategory? category)
    {
        if (category == null) return;

        await using var db = await _dbFactory.CreateDbContextAsync();
        var relatedEquipment = await db.Equipments
            .AsNoTracking()
            .Where(e => e.CategoryId == category.Id)
            .CountAsync();

        string? blockMessage = null;
        if (relatedEquipment > 0)
        {
            blockMessage = $"Невозможно удалить категорию \"{category.Name}\", так как с ней связано {relatedEquipment} единиц(ы) оборудования. Сначала удалите или переназначьте связанное оборудование.";
        }

        var owner = System.Windows.Application.Current.MainWindow;
        var confirmed = ConfirmDeleteDialog.Show(owner, "категорию оборудования", category.Name, null, blockMessage);
        if (!confirmed) return;

        var entity = await db.EquipmentCategories.FindAsync(category.Id);
        if (entity != null)
        {
            db.EquipmentCategories.Remove(entity);
            await db.SaveChangesAsync();
            await LoadEquipmentCategoriesAsync();
        }
    }

    // Material Categories commands
    [RelayCommand]
    private void AddMaterialCategory()
    {
        var overlay = new CreateCategoryOverlay("Material", this, _dbFactory, async name =>
        {
            await LoadMaterialCategoriesAsync();
        });
        MainWindow.Instance?.ShowStackedModal(overlay, 420);
    }

    [RelayCommand]
    private void ViewMaterialCategory(LocalMaterialCategory? category)
    {
        if (category == null) return;
        System.Diagnostics.Debug.WriteLine($"ViewMaterialCategory: {category.Name}");
        // TODO: Show view dialog
    }

    [RelayCommand]
    private void EditMaterialCategory(LocalMaterialCategory? category)
    {
        if (category == null) return;
        System.Diagnostics.Debug.WriteLine($"EditMaterialCategory: {category.Name}");
        // TODO: Show edit dialog
    }

    [RelayCommand]
    private async Task DeleteMaterialCategory(LocalMaterialCategory? category)
    {
        if (category == null) return;

        await using var db = await _dbFactory.CreateDbContextAsync();
        var relatedMaterials = await db.Materials
            .AsNoTracking()
            .Where(m => m.CategoryId == category.Id)
            .CountAsync();

        string? blockMessage = null;
        if (relatedMaterials > 0)
        {
            blockMessage = $"Невозможно удалить категорию \"{category.Name}\", так как с ней связано {relatedMaterials} материал(ов). Сначала удалите или переназначьте связанные материалы.";
        }

        var owner = System.Windows.Application.Current.MainWindow;
        var confirmed = ConfirmDeleteDialog.Show(owner, "категорию материалов", category.Name, null, blockMessage);
        if (!confirmed) return;

        var entity = await db.MaterialCategories.FindAsync(category.Id);
        if (entity != null)
        {
            db.MaterialCategories.Remove(entity);
            await db.SaveChangesAsync();
            await LoadMaterialCategoriesAsync();
        }
    }
}
