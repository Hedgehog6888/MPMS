using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPMS.Data;
using MPMS.Models;
using MPMS.Services;
using MPMS.ViewModels;

namespace MPMS.Views.Overlays;

public partial class KS2ReportOverlay : UserControl
{
    private readonly LocalProject _project;
    private readonly KS2ReportService _ks2ReportService;
    private readonly SidebarFooterViewModel _sidebarFooter;

    public KS2ReportOverlay(LocalProject project)
    {
        InitializeComponent();
        _project = project;
        _ks2ReportService = App.Services.GetRequiredService<KS2ReportService>();
        _sidebarFooter = App.Services.GetRequiredService<SidebarFooterViewModel>();

        LoadProjectData();
    }

    private async void LoadProjectData()
    {
        ProjectNameValue.Text = _project.Name;
        DescriptionValue.Text = _project.Description ?? "—";
        DescriptionTooltip.Content = _project.Description ?? "—";
        ClientValue.Text = _project.Client ?? "—";
        AddressValue.Text = _project.Address ?? "—";
        StartDateValue.Text = _project.StartDate?.ToString("dd.MM.yyyy") ?? "—";
        EndDateValue.Text = _project.EndDate?.ToString("dd.MM.yyyy") ?? "—";
        
        StatusValue.Text = _project.Status switch
        {
            ProjectStatus.Planning => "Планирование",
            ProjectStatus.InProgress => "В работе",
            ProjectStatus.Completed => "Завершён",
            ProjectStatus.Cancelled => "Отменён",
            ProjectStatus.Closed => "Закрыт",
            _ => _project.Status.ToString()
        };

        ProgressValue.Text = $"{_project.ProgressPercent}%";

        // Load work types from database to calculate correct total
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        var tasks = await db.Tasks
            .Where(t => t.ProjectId == _project.Id && !t.IsArchived)
            .ToListAsync();

        var taskIds = tasks.Select(t => t.Id).ToList();
        var stages = await db.TaskStages
            .Where(s => taskIds.Contains(s.TaskId) && !s.IsArchived)
            .ToListAsync();

        var stageIds = stages.Select(s => s.Id).ToList();
        var stageWorkTypes = await db.StageWorkTypes
            .Where(swt => stageIds.Contains(swt.StageId))
            .ToListAsync();

        // Calculate work types total with stage adjustments
        decimal workTypesTotal = 0;
        foreach (var stage in stages)
        {
            var serviceK = 1m + stage.ServicesAdjustmentPercent / 100m;
            var stageWorkTypesTotal = stageWorkTypes
                .Where(swt => swt.StageId == stage.Id)
                .Sum(swt => swt.Quantity * swt.PricePerUnit) * serviceK;
            workTypesTotal += stageWorkTypesTotal;
        }

        WorkTotalValue.Text = $"{workTypesTotal:F2} ₽";
        TotalSumValue.Text = $"{workTypesTotal:F2} ₽";
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.Instance?.HideDrawer();
    }

    private async void GenerateReport_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.Instance?.HideDrawer();

        _sidebarFooter.BeginReportGeneration("Генерация отчёта КС-2...");

        try
        {
            await _ks2ReportService.GenerateKS2ReportAsync(_project.Id);
            _sidebarFooter.CompleteReportGeneration("Отчёт КС-2 создан");
            MainWindow.Instance?.RefreshFilesPage();
        }
        catch (Exception ex)
        {
            _sidebarFooter.CancelReportGeneration();
            MessageBox.Show($"Ошибка при генерации отчёта: {ex.Message}", "Ошибка", 
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
