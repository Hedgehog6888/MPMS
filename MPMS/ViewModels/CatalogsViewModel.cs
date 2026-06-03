using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MPMS.Data;
using MPMS.Infrastructure;
using MPMS.Models;
using MPMS.Services;
using MPMS.Views;
using MPMS.Views.Overlays;
using Microsoft.EntityFrameworkCore;

namespace MPMS.ViewModels;

public partial class CatalogsViewModel : ViewModelBase, ILoadable
{
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;
    private readonly ISyncService _sync;
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

    [ObservableProperty] private ObservableCollection<string> _workTypeCategoryFilterOptions = new() { "Все категории" };

    public CatalogsViewModel(IDbContextFactory<LocalDbContext> dbFactory, ISyncService sync, IPageUiStateStore uiState)
    {
        _dbFactory = dbFactory;
        _sync = sync;
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
        var options = new ObservableCollection<string> { "Все категории" };
        foreach (var category in categories)
        {
            options.Add(category);
        }
        WorkTypeCategoryFilterOptions = options;
    }

    private async Task LoadWorkTypeCategoriesAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var items = await db.WorkTypeCategories
            .AsNoTracking()
            .OrderBy(c => c.SortOrder)
            .ThenBy(c => c.Name)
            .ToListAsync();

        // Подсчет количества видов работ для каждой категории.
        // Некоторые старые записи могли иметь пустой/NULL CategoryId, что приводит к дубликатам ключей при ToDictionaryAsync.
        // Сначала забираем в память и группируем сами, отфильтровав Guid.Empty.
        var categoryIds = await db.WorkTypeTemplates
            .AsNoTracking()
            .Select(w => w.CategoryId)
            .ToListAsync();

        var counts = categoryIds
            .Where(id => id != Guid.Empty)
            .GroupBy(id => id)
            .ToDictionary(g => g.Key, g => g.Count());

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
        }, _dbFactory, async newCategory =>
        {
            await LoadWorkTypeCategoriesAsync();
        });
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
        }, _dbFactory);
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
                var oldPrice = entity.BasePrice;

                entity.Name = updatedWorkType.Name;
                entity.CategoryId = updatedWorkType.CategoryId;
                entity.CategoryName = updatedWorkType.CategoryName;
                entity.Article = updatedWorkType.Article;
                entity.Unit = updatedWorkType.Unit;
                entity.BasePrice = updatedWorkType.BasePrice;
                entity.Description = updatedWorkType.Description;
                entity.UpdatedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();

                // Record price history if price changed
                if (entity.BasePrice != oldPrice)
                {
                    var history = new LocalWorkTypePriceHistory
                    {
                        Id = Guid.NewGuid(),
                        WorkTypeId = entity.Id,
                        OldPrice = oldPrice,
                        NewPrice = entity.BasePrice,
                        ChangedAt = DateTime.UtcNow
                    };
                    db.WorkTypePriceHistories.Add(history);
                    await db.SaveChangesAsync();
                }

                await PropagateWorkTypeTemplateChangeAsync(entity.Id, entity);
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
            await PropagateWorkTypeTemplateChangeAsync(entity.Id, null);
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

    private async Task PropagateWorkTypeTemplateChangeAsync(Guid templateId, LocalWorkTypeTemplate? updatedTemplate)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        var affected = await (from sw in db.StageWorkTypes
                              join st in db.TaskStages on sw.StageId equals st.Id
                              join t in db.Tasks on st.TaskId equals t.Id
                              join p in db.Projects on t.ProjectId equals p.Id
                              where sw.WorkTypeTemplateId == templateId
                                    && st.Status != StageStatus.Completed
                                    && !st.IsArchived
                                    && !p.IsClosed
                              select new { sw, st })
            .ToListAsync();

        if (affected.Count == 0) return;

        var stageIds = new HashSet<Guid>();

        foreach (var row in affected)
        {
            if (updatedTemplate is null)
            {
                db.StageWorkTypes.Remove(row.sw);
            }
            else
            {
                var hadPriceOverride = Math.Abs(row.sw.PricePerUnit - row.sw.BasePricePerUnit) > 0.005m;
                row.sw.WorkTypeName = updatedTemplate.Name;
                row.sw.WorkTypeDescription = updatedTemplate.Description;
                row.sw.Unit = updatedTemplate.Unit;
                row.sw.BasePricePerUnit = updatedTemplate.BasePrice;
                if (!hadPriceOverride)
                    row.sw.PricePerUnit = updatedTemplate.BasePrice;
                row.sw.IsSynced = false;
                row.sw.LastModifiedLocally = DateTime.UtcNow;
            }

            stageIds.Add(row.st.Id);
            row.st.IsSynced = false;
            row.st.UpdatedAt = DateTime.UtcNow;
            row.st.LastModifiedLocally = DateTime.UtcNow;
        }

        await db.SaveChangesAsync();

        foreach (var stageId in stageIds)
        {
            var stage = await db.TaskStages.FindAsync(stageId);
            if (stage is null) continue;

            var workTypes = await db.StageWorkTypes
                .Where(x => x.StageId == stageId)
                .ToListAsync();

            var items = workTypes
                .Select(w => new StageWorkTypeItemRequest(
                    w.WorkTypeTemplateId,
                    w.Quantity,
                    w.PricePerUnit,
                    w.WorkTypeName,
                    w.Unit,
                    w.BasePricePerUnit,
                    w.LineAdjustmentPercent))
                .ToList();

            var req = new UpdateStageRequest(
                stage.Name,
                stage.Description,
                stage.AssignedUserId,
                stage.Status,
                stage.DueDate,
                stage.IsMarkedForDeletion,
                stage.IsArchived,
                stage.WorkTypeTemplateId,
                stage.WorkQuantity,
                stage.WorkPricePerUnit,
                items);

            await _sync.QueueOperationAsync("Stage", stageId, SyncOperation.Update, req);
        }
    }
}
