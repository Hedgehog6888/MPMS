using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPMS.Data;
using MPMS.Models;
using MPMS.ViewModels;

namespace MPMS.Views.Overlays;

public partial class CreateStageOverlay : UserControl
{
    private TaskDetailViewModel? _vm;
    private LocalTask? _task;
    private LocalTaskStage? _editStage;
    private Func<System.Threading.Tasks.Task>? _onSaved;

    public CreateStageOverlay()
    {
        InitializeComponent();
    }

    public void SetTask(LocalTask task, Func<System.Threading.Tasks.Task>? onSaved = null)
    {
        _task = task;
        _vm = App.Services.GetRequiredService<TaskDetailViewModel>();
        _vm.SetTask(task);
        _onSaved = onSaved;
        TaskNameLabel.Text = $"Задача: {task.Name}";
        ProjectTaskPickerRow.Visibility = Visibility.Collapsed;
        _ = LoadUsersAsync(taskId: task.Id);
    }

    public void SetCreateModeFromStagesPage(Func<System.Threading.Tasks.Task>? onSaved = null)
    {
        _task = null;
        _vm = null;
        _editStage = null;
        _onSaved = onSaved;
        TitleLabel.Text = "Добавить этап";
        SaveButton.Content = "Добавить этап";
        TaskNameLabel.Text = "Выберите проект и задачу";
        ProjectTaskPickerRow.Visibility = Visibility.Visible;
        StatusRow.Visibility = Visibility.Collapsed;
        _ = LoadProjectsAndUsersAsync();
    }

    public void SetEditMode(LocalTaskStage stage, LocalTask task, Func<System.Threading.Tasks.Task>? onSaved = null)
    {
        _editStage = stage;
        _task = task;
        _vm = App.Services.GetRequiredService<TaskDetailViewModel>();
        _vm.SetTask(task);
        _onSaved = onSaved;
        TitleLabel.Text = "Редактировать этап";
        SaveButton.Content = "Сохранить";
        StatusRow.Visibility = Visibility.Visible;
        TaskNameLabel.Text = $"Задача: {task.Name}";

        NameBox.Text = stage.Name;
        DescriptionBox.Text = stage.Description ?? "";

        foreach (ComboBoxItem item in StatusCombo.Items)
            if (item.Tag?.ToString() == stage.Status.ToString())
            { StatusCombo.SelectedItem = item; break; }

        _ = LoadUsersAsync(stage.AssignedUserId);
    }

    private async System.Threading.Tasks.Task LoadUsersAsync(Guid? selectedId = null, Guid? taskId = null)
    {
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var workerRoles = new[] { "Foreman", "Прораб", "Worker", "Работник" };
        var users = await db.Users
            .Where(u => workerRoles.Contains(u.RoleName))
            .OrderBy(u => u.Name)
            .ToListAsync();
        AssigneeCombo.ItemsSource = users;
        if (selectedId.HasValue)
            AssigneeCombo.SelectedValue = selectedId.Value;
        else if (users.Count > 0)
            AssigneeCombo.SelectedIndex = 0;
    }

    private async System.Threading.Tasks.Task LoadProjectsAndUsersAsync()
    {
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var projects = await db.Projects.OrderBy(p => p.Name).ToListAsync();
        ProjectCombo.ItemsSource = projects;
        TaskCombo.ItemsSource = null;
        if (projects.Count > 0)
        {
            ProjectCombo.SelectedIndex = 0;
            await LoadTasksForProjectAsync((Guid)ProjectCombo.SelectedValue!);
        }
        await LoadUsersAsync();
    }

    private async void ProjectCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProjectCombo.SelectedValue is Guid projectId)
            await LoadTasksForProjectAsync(projectId);
    }

    private async System.Threading.Tasks.Task LoadTasksForProjectAsync(Guid projectId)
    {
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var tasks = await db.Tasks.Where(t => t.ProjectId == projectId).OrderBy(t => t.Name).ToListAsync();
        TaskCombo.ItemsSource = tasks;
        if (tasks.Count > 0)
            TaskCombo.SelectedIndex = 0;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => MainWindow.Instance?.HideDrawer();

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorPanel.Visibility = Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(NameBox.Text))
        { ShowError("Введите название этапа."); return; }

        Guid taskId;
        TaskDetailViewModel? vm;
        if (ProjectTaskPickerRow.Visibility == Visibility.Visible)
        {
            if (TaskCombo.SelectedValue is not Guid tid)
            { ShowError("Выберите задачу."); return; }
            taskId = tid;
            vm = App.Services.GetRequiredService<TaskDetailViewModel>();
            var task = await GetTaskByIdAsync(taskId);
            if (task is null) { ShowError("Задача не найдена."); return; }
            vm.SetTask(task);
        }
        else
        {
            if (_task is null || _vm is null)
            { ShowError("Ошибка: задача не установлена."); return; }
            taskId = _task.Id;
            vm = _vm;
        }

        var assignee = AssigneeCombo.SelectedValue as Guid?;

        SaveButton.IsEnabled = false;
        try
        {
            if (_editStage is null)
            {
                var req = new CreateStageRequest(
                    taskId,
                    NameBox.Text.Trim(),
                    string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim(),
                    assignee);
                await vm.SaveNewStageAsync(req, Guid.NewGuid());
            }
            else
            {
                var status = GetStatus();
                var req = new UpdateStageRequest(
                    NameBox.Text.Trim(),
                    string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim(),
                    assignee, status);
                await vm.SaveUpdatedStageAsync(_editStage.Id, req);
            }

            if (_onSaved is not null) await _onSaved();
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

    private StageStatus GetStatus()
    {
        if (StatusCombo.SelectedItem is ComboBoxItem item)
        {
            return item.Tag?.ToString() switch
            {
                "InProgress" => StageStatus.InProgress,
                "Completed"  => StageStatus.Completed,
                _            => StageStatus.Planned
            };
        }
        return StageStatus.Planned;
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
    }

    private async System.Threading.Tasks.Task<LocalTask?> GetTaskByIdAsync(Guid taskId)
    {
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Tasks.FindAsync(taskId);
    }
}
