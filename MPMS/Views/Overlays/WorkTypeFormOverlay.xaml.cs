using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using MPMS.Data;
using MPMS.Infrastructure;
using MPMS.Models;

namespace MPMS.Views.Overlays;

public partial class WorkTypeFormOverlay : UserControl
{
    private LocalWorkTypeTemplate? _existingWorkType;
    private Action<LocalWorkTypeTemplate>? _onSave;
    private Action<LocalWorkTypeCategory>? _onCategoryAdded;
    private string? _generatedArticle;
    private IDbContextFactory<LocalDbContext>? _dbFactory;

    public WorkTypeFormOverlay()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        NameInput.Focus();
    }

    public async void SetupForCreate(List<LocalWorkTypeCategory> categories, Action<LocalWorkTypeTemplate> onSave, IDbContextFactory<LocalDbContext> dbFactory, Action<LocalWorkTypeCategory>? onCategoryAdded = null)
    {
        _existingWorkType = null;
        _onSave = onSave;
        _onCategoryAdded = onCategoryAdded;
        _dbFactory = dbFactory;
        TitleText.Text = "Новый вид работ";
        SubtitleText.Text = "Заполните данные о виде работ";
        SaveButton.Content = "Сохранить";

        CategoryCombo.ItemsSource = categories.Where(c => c.IsActive).ToList();
        CategoryCombo.DisplayMemberPath = "Name";
        CategoryCombo.SelectedIndex = 0;

        NameInput.Text = string.Empty;
        PriceInput.Text = "0,00";
        DescriptionInput.Text = string.Empty;
        ErrorPanel.Visibility = Visibility.Collapsed;

        // Generate next article number
        await using var db = await dbFactory.CreateDbContextAsync();
        _generatedArticle = await ArticleNumbers.NextWorkTypeAsync(db);
        HeaderArticleLine.Text = $"Арт. № {_generatedArticle}";
        HeaderArticleLine.Visibility = Visibility.Visible;
    }

    public void SetupForEdit(LocalWorkTypeTemplate workType, List<LocalWorkTypeCategory> categories, Action<LocalWorkTypeTemplate> onSave, IDbContextFactory<LocalDbContext> dbFactory)
    {
        _existingWorkType = workType;
        _onSave = onSave;
        _dbFactory = dbFactory;
        TitleText.Text = "Редактирование вида работ";
        SubtitleText.Text = $"{workType.Name}";
        SaveButton.Content = "Сохранить изменения";

        CategoryCombo.ItemsSource = categories.ToList();
        CategoryCombo.DisplayMemberPath = "Name";
        CategoryCombo.SelectedItem = categories.FirstOrDefault(c => c.Name == workType.CategoryName);

        NameInput.Text = workType.Name;
        HeaderArticleLine.Text = $"Арт. № {workType.Article ?? "—"}";
        HeaderArticleLine.Visibility = Visibility.Visible;
        PriceInput.Text = workType.BasePrice.ToString("N2");
        DescriptionInput.Text = workType.Description ?? string.Empty;
        ErrorPanel.Visibility = Visibility.Collapsed;
        _generatedArticle = null;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.Instance?.HideDrawer();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorPanel.Visibility = Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(NameInput.Text))
        {
            ShowError("Введите название вида работ");
            return;
        }

        if (CategoryCombo.SelectedItem is not LocalWorkTypeCategory selectedCategory)
        {
            ShowError("Выберите категорию");
            return;
        }

        if (!decimal.TryParse(PriceInput.Text, out var price))
        {
            ShowError("Введите корректную цену");
            return;
        }

        var workType = _existingWorkType ?? new LocalWorkTypeTemplate { Id = Guid.NewGuid() };
        workType.Name = NameInput.Text.Trim();
        workType.CategoryId = selectedCategory.Id;
        workType.CategoryName = selectedCategory.Name;
        workType.Article = _existingWorkType?.Article ?? _generatedArticle;
        workType.Unit = null;
        workType.BasePrice = price;
        workType.Description = string.IsNullOrWhiteSpace(DescriptionInput.Text) ? null : DescriptionInput.Text.Trim();
        workType.UpdatedAt = DateTime.UtcNow;
        if (_existingWorkType == null)
        {
            workType.CreatedAt = DateTime.UtcNow;
            workType.IsActive = true;
        }

        _onSave?.Invoke(workType);
        MainWindow.Instance?.HideDrawer();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
    }

    private void AddCategory_Click(object sender, RoutedEventArgs e)
    {
        if (_dbFactory is null || MainWindow.Instance is null) return;

        var currentSelection = CategoryCombo.SelectedItem as LocalWorkTypeCategory;

        var overlay = new WorkTypeCategoryFormOverlay(_dbFactory);
        overlay.SetupForCreate(newCategory =>
        {
            // Notify the parent that a category was added
            _onCategoryAdded?.Invoke(newCategory);

            // Reload categories from database to get the saved category with proper Id and SortOrder
            _ = Task.Run(async () =>
            {
                await using var db = await _dbFactory.CreateDbContextAsync();
                var categories = await db.WorkTypeCategories
                    .AsNoTracking()
                    .Where(c => c.IsActive)
                    .OrderBy(c => c.SortOrder)
                    .ThenBy(c => c.Name)
                    .ToListAsync();

                // Update UI on the main thread
                Dispatcher.Invoke(() =>
                {
                    CategoryCombo.ItemsSource = categories;
                    CategoryCombo.SelectedItem = categories.FirstOrDefault(c => c.Id == newCategory.Id);
                });
            });
        });

        MainWindow.Instance.ShowStackedModal(overlay, 420);
    }
}
