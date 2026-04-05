using System.Windows;
using System.Windows.Controls;
using MPMS.Models;
using MPMS.ViewModels;

namespace MPMS.Views.Overlays;

public partial class AddQuantityOverlay : UserControl
{
    private readonly LocalMaterial _material;
    private readonly WarehouseViewModel _vm;

    public AddQuantityOverlay(LocalMaterial material, WarehouseViewModel vm)
    {
        InitializeComponent();
        _material = material;
        _vm = vm;

        SubtitleText.Text = $"{material.Name} — текущий остаток: {material.Quantity:G} {material.Unit ?? string.Empty}".Trim();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => MainWindow.Instance?.HideDrawer();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorPanel.Visibility = Visibility.Collapsed;

        if (!decimal.TryParse(AmountBox.Text.Replace(',', '.'),
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var amount) || amount <= 0)
        {
            ErrorText.Text = "Введите корректное положительное количество";
            ErrorPanel.Visibility = Visibility.Visible;
            return;
        }

        var comment = string.IsNullOrWhiteSpace(CommentBox.Text) ? null : CommentBox.Text.Trim();

        MainWindow.Instance?.HideAllOverlays();
        _ = _vm.AddMaterialQuantityAsync(_material.Id, amount, comment);
    }
}
