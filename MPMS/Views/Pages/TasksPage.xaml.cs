using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Extensions.DependencyInjection;
using MPMS;
using MPMS.Infrastructure;
using MPMS.Models;
using MPMS.Services;
using MPMS.ViewModels;
using MPMS.Views.Overlays;

namespace MPMS.Views.Pages;

public partial class TasksPage : UserControl
{
    public TasksPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        var auth = App.Services.GetRequiredService<IAuthService>();
        string role = auth.UserRole ?? "";
        bool canCreate = role is "Administrator" or "Project Manager" or "Foreman";
        CreateTaskBtn.Visibility = canCreate ? Visibility.Visible : Visibility.Collapsed;
    }

    private TasksViewModel? VM => DataContext as TasksViewModel;

    private void CreateTask_Click(object sender, RoutedEventArgs e)
    {
        if (VM is null) return;
        var overlay = new CreateTaskOverlay();
        overlay.SetCreateMode(VM);
        MainWindow.Instance?.ShowCenteredOverlay(overlay, MainWindow.CenteredFormOverlayWidth);
    }

    private void EditTask_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not LocalTask task || VM is null) return;
        var overlay = new CreateTaskOverlay();
        overlay.SetEditMode(task, async () => await VM.LoadAsync());
        MainWindow.Instance?.ShowCenteredOverlay(overlay, MainWindow.CenteredFormOverlayWidth);
    }

    private async void DeleteTask_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not LocalTask task || VM is null) return;
        var owner = Window.GetWindow(this);
        if (MPMS.Views.ConfirmDeleteDialog.Show(owner, "Задача", task.Name))
            await VM.DeleteTaskCommand.ExecuteAsync(task);
    }

    private async void MarkTask_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not LocalTask task || VM is null) return;
        if (!task.IsMarkedForDeletion)
        {
            var owner = Window.GetWindow(this) ?? Application.Current.MainWindow;
            if (owner is null || !MPMS.Views.ConfirmDeleteDialog.ShowMarkForDeletion(owner, "задачу", task.Name))
                return;
        }
        await VM.MarkTaskForDeletionCommand.ExecuteAsync(task);
    }

    private void Task_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not LocalTask task) return;
        OpenTaskDetail(task);
    }

    private async void OpenTaskDetail(LocalTask task)
    {
        var overlay = new TaskDetailOverlay();
        var vm = VM;
        UIElement? leftPanel = null;
        ProjectSummaryPanel? projectPanel = null;
        if (vm != null)
        {
            var project = await vm.GetProjectForTaskAsync(task.ProjectId);
            if (project != null)
            {
                projectPanel = new ProjectSummaryPanel();
                projectPanel.SetProject(project);
                leftPanel = projectPanel;
            }
        }

        overlay.SetTask(task, () =>
        {
            if (vm != null)
            {
                _ = Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    await vm.LoadAsync();
                    var project = await vm.GetProjectForTaskAsync(task.ProjectId);
                    if (project != null && projectPanel != null)
                        projectPanel.SetProject(project);
                });
            }
        });

        MainWindow.Instance?.ShowDrawer(leftPanel, overlay, MainWindow.TaskOrStageDetailWithLeftTotalWidth);
    }
}

