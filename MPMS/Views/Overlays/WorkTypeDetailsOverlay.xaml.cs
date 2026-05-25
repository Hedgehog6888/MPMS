using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MPMS.Models;

namespace MPMS.Views.Overlays;

public partial class WorkTypeDetailsOverlay : UserControl
{
    private LocalWorkTypeTemplate? _workType;
    private Action<LocalWorkTypeTemplate>? _onEdit;

    public WorkTypeDetailsOverlay()
    {
        InitializeComponent();
    }

    public void ShowWorkType(LocalWorkTypeTemplate workType, Action<LocalWorkTypeTemplate> onEdit)
    {
        _workType = workType;
        _onEdit = onEdit;

        TitleText.Text = workType.Name;
        CategoryText.Text = workType.CategoryName;
        ArticleText.Text = string.IsNullOrEmpty(workType.Article) ? "—" : workType.Article;
        PriceText.Text = $"{workType.BasePrice:N2} ₽";
        DescriptionText.Text = workType.Description ?? string.Empty;

        CreatedText.Text = $"Создан: {workType.CreatedAt:dd.MM.yyyy HH:mm}";
        UpdatedText.Text = $"Обновлён: {workType.UpdatedAt:dd.MM.yyyy HH:mm}";
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
