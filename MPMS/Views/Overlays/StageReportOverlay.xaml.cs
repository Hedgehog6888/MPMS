using System.Windows;
using System.Windows.Controls;
using MPMS.Models;

namespace MPMS.Views.Overlays;

public partial class StageReportOverlay : UserControl
{
    private readonly LocalTaskStage _stage;
    private readonly LocalTask _task;

    public StageReportOverlay(LocalTaskStage stage, LocalTask task)
    {
        InitializeComponent();
        _stage = stage;
        _task = task;

        LoadStageData();
    }

    private void LoadStageData()
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

        ProgressValue.Text = _stage.Status == StageStatus.Completed ? "100%" : "0%";

        var basePrice = _stage.WorkPricePerUnit;
        var adjustedPrice = basePrice * (1 + _stage.ServicesAdjustmentPercent / 100);
        var workTotal = _stage.WorkQuantity * adjustedPrice;
        WorkTotalValue.Text = $"{workTotal:F2} ₽";

        var materialsTotal = 0m;
        MaterialsTotalValue.Text = $"{materialsTotal:F2} ₽";

        var totalSum = workTotal + materialsTotal;
        TotalSumValue.Text = $"{totalSum:F2} ₽";
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.Instance?.HideDrawer();
    }

    private void GenerateReport_Click(object sender, RoutedEventArgs e)
    {
        // TODO: Generate report
        MainWindow.Instance?.HideDrawer();
    }
}
