using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPMS.Data;
using MPMS.Models;
using MPMS.ViewModels;
using TaskStatus = MPMS.Models.TaskStatus;

namespace MPMS.Views.Overlays;

public partial class CreateTaskOverlay : UserControl
{
    private TasksViewModel? _tasksVm;
    private LocalTask? _editTask;
    private Guid? _fixedProjectId;
    private Func<System.Threading.Tasks.Task>? _onSaved;

    public CreateTaskOverlay()
    {
        InitializeComponent();
    }

    public void SetCreateMode(TasksViewModel vm, Guid? projectId = null,
        Func<System.Threading.Tasks.Task>? onSaved = null)
    {
        _tasksVm = vm;
        _fixedProjectId = projectId;
        _onSaved = onSaved;
        TitleLabel.Text = "Создать задачу";
        SaveButton.Content = "Создать задачу";
        _ = LoadDataAsync(null, null);
    }

    public void SetEditMode(LocalTask task, Func<System.Threading.Tasks.Task>? onSaved = null)
    {
        _editTask = task;
        _onSaved = onSaved;
        TitleLabel.Text = "Редактировать задачу";
        SaveButton.Content = "Сохранить изменения";
        StatusRow.Visibility = Visibility.Visible;

        NameBox.Text = task.Name;
        DescriptionBox.Text = task.Description ?? "";
        _ = LoadDataAsync(task.ProjectId, task.Id);
    }

    private async System.Threading.Tasks.Task LoadDataAsync(Guid? preselectedProjectId, Guid? editTaskId)
    {
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        var projects = await db.Projects.OrderBy(p => p.Name).ToListAsync();
        ProjectCombo.ItemsSource = projects;

        var users = await db.Users.OrderBy(u => u.Name).ToListAsync();
        AssigneeCombo.ItemsSource = users;

        // Preselect project
        if (_fixedProjectId.HasValue)
        {
            ProjectCombo.SelectedValue = _fixedProjectId.Value;
            ProjectCombo.IsEnabled = false;
        }
        else if (preselectedProjectId.HasValue)
        {
            ProjectCombo.SelectedValue = preselectedProjectId.Value;
        }

        if (editTaskId.HasValue && _editTask is not null)
        {
            // Set priority
            foreach (ComboBoxItem item in PriorityCombo.Items)
                if (item.Tag?.ToString() == _editTask.Priority.ToString())
                { PriorityCombo.SelectedItem = item; break; }

            // Set status
            foreach (ComboBoxItem item in StatusCombo.Items)
                if (item.Tag?.ToString() == _editTask.Status.ToString())
                { StatusCombo.SelectedItem = item; break; }

            // Set assignee
            if (_editTask.AssignedUserId.HasValue)
                AssigneeCombo.SelectedValue = _editTask.AssignedUserId.Value;

            // Set due date
            if (_editTask.DueDate.HasValue)
                DueDatePicker.SelectedDate = _editTask.DueDate.Value.ToDateTime(TimeOnly.MinValue);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => MainWindow.Instance?.HideDrawer();

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorPanel.Visibility = Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(NameBox.Text))
        { ShowError("Введите название задачи."); return; }

        if (ProjectCombo.SelectedValue is not Guid projectId)
        { ShowError("Выберите проект."); return; }

        if (DueDatePicker.SelectedDate is null)
        { ShowError("Выберите срок выполнения."); return; }

        var priority = GetPriority();
        var dueDate  = DateOnly.FromDateTime(DueDatePicker.SelectedDate.Value);
        var assignee = AssigneeCombo.SelectedValue as Guid?;

        SaveButton.IsEnabled = false;
        try
        {
            if (_editTask is null)
            {
                var tasksVm = _tasksVm ?? App.Services.GetRequiredService<TasksViewModel>();
                var req = new CreateTaskRequest(
                    projectId,
                    NameBox.Text.Trim(),
                    string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim(),
                    assignee, priority, dueDate);
                await tasksVm.SaveNewTaskAsync(req, Guid.NewGuid());
                if (_onSaved is not null) await _onSaved();
            }
            else
            {
                var status = GetStatus();
                var taskDetailVm = App.Services.GetRequiredService<MPMS.ViewModels.TaskDetailViewModel>();
                var req = new UpdateTaskRequest(
                    NameBox.Text.Trim(),
                    string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim(),
                    assignee, priority, dueDate, status);
                await taskDetailVm.EditTaskAsync(_editTask.Id, req);
                if (_onSaved is not null) await _onSaved();
            }
            MainWindow.Instance?.HideDrawer();
        }
        catch (Exception ex)
        {
            ShowError($"Ошибка: {ex.Message}");
        }
        finally
        {
            SaveButton.IsEnabled = true;
        }
    }

    private TaskPriority GetPriority()
    {
        if (PriorityCombo.SelectedItem is ComboBoxItem item)
        {
            return item.Tag?.ToString() switch
            {
                "Low"      => TaskPriority.Low,
                "High"     => TaskPriority.High,
                "Critical" => TaskPriority.Critical,
                _          => TaskPriority.Medium
            };
        }
        return TaskPriority.Medium;
    }

    private TaskStatus GetStatus()
    {
        if (StatusCombo.SelectedItem is ComboBoxItem item)
        {
            return item.Tag?.ToString() switch
            {
                "InProgress" => TaskStatus.InProgress,
                "Paused"     => TaskStatus.Paused,
                "Completed"  => TaskStatus.Completed,
                _            => TaskStatus.Planned
            };
        }
        return TaskStatus.Planned;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
    }
}
