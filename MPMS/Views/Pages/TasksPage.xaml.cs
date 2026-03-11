using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;
using MPMS.Models;
using MPMS.ViewModels;
using MPMS.Views.Dialogs;

namespace MPMS.Views.Pages;

public partial class TasksPage : UserControl
{
    public TasksPage()
    {
        InitializeComponent();
    }

    private TasksViewModel? VM => DataContext as TasksViewModel;

    private void ViewMode_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || rb.Tag is not string mode) return;
        if (VM is not null) VM.ViewMode = mode;

        ListPanel.Visibility = mode == "List" ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
        KanbanPanel.Visibility = mode == "Kanban" ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }

    private void Project_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (VM is null) return;
        if (sender is ComboBox cb)
            VM.SetProjectFilter(cb.SelectedItem as LocalProject);
    }

    private async void CreateTask_Click(object sender, RoutedEventArgs e)
    {
        var dialog = App.Services.GetRequiredService<CreateTaskDialog>();
        dialog.Owner = Window.GetWindow(this);

        if (dialog.ShowDialog() == true && dialog.Result is not null && VM is not null)
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
