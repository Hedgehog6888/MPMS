using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    /// <summary>Предпросмотр номера при создании; при сохранении передаётся в VM как предпочтительный.</summary>
    private string? _preferredInventoryForSave;
    private readonly Func<Task>? _afterStackedCloseSuccess;

    // Display items for unit combo
    private record UnitDisplayItem(string Display, string Short, bool IsInteger);
    private record EquipmentConditionItem(string Display, EquipmentCondition Value);

    public CreateWarehouseItemOverlay(
        string mode,
        WarehouseViewModel vm,
        List<LocalMaterialCategory> materialCategories,
        List<LocalEquipmentCategory> equipmentCategories,
        LocalMaterial? editMaterial = null,
        LocalEquipment? editEquipment = null,
        Func<Task>? afterStackedCloseSuccess = null)
    {
        InitializeComponent();
        Loaded += OnOverlayLoaded;
        _mode = mode;
        _vm = vm;
        _afterStackedCloseSuccess = afterStackedCloseSuccess;
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
            EquipmentConditionPanel.Visibility = Visibility.Visible;
            QuantityCostPanel.Visibility = Visibility.Collapsed;
            CostOnlyPanel.Visibility = Visibility.Collapsed;

            var conditions = new List<EquipmentConditionItem>
            {
                new("Исправно", EquipmentCondition.Good),
                new("Требует обслуживания", EquipmentCondition.NeedsMaintenance),
                new("Неисправно", EquipmentCondition.Faulty)
            };
            EquipmentConditionCombo.ItemsSource = conditions;

            if (editEquipment is not null)
            {
                NameBox.Text = editEquipment.Name;
                DescriptionBox.Text = editEquipment.Description ?? string.Empty;
                CategoryCombo.SelectedItem = equipmentCategories.FirstOrDefault(c => c.Id == editEquipment.CategoryId);
                SetPhotoPath(editEquipment.ImagePath);
                var parsed = Enum.TryParse<EquipmentCondition>(editEquipment.Condition, out var parsedCondition)
                    ? parsedCondition
                    : EquipmentCondition.Good;
                EquipmentConditionCombo.SelectedItem = conditions.FirstOrDefault(c => c.Value == parsed);
                HeaderInventoryLine.Visibility = Visibility.Visible;
                HeaderInventoryLine.Text = $"Инв. № {editEquipment.InventoryNumber ?? "—"}";
            }
            else
            {
                EquipmentConditionCombo.SelectedItem = conditions.First(c => c.Value == EquipmentCondition.Good);
                HeaderInventoryLine.Visibility = Visibility.Visible;
                HeaderInventoryLine.Text = "Инв. № …";
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
                HeaderInventoryLine.Visibility = Visibility.Visible;
                HeaderInventoryLine.Text = $"Инв. № {editMaterial.InventoryNumber ?? "—"}";

                // Select matching unit
                if (!string.IsNullOrWhiteSpace(editMaterial.Unit))
                {
                    var match = units.FirstOrDefault(u => u.Short == editMaterial.Unit);
                    if (match is not null) UnitCombo.SelectedItem = match;
                }

                if (editMaterial.Cost.HasValue)
                    CostEditBox.Text = FormatCostDisplay(editMaterial.Cost.Value);
            }
            else
            {
                HeaderInventoryLine.Visibility = Visibility.Visible;
                HeaderInventoryLine.Text = "Инв. № …";
            }

            SetupMaterialNumericFields();
            UpdateQuantityLabelForUnit();
        }
    }

    private async void OnOverlayLoaded(object sender, RoutedEventArgs e)
    {
        if (_isEdit) return;
        try
        {
            var n = _mode == "Equipment"
                ? await _vm.PeekNextEquipmentInventoryNumberAsync()
                : await _vm.PeekNextMaterialInventoryNumberAsync();
            HeaderInventoryLine.Text = $"Инв. № {n}";
            _preferredInventoryForSave = n;
        }
        catch
        {
            HeaderInventoryLine.Text = "Инв. № —";
        }
    }

    private void SetupMaterialNumericFields()
    {
        if (_mode == "Equipment") return;

        QuantityBox.PreviewTextInput += DecimalField_PreviewTextInput;
        DataObject.AddPastingHandler(QuantityBox, DecimalField_Pasting);
        QuantityBox.LostFocus += (_, _) => FormatQuantityOnBlur();

        CostBox.PreviewTextInput += DecimalField_PreviewTextInput;
        DataObject.AddPastingHandler(CostBox, DecimalField_Pasting);
        CostBox.LostFocus += (_, _) => FormatCostOnBlur(CostBox);

        CostEditBox.PreviewTextInput += DecimalField_PreviewTextInput;
        DataObject.AddPastingHandler(CostEditBox, DecimalField_Pasting);
        CostEditBox.LostFocus += (_, _) => FormatCostOnBlur(CostEditBox);
    }

    private static string FormatCostDisplay(decimal value)
        => value.ToString("0.00", CultureInfo.InvariantCulture);

    private void UpdateQuantityLabelForUnit()
    {
        if (_mode == "Equipment" || _isEdit) return;
        if (UnitCombo.SelectedItem is not UnitDisplayItem u) return;
        QuantityLabel.Text = u.IsInteger
            ? "Количество * (целое)"
            : "Количество *";
    }

    private void FormatQuantityOnBlur()
    {
        var raw = QuantityBox.Text.Trim();
        if (string.IsNullOrEmpty(raw)) return;
        var unit = (UnitCombo.SelectedItem as UnitDisplayItem)?.Short;
        if (!decimal.TryParse(raw.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
            return;
        if (MaterialUnits.IsIntegerUnit(unit))
            QuantityBox.Text = decimal.Truncate(d).ToString("0", CultureInfo.InvariantCulture);
        else
            QuantityBox.Text = d.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static void FormatCostOnBlur(TextBox tb)
    {
        var raw = tb.Text.Trim();
        if (string.IsNullOrEmpty(raw)) return;
        if (!decimal.TryParse(raw.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out var d))
            return;
        tb.Text = d.ToString("0.00", CultureInfo.InvariantCulture);
    }

    private static void DecimalField_PreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is not TextBox tb) return;
        var selStart = tb.SelectionStart;
        var selLen = tb.SelectionLength;
        var proposed = tb.Text[..selStart] + e.Text + tb.Text[(selStart + selLen)..];
        if (!IsValidPartialDecimal(proposed))
            e.Handled = true;
    }

    private static void DecimalField_Pasting(object sender, DataObjectPastingEventArgs e)
    {
        if (e.DataObject.GetDataPresent(typeof(string)) != true || sender is not TextBox tb) return;
        var paste = (string)e.DataObject.GetData(typeof(string))!;
        var selStart = tb.SelectionStart;
        var selLen = tb.SelectionLength;
        var proposed = tb.Text[..selStart] + paste + tb.Text[(selStart + selLen)..];
        if (!IsValidPartialDecimal(proposed))
            e.CancelCommand();
    }

    /// <summary>Допускает только цифры и один разделитель; пустая строка ок.</summary>
    private static bool IsValidPartialDecimal(string s)
    {
        if (string.IsNullOrEmpty(s)) return true;
        s = s.Replace(',', '.');
        var dot = s.IndexOf('.');
        if (dot < 0)
            return s.All(char.IsDigit);
        if (s[(dot + 1)..].Contains('.')) return false;
        foreach (var part in s.Split('.'))
        {
            if (string.IsNullOrEmpty(part)) continue;
            if (!part.All(char.IsDigit)) return false;
        }
        return true;
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
        UpdateQuantityLabelForUnit();
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

    private async void Save_Click(object sender, RoutedEventArgs e)
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

        var mw = MainWindow.Instance;
        var stacked = mw?.HasStackedModalOverDrawer == true;

        if (_mode == "Equipment")
        {
            if (EquipmentConditionCombo.SelectedItem is not EquipmentConditionItem conditionItem)
            {
                ShowError("Выберите состояние оборудования");
                return;
            }

            if (_isEdit && _editEquipment is not null)
                await _vm.UpdateEquipmentAsync(_editEquipment.Id, name, description, categoryId, categoryName, image, conditionItem.Value);
            else
                await _vm.SaveNewEquipmentAsync(name, description, categoryId, categoryName, image, conditionItem.Value, _preferredInventoryForSave);

            if (stacked)
            {
                mw!.HideDrawer();
                if (_afterStackedCloseSuccess is not null)
                    await _afterStackedCloseSuccess();
            }
            else
                mw?.HideAllOverlays();

            return;
        }

        if (UnitCombo.SelectedItem is not UnitDisplayItem unitItem)
        {
            ShowError("Выберите единицу измерения");
            return;
        }

        var selectedUnit = unitItem.Short;

        var costText = (_isEdit ? CostEditBox.Text : CostBox.Text).Trim();
        if (string.IsNullOrWhiteSpace(costText))
        {
            ShowError("Введите стоимость за единицу");
            return;
        }

        if (!decimal.TryParse(costText.Replace(',', '.'), NumberStyles.Any,
                CultureInfo.InvariantCulture, out var costVal) || costVal < 0)
        {
            ShowError("Некорректная стоимость");
            return;
        }

        var cost = costVal;

        if (!_isEdit)
        {
            var qtyRaw = QuantityBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(qtyRaw))
            {
                ShowError("Введите количество");
                return;
            }

            var qty = MaterialUnits.ParseQuantity(qtyRaw, selectedUnit);
            if (qty is null || qty < 0)
            {
                var isInt = MaterialUnits.IsIntegerUnit(selectedUnit);
                ShowError(isInt
                    ? "Введите целое неотрицательное число"
                    : "Некорректное количество");
                return;
            }

            await _vm.SaveNewMaterialAsync(name, selectedUnit, description, categoryId, categoryName, image, qty.Value, cost, _preferredInventoryForSave);
        }
        else if (_editMaterial is not null)
            await _vm.UpdateMaterialAsync(_editMaterial.Id, name, selectedUnit, description, categoryId, categoryName, image, cost);

        if (stacked)
        {
            mw!.HideDrawer();
            if (_afterStackedCloseSuccess is not null)
                await _afterStackedCloseSuccess();
        }
        else
            mw?.HideAllOverlays();
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
    }
}
