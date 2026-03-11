using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using MPMS.Models;
using MPMS.ViewModels;
using MPMS.Views.Dialogs;

namespace MPMS.Views.Pages;

public partial class ProjectDetailPage : UserControl
{
    public ProjectDetailPage()
    {
        InitializeComponent();
    }

    private ProjectDetailViewModel? VM => DataContext as ProjectDetailViewModel;

    private void Back_Click(object sender, MouseButtonEventArgs e)
    {
        VM?.GoBackCommand.Execute(null);
    }

    private void Tab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || rb.Tag is not string tab) return;
        VM?.SwitchTabCommand.Execute(tab);

        InfoPanel.Visibility = tab == "Info" ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        TasksPanel.Visibility = tab == "Tasks" ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        CreateTaskBtn.Visibility = tab == "Tasks" ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }

    private void TaskView_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || rb.Tag is not string mode) return;
        VM?.SwitchTaskViewCommand.Execute(mode);

        TaskListPanel.Visibility = mode == "List" ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        TaskKanbanPanel.Visibility = mode == "Kanban" ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }

    private async void CreateTask_Click(object sender, RoutedEventArgs e)
    {
        if (VM?.Project is null) return;

        var dialog = App.Services.GetRequiredService<CreateTaskDialog>();
        dialog.Owner = Window.GetWindow(this);
        dialog.SetProjectFilter(VM.Project.Id);

        if (dialog.ShowDialog() == true && dialog.Result is not null)
        {
            var id = Guid.NewGuid();
            await VM.SaveNewTaskAsync(dialog.Result, id);
        }
    }

    private async void EditTask_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not LocalTask task || VM is null)
            return;

        var dialog = App.Services.GetRequiredService<CreateTaskDialog>();
        dialog.Owner = Window.GetWindow(this);
        dialog.SetEditMode(task);

        if (dialog.ShowDialog() == true && dialog.UpdateResult is not null)
            await VM.SaveUpdatedTaskAsync(task.Id, dialog.UpdateResult);
    }

    private async void DeleteTask_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not LocalTask task || VM is null)
            return;

        var result = MessageBox.Show($"Удалить задачу «{task.Name}»?",
            "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
            await VM.DeleteTaskCommand.ExecuteAsync(task);
    }

    private void Task_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not LocalTask task) return;
        OpenTaskDetail(task);
    }

    private void OpenTaskDetail(LocalTask task)
    {
        var window = App.Services.GetRequiredService<TaskDetailWindow>();
        window.Owner = Window.GetWindow(this);
        window.SetTask(task);
        window.ShowDialog();
        _ = VM?.LoadAsync();
    }
}
