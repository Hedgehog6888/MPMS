using System.Windows;
using System.Windows.Controls;
using System.Collections.ObjectModel;
using System.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPMS.Data;
using MPMS.Models;
using MPMS.Services;
using MPMS.ViewModels;

namespace MPMS.Views.Overlays;

public class CategorySelectionItem : INotifyPropertyChanged
{
    private bool _isSelected;

    public string Name { get; set; } = string.Empty;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public partial class ReportSelectionOverlay : UserControl
{
    private readonly string _reportType;
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;
    private readonly WarehouseReportService _warehouseReportService;
    private readonly PriceListService _priceListService;
    private readonly SidebarFooterViewModel _sidebarFooter;

    public ReportSelectionOverlay(string reportType)
    {
        InitializeComponent();
        _reportType = reportType;
        _dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        _warehouseReportService = App.Services.GetRequiredService<WarehouseReportService>();
        _priceListService = App.Services.GetRequiredService<PriceListService>();
        _sidebarFooter = App.Services.GetRequiredService<SidebarFooterViewModel>();

        TitleLabel.Text = reportType == "MaterialStock" ? "Отчёт по складу" : "Прайс лист";
        SubtitleLabel.Text = reportType == "MaterialStock"
            ? "Выберите данные для включения в отчёт по складу"
            : "Выберите данные для включения в прайс лист";

        // Hide data types section for work type report
        if (reportType == "WorkType")
        {
            DataTypesPanel.Visibility = Visibility.Collapsed;
            WorkTypeCategoriesSection.Visibility = Visibility.Visible;
        }
        else
        {
            // For material stock, show both sections initially based on checkboxes
            UpdateCategorySectionsVisibility();
            IncludeMaterials.Checked += (s, e) => UpdateCategorySectionsVisibility();
            IncludeMaterials.Unchecked += (s, e) => UpdateCategorySectionsVisibility();
            IncludeEquipment.Checked += (s, e) => UpdateCategorySectionsVisibility();
            IncludeEquipment.Unchecked += (s, e) => UpdateCategorySectionsVisibility();
        }

        // Setup category selection panel enabled state
        SpecificCategories.Checked += (s, e) =>
        {
            var panel = FindName("CategorySelectionPanel") as Border;
            if (panel != null) panel.IsEnabled = true;
        };
        AllCategories.Checked += (s, e) =>
        {
            var panel = FindName("CategorySelectionPanel") as Border;
            if (panel != null) panel.IsEnabled = false;
        };

        LoadCategories();
    }

    private void UpdateCategorySectionsVisibility()
    {
        if (_reportType == "MaterialStock")
        {
            MaterialCategoriesSection.Visibility = IncludeMaterials.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
            EquipmentCategoriesSection.Visibility = IncludeEquipment.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private async void LoadCategories()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();

        if (_reportType == "MaterialStock")
        {
            // Load material categories that have materials
            var materialCategories = await db.Materials
                .Where(m => !m.IsArchived)
                .Where(m => m.CategoryName != null)
                .Select(m => m.CategoryName)
                .Distinct()
                .OrderBy(name => name)
                .Select(name => new CategorySelectionItem { Name = name! })
                .ToListAsync();
            MaterialCategoryCheckboxes.ItemsSource = materialCategories;

            // Load equipment categories that have equipment
            var equipmentCategories = await db.Equipments
                .Where(e => !e.IsArchived)
                .Where(e => e.CategoryName != null)
                .Select(e => e.CategoryName)
                .Distinct()
                .OrderBy(name => name)
                .Select(name => new CategorySelectionItem { Name = name! })
                .ToListAsync();
            EquipmentCategoryCheckboxes.ItemsSource = equipmentCategories;
        }
        else if (_reportType == "WorkType")
        {
            // Load work type categories that have active work types
            var workTypeCategories = await db.WorkTypeTemplates
                .Where(w => w.IsActive)
                .Select(w => w.CategoryName)
                .Distinct()
                .OrderBy(name => name)
                .Select(name => new CategorySelectionItem { Name = name! })
                .ToListAsync();

            WorkTypeCategoryCheckboxes.ItemsSource = workTypeCategories;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.Instance?.HideDrawer();
    }

    private async void GenerateReport_Click(object sender, RoutedEventArgs e)
    {
        if (_reportType == "MaterialStock")
        {
            var includeMaterials = IncludeMaterials.IsChecked == true;
            var includeEquipment = IncludeEquipment.IsChecked == true;
            var allCategories = AllCategories.IsChecked == true;

            var selectedMaterialCategories = new ObservableCollection<string>();
            var selectedEquipmentCategories = new ObservableCollection<string>();

            if (!allCategories)
            {
                foreach (CategorySelectionItem item in MaterialCategoryCheckboxes.Items)
                {
                    if (item.IsSelected)
                    {
                        selectedMaterialCategories.Add(item.Name);
                    }
                }

                foreach (CategorySelectionItem item in EquipmentCategoryCheckboxes.Items)
                {
                    if (item.IsSelected)
                    {
                        selectedEquipmentCategories.Add(item.Name);
                    }
                }
            }

            // Close overlay immediately and start async generation
            MainWindow.Instance?.HideDrawer();

            // Start report generation with progress tracking
            _sidebarFooter.BeginReportGeneration("Генерация отчёта по складу...");
            
            try
            {
                await _warehouseReportService.GenerateWarehouseReportAsync(
                    includeMaterials,
                    includeEquipment,
                    allCategories,
                    selectedMaterialCategories,
                    selectedEquipmentCategories);

                _sidebarFooter.CompleteReportGeneration("Отчёт по складу создан");
                MainWindow.Instance?.RefreshFilesPage();
            }
            catch
            {
                _sidebarFooter.CancelReportGeneration();
                // TODO: Show error to user
            }
        }
        else if (_reportType == "WorkType")
        {
            var allCategories = AllCategories.IsChecked == true;
            var selectedCategories = new ObservableCollection<string>();

            if (!allCategories)
            {
                foreach (CategorySelectionItem item in WorkTypeCategoryCheckboxes.Items)
                {
                    if (item.IsSelected)
                    {
                        selectedCategories.Add(item.Name);
                    }
                }
            }

            // Close overlay immediately and start async generation
            MainWindow.Instance?.HideDrawer();

            // Start price list generation with progress tracking
            _sidebarFooter.BeginReportGeneration("Генерация прайс листа...");
            
            try
            {
                await _priceListService.GeneratePriceListAsync(
                    allCategories,
                    selectedCategories);

                _sidebarFooter.CompleteReportGeneration("Прайс лист создан");
                MainWindow.Instance?.RefreshFilesPage();
            }
            catch
            {
                _sidebarFooter.CancelReportGeneration();
                // TODO: Show error to user
            }
        }
    }
}
