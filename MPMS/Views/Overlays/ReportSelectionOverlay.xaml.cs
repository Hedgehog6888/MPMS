using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPMS.Data;
using MPMS.Models;

namespace MPMS.Views.Overlays;

public partial class ReportSelectionOverlay : UserControl
{
    private readonly string _reportType;
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;

    public ReportSelectionOverlay(string reportType)
    {
        InitializeComponent();
        _reportType = reportType;
        _dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();

        TitleLabel.Text = reportType == "MaterialStock" ? "Отчёт по складу" : "Отчёт по видам работы";
        SubtitleLabel.Text = reportType == "MaterialStock"
            ? "Выберите данные для включения в отчёт по складу"
            : "Выберите данные для включения в отчёт по видам работы";

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
            // Load material categories
            var materialCategories = await db.MaterialCategories
                .OrderBy(c => c.Name)
                .Select(c => new { Name = c.Name })
                .ToListAsync();
            MaterialCategoryCheckboxes.ItemsSource = materialCategories;

            // Load equipment categories
            var equipmentCategories = await db.EquipmentCategories
                .OrderBy(c => c.Name)
                .Select(c => new { Name = c.Name })
                .ToListAsync();
            EquipmentCategoryCheckboxes.ItemsSource = equipmentCategories;
        }
        else if (_reportType == "WorkType")
        {
            // Load work type categories
            var workTypeCategories = await db.WorkTypeCategories
                .Where(c => c.IsActive)
                .OrderBy(c => c.SortOrder)
                .ThenBy(c => c.Name)
                .Select(c => new { Name = c.Name })
                .ToListAsync();

            WorkTypeCategoryCheckboxes.ItemsSource = workTypeCategories;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.Instance?.HideDrawer();
    }

    private void GenerateReport_Click(object sender, RoutedEventArgs e)
    {
        // TODO: Generate report based on selections
        // For now, just close the overlay
        MainWindow.Instance?.HideDrawer();
    }
}
