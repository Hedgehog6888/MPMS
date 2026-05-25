using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using MPMS.Data;
using MPMS.Models;

namespace MPMS.Views.Overlays;

public partial class WorkTypeCategoryFormOverlay : UserControl
{
    private LocalWorkTypeCategory? _existingCategory;
    private Action<LocalWorkTypeCategory>? _onSave;
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;

    public WorkTypeCategoryFormOverlay(IDbContextFactory<LocalDbContext> dbFactory)
    {
        InitializeComponent();
        _dbFactory = dbFactory;
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        NameInput.Focus();
    }

    public void SetupForCreate(Action<LocalWorkTypeCategory> onSave)
    {
        _existingCategory = null;
        _onSave = onSave;
        TitleText.Text = "Новая категория";
        SubtitleText.Text = "Введите название категории видов работ";
        SaveButton.Content = "Создать";

        NameInput.Text = string.Empty;
        ErrorPanel.Visibility = Visibility.Collapsed;
    }

    public void SetupForEdit(LocalWorkTypeCategory category, Action<LocalWorkTypeCategory> onSave)
    {
        _existingCategory = category;
        _onSave = onSave;
        TitleText.Text = "Редактирование категории";
        SubtitleText.Text = $"{category.Name}";
        SaveButton.Content = "Сохранить изменения";

        NameInput.Text = category.Name;
        ErrorPanel.Visibility = Visibility.Collapsed;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.Instance?.HideDrawer();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorPanel.Visibility = Visibility.Collapsed;

        var name = NameInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ShowError("Введите название категории");
            return;
        }

        // Check for duplicates
        await using var db = await _dbFactory.CreateDbContextAsync();
        var existingId = _existingCategory?.Id ?? Guid.Empty;
        var exists = await db.WorkTypeCategories
            .AnyAsync(c => c.Name == name && c.Id != existingId);
        if (exists)
        {
            ShowError("Категория с таким названием уже существует");
            return;
        }

        var category = _existingCategory ?? new LocalWorkTypeCategory { Id = Guid.NewGuid() };
        category.Name = name;

        if (_existingCategory == null)
        {
            category.IsActive = true;
            // Get max sort order and add 1
            var maxSort = await db.WorkTypeCategories
                .Select(c => (int?)c.SortOrder)
                .MaxAsync();
            category.SortOrder = (maxSort ?? 0) + 1;
            db.WorkTypeCategories.Add(category);
        }
        else
        {
            db.WorkTypeCategories.Update(category);
        }

        await db.SaveChangesAsync();
        _onSave?.Invoke(category);
        MainWindow.Instance?.HideDrawer();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
    }
}
