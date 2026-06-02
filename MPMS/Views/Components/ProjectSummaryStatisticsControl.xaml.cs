using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPMS.Data;
using MPMS.Models;
using MPMS.ViewModels;

namespace MPMS.Views.Components;

public partial class ProjectSummaryStatisticsControl
{
    public ProjectSummaryStatisticsControl()
    {
        InitializeComponent();
    }

    private async void OpenStageDetail_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.DataContext is not ProjectSummaryStageRowVm row) return;

        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        var stage = await db.TaskStages.FindAsync(row.StageId);
        var task = await db.Tasks.FindAsync(row.TaskId);

        if (stage is null || task is null) return;

        var project = await db.Projects.FindAsync(task.ProjectId);
        if (project is null) return;

        var main = App.Services.GetRequiredService<MainViewModel>();
        var stageEditor = App.Services.GetRequiredService<StageDetailViewModel>();

        stageEditor.SetEditMode(stage, task,
            goBack: () => main.NavigateToProject(project),
            onSavedAsync: null);

        stageEditor.ActiveTab = "Summary";

        main.NavigateToStageEditor(stageEditor);
    }
}
