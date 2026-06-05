using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPMS.Data;
using MPMS.Models;
using MPMS.Services;
using MPMS.ViewModels;

namespace MPMS.Views.Overlays;

public partial class StageReportOverlay : UserControl
{
    private readonly LocalTaskStage _stage;
    private readonly LocalTask _task;
    private readonly StageReportService _stageReportService;
    private readonly SidebarFooterViewModel _sidebarFooter;

    public StageReportOverlay(LocalTaskStage stage, LocalTask task)
    {
        InitializeComponent();
        _stage = stage;
        _task = task;
        _stageReportService = App.Services.GetRequiredService<StageReportService>();
        _sidebarFooter = App.Services.GetRequiredService<SidebarFooterViewModel>();

        LoadStageData();
    }

    private async void LoadStageData()
    {
        StageNameValue.Text = _stage.Name;
        DescriptionValue.Text = _stage.Description ?? "—";
        DescriptionTooltip.Content = _stage.Description ?? "—";
        ProjectNameValue.Text = _task.ProjectName ?? "—";
        TaskNameValue.Text = _task.Name ?? "—";
        ClientValue.Text = _stage.ProjectClient ?? "—";
        AddressValue.Text = _stage.ProjectAddress ?? "—";
        StartDateValue.Text = _stage.StageStartDate?.ToString("dd.MM.yyyy") ?? "—";
        EndDateValue.Text = _stage.StageEndDate?.ToString("dd.MM.yyyy") ?? "—";

        StatusValue.Text = _stage.Status switch
        {
            StageStatus.Planned => "Запланирован",
            StageStatus.InProgress => "В работе",
            StageStatus.Completed => "Завершён",
            _ => _stage.Status.ToString()
        };

        // Load work types and materials from database to calculate correct totals
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        var stageWorkTypes = await db.StageWorkTypes
            .Where(swt => swt.StageId == _stage.Id)
            .ToListAsync();

        var stageMaterials = await db.StageMaterials
            .Where(sm => sm.StageId == _stage.Id)
            .ToListAsync();

        // Calculate work types total with stage adjustment
        var serviceK = 1m + _stage.ServicesAdjustmentPercent / 100m;
        var workTypesTotal = stageWorkTypes.Sum(swt => swt.Quantity * swt.PricePerUnit) * serviceK;

        // Calculate materials total with stage adjustment
        var materialK = 1m + _stage.MaterialsAdjustmentPercent / 100m;
        var materialsTotal = stageMaterials.Sum(sm => sm.Quantity * sm.PricePerUnit) * materialK;

        var totalSum = workTypesTotal + materialsTotal;

        WorkTotalValue.Text = $"{workTypesTotal:F2} ₽";
        MaterialsTotalValue.Text = $"{materialsTotal:F2} ₽";
        TotalSumValue.Text = $"{totalSum:F2} ₽";
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.Instance?.HideDrawer();
    }

    private async void GenerateReport_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.Instance?.HideDrawer();

        _sidebarFooter.BeginReportGeneration("Генерация отчёта по этапу...");

        try
        {
            await _stageReportService.GenerateStageReportAsync(_stage.Id);
            _sidebarFooter.CompleteReportGeneration("Отчёт по этапу создан");
            await Task.Delay(100); // Small delay to ensure DB transaction completes
            MainWindow.Instance?.RefreshFilesPage();
        }
        catch
        {
            _sidebarFooter.CancelReportGeneration();
        }
    }
}
