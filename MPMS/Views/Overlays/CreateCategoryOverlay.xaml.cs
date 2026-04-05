using System.Windows;
using System.Windows.Controls;
using MPMS.ViewModels;

namespace MPMS.Views.Overlays;

public partial class CreateCategoryOverlay : UserControl
{
    private readonly string _mode;
    private readonly WarehouseViewModel _vm;
    private readonly Action<string> _onCreated;

    public CreateCategoryOverlay(string mode, WarehouseViewModel vm, Action<string> onCreated)
    {
        InitializeComponent();
        _mode = mode;
        _vm = vm;
        _onCreated = onCreated;

        TitleLabel.Text = mode == "Equipment" ? "Новая категория оборудования" : "Новая категория материала";
        SubtitleLabel.Text = mode == "Equipment"
            ? "Введите название категории оборудования"
            : "Введите название категории материала";
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => MainWindow.Instance?.HideDrawer();

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorPanel.Visibility = Visibility.Collapsed;
        var name = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ErrorText.Text = "Введите название категории";
            ErrorPanel.Visibility = Visibility.Visible;
            return;
        }

        if (_mode == "Equipment")
            await _vm.SaveNewEquipmentCategoryAsync(name);
        else
            await _vm.SaveNewMaterialCategoryAsync(name);

        MainWindow.Instance?.HideDrawer();
        _onCreated(name);
    }
}
