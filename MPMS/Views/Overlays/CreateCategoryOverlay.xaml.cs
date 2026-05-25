using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using MPMS.Data;
using MPMS.Models;
using MPMS.ViewModels;

namespace MPMS.Views.Overlays;

public partial class CreateCategoryOverlay : UserControl
{
    private readonly string _mode;
    private readonly WarehouseViewModel? _warehouseVm;
    private readonly CatalogsViewModel? _catalogsVm;
    private readonly IDbContextFactory<LocalDbContext>? _dbFactory;
    private readonly Action<string?> _onCreated;

    // Constructor for WarehouseViewModel (used in CreateWarehouseItemOverlay)
    public CreateCategoryOverlay(string mode, WarehouseViewModel vm, Action<string?> onCreated)
    {
        InitializeComponent();
        _mode = mode;
        _warehouseVm = vm;
        _onCreated = onCreated;

        TitleLabel.Text = mode == "Equipment" ? "Новая категория оборудования" : "Новая категория материала";
        SubtitleLabel.Text = mode == "Equipment"
            ? "Введите название категории оборудования"
            : "Введите название категории материала";
    }

    // Constructor for CatalogsViewModel (used in Catalogs page)
    public CreateCategoryOverlay(string mode, CatalogsViewModel vm, IDbContextFactory<LocalDbContext> dbFactory, Action<string?> onCreated)
    {
        InitializeComponent();
        _mode = mode;
        _catalogsVm = vm;
        _dbFactory = dbFactory;
        _onCreated = onCreated;

        var (title, subtitle) = mode switch
        {
            "Equipment" => ("Новая категория оборудования", "Введите название категории оборудования"),
            "Material" => ("Новая категория материала", "Введите название категории материала"),
            "WorkType" => ("Новая категория видов работ", "Введите название категории видов работ"),
            _ => ("Новая категория", "Введите название категории")
        };
        TitleLabel.Text = title;
        SubtitleLabel.Text = subtitle;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.Instance?.HideDrawer();
        _onCreated(null); // Notify that overlay was closed without creating
    }

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

        if (_warehouseVm is not null)
        {
            // Warehouse mode
            if (_mode == "Equipment")
                await _warehouseVm.SaveNewEquipmentCategoryAsync(name);
            else
                await _warehouseVm.SaveNewMaterialCategoryAsync(name);
        }
        else if (_dbFactory is not null)
        {
            // Catalogs mode - save directly to database
            await using var db = await _dbFactory.CreateDbContextAsync();
            switch (_mode)
            {
                case "Equipment":
                    if (await db.EquipmentCategories.AnyAsync(c => c.Name == name))
                    {
                        ErrorText.Text = "Категория оборудования с таким названием уже существует";
                        ErrorPanel.Visibility = Visibility.Visible;
                        return;
                    }
                    db.EquipmentCategories.Add(new LocalEquipmentCategory { Id = Guid.NewGuid(), Name = name });
                    break;
                case "Material":
                    if (await db.MaterialCategories.AnyAsync(c => c.Name == name))
                    {
                        ErrorText.Text = "Категория материала с таким названием уже существует";
                        ErrorPanel.Visibility = Visibility.Visible;
                        return;
                    }
                    db.MaterialCategories.Add(new LocalMaterialCategory { Id = Guid.NewGuid(), Name = name });
                    break;
                case "WorkType":
                    if (await db.WorkTypeCategories.AnyAsync(c => c.Name == name))
                    {
                        ErrorText.Text = "Категория видов работ с таким названием уже существует";
                        ErrorPanel.Visibility = Visibility.Visible;
                        return;
                    }
                    var maxSort = await db.WorkTypeCategories.Select(c => (int?)c.SortOrder).MaxAsync();
                    db.WorkTypeCategories.Add(new LocalWorkTypeCategory
                    {
                        Id = Guid.NewGuid(),
                        Name = name,
                        IsActive = true,
                        SortOrder = (maxSort ?? 0) + 1
                    });
                    break;
            }
            await db.SaveChangesAsync();
        }

        MainWindow.Instance?.HideDrawer();
        _onCreated(name);
    }
}
