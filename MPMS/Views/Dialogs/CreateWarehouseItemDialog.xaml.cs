using System.IO;
using System.Windows;
using MPMS.Models;
using Microsoft.Win32;

namespace MPMS.Views.Dialogs;

public partial class CreateWarehouseItemDialog : Window
{
    private readonly string _mode; // "Material" or "Equipment"
    private readonly bool _isEdit;

    // Output properties
    public string ItemName { get; private set; } = string.Empty;
    public string? Unit { get; private set; }
    public string? ItemDescription { get; private set; }
    public Guid? SelectedCategoryId { get; private set; }
    public string? SelectedCategoryName { get; private set; }
    public string? ImagePath { get; private set; }
    public decimal InitialQuantity { get; private set; }
    public string? InventoryNumber { get; private set; }

    public CreateWarehouseItemDialog(
        string mode,
        List<LocalMaterialCategory> materialCategories,
        List<LocalEquipmentCategory> equipmentCategories,
        LocalMaterial? editMaterial = null,
        LocalEquipment? editEquipment = null)
    {
        InitializeComponent();
        _mode = mode;
        _isEdit = editMaterial is not null || editEquipment is not null;

        if (mode == "Equipment")
        {
            TitleText.Text = _isEdit ? "Редактировать оборудование" : "Добавить оборудование";
            Title = TitleText.Text;
            SaveButton.Content = _isEdit ? "Сохранить" : "Добавить";
            CategoryLabel.Text = "Категория оборудования";
            CategoryCombo.ItemsSource = equipmentCategories;
            UnitPanel.Visibility = Visibility.Collapsed;
            QuantityPanel.Visibility = Visibility.Collapsed;
            InvNumberPanel.Visibility = Visibility.Visible;

            if (editEquipment is not null)
            {
                NameBox.Text = editEquipment.Name;
                DescriptionBox.Text = editEquipment.Description ?? string.Empty;
                ImagePathBox.Text = editEquipment.ImagePath ?? string.Empty;
                InvNumberBox.Text = editEquipment.InventoryNumber ?? string.Empty;
                CategoryCombo.SelectedItem = equipmentCategories.FirstOrDefault(c => c.Id == editEquipment.CategoryId);
            }
        }
        else
        {
            TitleText.Text = _isEdit ? "Редактировать материал" : "Добавить материал";
            Title = TitleText.Text;
            SaveButton.Content = _isEdit ? "Сохранить" : "Добавить";
            CategoryCombo.ItemsSource = materialCategories;
            if (_isEdit) QuantityPanel.Visibility = Visibility.Collapsed;

            if (editMaterial is not null)
            {
                NameBox.Text = editMaterial.Name;
                UnitBox.Text = editMaterial.Unit ?? string.Empty;
                DescriptionBox.Text = editMaterial.Description ?? string.Empty;
                ImagePathBox.Text = editMaterial.ImagePath ?? string.Empty;
                CategoryCombo.SelectedItem = materialCategories.FirstOrDefault(c => c.Id == editMaterial.CategoryId);
            }
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
            ImagePathBox.Text = dlg.FileName;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        var name = NameBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            ErrorText.Text = "Введите название";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        if (_mode != "Equipment" && !_isEdit)
        {
            if (!decimal.TryParse(QuantityBox.Text.Replace(',', '.'), System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var qty) || qty < 0)
            {
                ErrorText.Text = "Некорректное начальное количество";
                ErrorText.Visibility = Visibility.Visible;
                return;
            }
            InitialQuantity = qty;
        }

        var imagePath = ImagePathBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(imagePath) && !File.Exists(imagePath))
        {
            ErrorText.Text = "Указанный файл не найден";
            ErrorText.Visibility = Visibility.Visible;
            return;
        }

        ItemName = name;
        Unit = string.IsNullOrWhiteSpace(UnitBox.Text) ? null : UnitBox.Text.Trim();
        ItemDescription = string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim();
        ImagePath = string.IsNullOrWhiteSpace(imagePath) ? null : imagePath;
        InventoryNumber = string.IsNullOrWhiteSpace(InvNumberBox.Text) ? null : InvNumberBox.Text.Trim();

        if (CategoryCombo.SelectedItem is LocalMaterialCategory mc)
        {
            SelectedCategoryId = mc.Id;
            SelectedCategoryName = mc.Name;
        }
        else if (CategoryCombo.SelectedItem is LocalEquipmentCategory ec)
        {
            SelectedCategoryId = ec.Id;
            SelectedCategoryName = ec.Name;
        }
        else
        {
            SelectedCategoryId = null;
            SelectedCategoryName = null;
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => DialogResult = false;
}
