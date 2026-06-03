using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using MPMS.Data;
using MPMS.Models;

namespace MPMS.Views.Overlays;

public partial class WorkTypeDetailsOverlay : UserControl
{
    private LocalWorkTypeTemplate? _workType;
    private Action<LocalWorkTypeTemplate>? _onEdit;
    private IDbContextFactory<LocalDbContext>? _dbFactory;

    public WorkTypeDetailsOverlay()
    {
        InitializeComponent();
    }

    public async void ShowWorkType(LocalWorkTypeTemplate workType, Action<LocalWorkTypeTemplate> onEdit, IDbContextFactory<LocalDbContext>? dbFactory = null)
    {
        _workType = workType;
        _onEdit = onEdit;
        _dbFactory = dbFactory;

        TitleText.Text = workType.Name;
        CategoryText.Text = workType.CategoryName;
        ArticleText.Text = string.IsNullOrEmpty(workType.Article) ? "—" : workType.Article;
        PriceText.Text = $"{workType.BasePrice:N2} ₽";
        DescriptionText.Text = workType.Description ?? string.Empty;

        CreatedText.Text = $"Создан: {workType.CreatedAt:dd.MM.yyyy HH:mm}";
        UpdatedText.Text = $"Обновлён: {workType.UpdatedAt:dd.MM.yyyy HH:mm}";

        // Load price history
        if (_dbFactory != null)
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var history = await db.WorkTypePriceHistories
                .Where(h => h.WorkTypeId == workType.Id)
                .OrderByDescending(h => h.ChangedAt)
                .ToListAsync();

            PriceHistoryList.ItemsSource = history;
            NoHistoryText.Visibility = history.Any() ? Visibility.Collapsed : Visibility.Visible;
        }
        else
        {
            PriceHistoryList.ItemsSource = null;
            NoHistoryText.Visibility = Visibility.Visible;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.Instance?.HideDrawer();
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (_workType != null)
        {
            _onEdit?.Invoke(_workType);
        }
    }
}
