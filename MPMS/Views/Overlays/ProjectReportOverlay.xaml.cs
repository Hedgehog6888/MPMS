using System.Windows;
using System.Windows.Controls;
using MPMS.Models;

namespace MPMS.Views.Overlays;

public partial class ProjectReportOverlay : UserControl
{
    private readonly LocalProject _project;

    public ProjectReportOverlay(LocalProject project)
    {
        InitializeComponent();
        _project = project;

        LoadProjectData();
    }

    private void LoadProjectData()
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

        TotalTasksValue.Text = _project.TotalTasks.ToString();
        CompletedTasksValue.Text = _project.CompletedTasks.ToString();

        TotalStagesValue.Text = _project.TotalStages.ToString();
        CompletedStagesValue.Text = _project.CompletedStages.ToString();
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
