using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using MPMS.Models;
using MPMS.ViewModels;

namespace MPMS.Views.Overlays;

public partial class CreateWarehouseItemOverlay : UserControl
{
    private readonly string _mode;
    private readonly bool _isEdit;
    private readonly WarehouseViewModel _vm;
    private readonly LocalMaterial? _editMaterial;
    private readonly LocalEquipment? _editEquipment;

    // Display items for unit combo
    private record UnitDisplayItem(string Display, string Short, bool IsInteger);

    public CreateWarehouseItemOverlay(
        string mode,
        WarehouseViewModel vm,
        List<LocalMaterialCategory> materialCategories,
        List<LocalEquipmentCategory> equipmentCategories,
        LocalMaterial? editMaterial = null,
        LocalEquipment? editEquipment = null)
    {
        InitializeComponent();
        _mode = mode;
        _vm = vm;
        _isEdit = editMaterial is not null || editEquipment is not null;
        _editMaterial = editMaterial;
        _editEquipment = editEquipment;

        if (mode == "Equipment")
        {
            TitleLabel.Text = _isEdit ? "Редактировать оборудование" : "Добавить оборудование";
            SubtitleLabel.Text = _isEdit ? "Измените данные оборудования" : "Заполните информацию об оборудовании";
            SaveButton.Content = _isEdit ? "Сохранить" : "Добавить";
            CategoryLabel.Text = "Категория оборудования";
            CategoryCombo.ItemsSource = equipmentCategories;
            UnitPanel.Visibility = Visibility.Collapsed;
            QuantityCostPanel.Visibility = Visibility.Collapsed;
            CostOnlyPanel.Visibility = Visibility.Collapsed;
            InvNumberPanel.Visibility = Visibility.Visible;

            if (editEquipment is not null)
            {
                NameBox.Text = editEquipment.Name;
                DescriptionBox.Text = editEquipment.Description ?? string.Empty;
                InvNumberBox.Text = editEquipment.InventoryNumber ?? string.Empty;
                CategoryCombo.SelectedItem = equipmentCategories.FirstOrDefault(c => c.Id == editEquipment.CategoryId);
                SetPhotoPath(editEquipment.ImagePath);
            }
        }
        else
        {
            TitleLabel.Text = _isEdit ? "Редактировать материал" : "Добавить материал";
            SubtitleLabel.Text = _isEdit ? "Измените данные материала" : "Заполните информацию о материале";
            SaveButton.Content = _isEdit ? "Сохранить" : "Добавить";
            CategoryCombo.ItemsSource = materialCategories;

            // Populate unit combo
            var units = MaterialUnits.All
                .Select(u => new UnitDisplayItem($"{u.Display}  ({u.Short})", u.Short, u.IsInteger))
                .ToList();
            UnitCombo.ItemsSource = units;
            UnitCombo.DisplayMemberPath = "Display";

            if (_isEdit)
            {
                QuantityCostPanel.Visibility = Visibility.Collapsed;
                CostOnlyPanel.Visibility = Visibility.Visible;
            }

            if (editMaterial is not null)
            {
                NameBox.Text = editMaterial.Name;
                DescriptionBox.Text = editMaterial.Description ?? string.Empty;
                CategoryCombo.SelectedItem = materialCategories.FirstOrDefault(c => c.Id == editMaterial.CategoryId);
                SetPhotoPath(editMaterial.ImagePath);

                // Select matching unit
                if (!string.IsNullOrWhiteSpace(editMaterial.Unit))
                {
                    var match = units.FirstOrDefault(u => u.Short == editMaterial.Unit);
                    if (match is not null) UnitCombo.SelectedItem = match;
                }

                if (editMaterial.Cost.HasValue)
                    CostEditBox.Text = editMaterial.Cost.Value.ToString("G");
            }
        }
    }

    private void SetPhotoPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            ImagePathBox.Text = string.Empty;
            PhotoPreviewBorder.Visibility = Visibility.Collapsed;
            return;
        }
        ImagePathBox.Text = path;
        TryShowPreview(path);
    }

    private void TryShowPreview(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            PhotoPreviewBorder.Visibility = Visibility.Collapsed;
            return;
        }
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            PhotoPreview.Source = bmp;
            PhotoPreviewBorder.Visibility = Visibility.Visible;
        }
        catch
        {
            PhotoPreviewBorder.Visibility = Visibility.Collapsed;
        }
    }

    private void UnitCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_isEdit && UnitCombo.SelectedItem is UnitDisplayItem u)
        {
            QuantityLabel.Text = u.IsInteger
                ? "Начальное количество (целое)"
                : "Начальное количество";
        }
    }

    private void BrowseImage_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Выберите изображение",
            Filter = "Изображения|*.jpg;*.jpeg;*.png;*.bmp|Все файлы|*.*"
        };
        if (dlg.ShowDialog() == true)
            SetPhotoPath(dlg.FileName);
    }

    private void RemovePhoto_Click(object sender, RoutedEventArgs e)
    {
        PhotoPreview.Source = null;
        PhotoPreviewBorder.Visibility = Visibility.Collapsed;
        ImagePathBox.Text = string.Empty;
    }

    private void AddCategory_Click(object sender, RoutedEventArgs e)
    {
        if (MainWindow.Instance is not { } mw) return;
        var overlay = new CreateCategoryOverlay(_mode, _vm, newCat =>
        {
            if (_mode == "Equipment")
            {
                var cats = _vm.EquipmentCategories.ToList();
                CategoryCombo.ItemsSource = cats;
                CategoryCombo.SelectedItem = cats.FirstOrDefault(c => c.Name == newCat);
            }
            else
            {
                var cats = _vm.MaterialCategories.ToList();
                CategoryCombo.ItemsSource = cats;
                CategoryCombo.SelectedItem = cats.FirstOrDefault(c => c.Name == newCat);
            }
        });
        mw.ShowStackedModalOverDrawer(overlay, 420);
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => MainWindow.Instance?.HideDrawer();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorPanel.Visibility = Visibility.Collapsed;

        var name = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ShowError("Введите название");
            return;
        }

        var imagePath = ImagePathBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(imagePath) && !File.Exists(imagePath))
        {
            ShowError("Указанный файл изображения не найден");
            return;
        }

        var description = string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim();
        var image = string.IsNullOrWhiteSpace(imagePath) ? null : imagePath;

        Guid? categoryId = null;
        string? categoryName = null;
        if (CategoryCombo.SelectedItem is LocalMaterialCategory mc)
        {
            categoryId = mc.Id;
            categoryName = mc.Name;
        }
        else if (CategoryCombo.SelectedItem is LocalEquipmentCategory ec)
        {
            categoryId = ec.Id;
            categoryName = ec.Name;
        }

        if (_mode == "Equipment")
        {
            var invNumber = string.IsNullOrWhiteSpace(InvNumberBox.Text) ? null : InvNumberBox.Text.Trim();
            MainWindow.Instance?.HideAllOverlays();
            if (_isEdit && _editEquipment is not null)
                _ = _vm.UpdateEquipmentAsync(_editEquipment.Id, name, description, categoryId, categoryName, image, invNumber);
            else
                _ = _vm.SaveNewEquipmentAsync(name, description, categoryId, categoryName, image, invNumber);
        }
        else
        {
            var selectedUnit = (UnitCombo.SelectedItem as UnitDisplayItem)?.Short;
            decimal? cost = null;
            var costText = _isEdit ? CostEditBox.Text : CostBox.Text;
            if (!string.IsNullOrWhiteSpace(costText))
            {
                if (!decimal.TryParse(costText.Replace(',', '.'), System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var c) || c < 0)
                {
                    ShowError("Некорректная стоимость");
                    return;
                }
                cost = c;
            }

            if (!_isEdit)
            {
                var qtyRaw = QuantityBox.Text.Trim();
                var qty = MaterialUnits.ParseQuantity(qtyRaw, selectedUnit);
                if (qty is null || qty < 0)
                {
                    var isInt = MaterialUnits.IsIntegerUnit(selectedUnit);
                    ShowError(isInt
                        ? "Введите целое неотрицательное число"
                        : "Некорректное начальное количество");
                    return;
                }
                MainWindow.Instance?.HideAllOverlays();
                _ = _vm.SaveNewMaterialAsync(name, selectedUnit, description, categoryId, categoryName, image, qty.Value, cost);
            }
            else if (_editMaterial is not null)
            {
                MainWindow.Instance?.HideAllOverlays();
                _ = _vm.UpdateMaterialAsync(_editMaterial.Id, name, selectedUnit, description, categoryId, categoryName, image, cost);
            }
        }
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
    }
}
