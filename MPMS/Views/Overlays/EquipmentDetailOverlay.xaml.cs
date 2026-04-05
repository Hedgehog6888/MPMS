using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using MPMS.Models;
using MPMS.ViewModels;

namespace MPMS.Views.Overlays;

public partial class EquipmentDetailOverlay : UserControl
{
    private LocalEquipment _equipment;
    private readonly WarehouseViewModel _vm;

    public EquipmentDetailOverlay(LocalEquipment equipment, WarehouseViewModel vm)
    {
        InitializeComponent();
        _equipment = equipment;
        _vm = vm;

        Loaded += async (_, _) => await RefreshAsync();
    }

    private async Task ReloadFromVmAsync()
    {
        await _vm.LoadAsync();
        var eq = _vm.Equipments.FirstOrDefault(x => x.Id == _equipment.Id);
        if (eq is null)
        {
            MainWindow.Instance?.HideDrawer();
            return;
        }

        _equipment = eq;
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        NameText.Text = _equipment.Name;
        CategoryText.Text = _equipment.CategoryName ?? string.Empty;
        StatusText.Text = _equipment.StatusDisplay;
        InvNumberText.Text = _equipment.InventoryNumber ?? "—";
        CreatedAtText.Text = _equipment.CreatedAt.ToLocalTime().ToString("dd.MM.yyyy");

        if (!string.IsNullOrWhiteSpace(_equipment.Description))
        {
            DescriptionPanel.Visibility = Visibility.Visible;
            DescriptionText.Text = _equipment.Description;
        }
        else
        {
            DescriptionPanel.Visibility = Visibility.Collapsed;
        }

        LoadPhoto();

        if (_equipment.IsWrittenOff)
        {
            WrittenOffBadge.Visibility = Visibility.Visible;
            WrittenOffBanner.Visibility = Visibility.Visible;
            WriteOffInfoPanel.Visibility = !string.IsNullOrWhiteSpace(_equipment.WrittenOffComment)
                ? Visibility.Visible : Visibility.Collapsed;
            WriteOffCommentText.Text = _equipment.WrittenOffComment ?? string.Empty;
            WriteOffDateText.Text = _equipment.WrittenOffAt.HasValue
                ? $"Дата списания: {_equipment.WrittenOffAt.Value.ToLocalTime():dd.MM.yyyy HH:mm}"
                : string.Empty;
            ActionPanel.Visibility = Visibility.Collapsed;
        }
        else
        {
            WrittenOffBadge.Visibility = Visibility.Collapsed;
            WrittenOffBanner.Visibility = Visibility.Collapsed;
            WriteOffInfoPanel.Visibility = Visibility.Collapsed;
            ActionPanel.Visibility = _vm.CanManage ? Visibility.Visible : Visibility.Collapsed;
        }

        if (_vm.CanViewHistory)
        {
            HistorySection.Visibility = Visibility.Visible;
            HistoryLoading.Visibility = Visibility.Visible;
            HistoryList.Visibility = Visibility.Collapsed;
            HistoryEmpty.Visibility = Visibility.Collapsed;

            var history = await _vm.GetEquipmentHistoryAsync(_equipment.Id);

            HistoryLoading.Visibility = Visibility.Collapsed;
            if (history.Count == 0)
                HistoryEmpty.Visibility = Visibility.Visible;
            else
            {
                HistoryList.Visibility = Visibility.Visible;
                HistoryList.ItemsSource = history;
            }
        }
        else
        {
            HistorySection.Visibility = Visibility.Collapsed;
        }
    }

    private void LoadPhoto()
    {
        if (string.IsNullOrWhiteSpace(_equipment.ImagePath) || !File.Exists(_equipment.ImagePath))
        {
            PhotoPlaceholder.Visibility = Visibility.Visible;
            EquipmentPhoto.Visibility = Visibility.Collapsed;
            return;
        }
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(_equipment.ImagePath, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            EquipmentPhoto.Source = bmp;
            EquipmentPhoto.Visibility = Visibility.Visible;
            PhotoPlaceholder.Visibility = Visibility.Collapsed;
        }
        catch
        {
            PhotoPlaceholder.Visibility = Visibility.Visible;
            EquipmentPhoto.Visibility = Visibility.Collapsed;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
        => MainWindow.Instance?.HideDrawer();

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (MainWindow.Instance is not { } mw) return;
        var overlay = new CreateWarehouseItemOverlay(
            "Equipment", _vm,
            _vm.MaterialCategories.ToList(),
            _vm.EquipmentCategories.ToList(),
            editEquipment: _equipment,
            afterStackedCloseSuccess: ReloadFromVmAsync);
        mw.ShowStackedModalOverDrawer(overlay, 560);
    }

    private void WriteOff_Click(object sender, RoutedEventArgs e)
    {
        if (MainWindow.Instance is not { } mw) return;
        var overlay = new WriteOffOverlay("оборудование", _equipment.Name,
            comment => _vm.WriteOffEquipmentAsync(_equipment.Id, comment));
        mw.ShowStackedModalOverDrawer(overlay, 520);
    }
}
