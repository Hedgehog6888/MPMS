using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using MPMS.Models;
using MPMS.ViewModels;

namespace MPMS.Views.Overlays;

public partial class MaterialDetailOverlay : UserControl
{
    private LocalMaterial _material;
    private readonly WarehouseViewModel _vm;

    public MaterialDetailOverlay(LocalMaterial material, WarehouseViewModel vm)
    {
        InitializeComponent();
        _material = material;
        _vm = vm;

        Loaded += async (_, _) => await RefreshAsync();
    }

    private async Task ReloadFromVmAsync()
    {
        await _vm.LoadAsync();
        var m = _vm.Materials.FirstOrDefault(x => x.Id == _material.Id);
        if (m is null)
        {
            MainWindow.Instance?.HideDrawer();
            return;
        }

        _material = m;
        await RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        NameText.Text = _material.Name;
        CategoryText.Text = _material.CategoryName ?? string.Empty;

        var unitLabel = string.IsNullOrWhiteSpace(_material.Unit) ? string.Empty : $" {_material.Unit}";
        QuantityText.Text = $"{_material.Quantity:G}{unitLabel}";
        UnitText.Text = _material.Unit ?? "—";
        CostText.Text = _material.Cost.HasValue
            ? _material.Cost.Value.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
            : "—";
        InventoryNumberText.Text = string.IsNullOrWhiteSpace(_material.InventoryNumber)
            ? "—"
            : _material.InventoryNumber;
        CreatedAtText.Text = _material.CreatedAt.ToLocalTime().ToString("dd.MM.yyyy");

        if (!string.IsNullOrWhiteSpace(_material.Description))
        {
            DescriptionPanel.Visibility = Visibility.Visible;
            DescriptionText.Text = _material.Description;
        }
        else
        {
            DescriptionPanel.Visibility = Visibility.Collapsed;
        }

        LoadPhoto();

        if (_material.IsWrittenOff)
        {
            WrittenOffBadge.Visibility = Visibility.Visible;
            WrittenOffBanner.Visibility = Visibility.Visible;
            WriteOffInfoPanel.Visibility = !string.IsNullOrWhiteSpace(_material.WrittenOffComment)
                ? Visibility.Visible : Visibility.Collapsed;
            WriteOffCommentText.Text = _material.WrittenOffComment ?? string.Empty;
            WriteOffDateText.Text = _material.WrittenOffAt.HasValue
                ? $"Дата списания: {_material.WrittenOffAt.Value.ToLocalTime():dd.MM.yyyy HH:mm}"
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

            var history = await _vm.GetMaterialHistoryAsync(_material.Id);

            HistoryLoading.Visibility = Visibility.Collapsed;
            if (history.Count == 0)
            {
                HistoryEmpty.Visibility = Visibility.Visible;
            }
            else
            {
                HistoryList.Visibility = Visibility.Visible;
                HistoryList.ItemsSource = history.Take(10).ToList();
            }
        }
        else
        {
            HistorySection.Visibility = Visibility.Collapsed;
        }
    }

    private void LoadPhoto()
    {
        if (string.IsNullOrWhiteSpace(_material.ImagePath) || !File.Exists(_material.ImagePath))
        {
            PhotoPlaceholder.Visibility = Visibility.Visible;
            MaterialPhoto.Visibility = Visibility.Collapsed;
            return;
        }
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(_material.ImagePath, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            MaterialPhoto.Source = bmp;
            MaterialPhoto.Visibility = Visibility.Visible;
            PhotoPlaceholder.Visibility = Visibility.Collapsed;
        }
        catch
        {
            PhotoPlaceholder.Visibility = Visibility.Visible;
            MaterialPhoto.Visibility = Visibility.Collapsed;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
        => MainWindow.Instance?.HideDrawer();

    private void AddQuantity_Click(object sender, RoutedEventArgs e)
    {
        if (MainWindow.Instance is not { } mw) return;
        var overlay = new AddQuantityOverlay(_material, _vm, ReloadFromVmAsync);
        mw.ShowStackedModalOverDrawer(overlay, 520);
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (MainWindow.Instance is not { } mw) return;
        var overlay = new CreateWarehouseItemOverlay(
            "Material", _vm,
            _vm.MaterialCategories.ToList(),
            _vm.EquipmentCategories.ToList(),
            editMaterial: _material,
            afterStackedCloseSuccess: ReloadFromVmAsync);
        mw.ShowStackedModalOverDrawer(overlay, 560);
    }

    private void WriteOff_Click(object sender, RoutedEventArgs e)
    {
        if (MainWindow.Instance is not { } mw) return;
        var overlay = new WriteOffOverlay(
            "материал", _material.Name,
            fullWriteOffAction: comment => _vm.WriteOffMaterialAsync(_material.Id, comment),
            currentQuantity: _material.Quantity,
            unit: _material.Unit,
            partialWriteOffAction: (amount, comment) => _vm.ConsumeMaterialAsync(_material.Id, amount, comment));
        mw.ShowStackedModalOverDrawer(overlay, 540);
    }
}
