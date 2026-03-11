using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using MPMS.Data;
using MPMS.Models;
using TaskStatus = MPMS.Models.TaskStatus;

namespace MPMS.Views.Dialogs;

public partial class CreateTaskDialog : Window
{
    private readonly IDbContextFactory<LocalDbContext> _dbFactory;
    private bool _isEditMode;
    private Guid? _fixedProjectId;

    public CreateTaskRequest? Result { get; private set; }
    public UpdateTaskRequest? UpdateResult { get; private set; }

    public CreateTaskDialog(IDbContextFactory<LocalDbContext> dbFactory)
    {
        InitializeComponent();
        _dbFactory = dbFactory;
        _ = LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var users = await db.Users.OrderBy(u => u.Name).ToListAsync();
        AssigneeCombo.ItemsSource = users;

        var projects = await db.Projects.OrderBy(p => p.Name).ToListAsync();
        ProjectCombo.ItemsSource = projects;

        if (projects.Count > 0) ProjectCombo.SelectedIndex = 0;
        if (users.Count > 0) AssigneeCombo.SelectedIndex = 0;

        if (_fixedProjectId.HasValue)
        {
            ProjectCombo.SelectedValue = _fixedProjectId.Value;
            ProjectCombo.IsEnabled = false;
        }
    }

    public void SetProjectFilter(Guid projectId)
    {
        _fixedProjectId = projectId;
        ProjectCombo.SelectedValue = projectId;
        ProjectCombo.IsEnabled = false;
    }

    public void SetEditMode(LocalTask task)
    {
        _isEditMode = true;
        TitleText.Text = "Редактировать задачу";
        SaveButton.Content = "Сохранить";
        StatusPanel.Visibility = Visibility.Visible;

        NameBox.Text = task.Name;
        DescriptionBox.Text = task.Description;
        _fixedProjectId = task.ProjectId;

        if (task.DueDate.HasValue)
            DueDatePicker.SelectedDate = task.DueDate.Value.ToDateTime(TimeOnly.MinValue);

        Loaded += (_, _) =>
        {
            ProjectCombo.SelectedValue = task.ProjectId;
            ProjectCombo.IsEnabled = false;

            if (task.AssignedUserId.HasValue)
                AssigneeCombo.SelectedValue = task.AssignedUserId.Value;

            SelectComboByTag(PriorityCombo, task.Priority.ToString());
            SelectComboByTag(StatusCombo, task.Status.ToString());
        };
    }

    private static void SelectComboByTag(ComboBox combo, string tag)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (item.Tag?.ToString() == tag)
            {
                item.IsSelected = true;
                return;
            }
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(NameBox.Text))
        {
            ShowError("Введите название задачи.");
            return;
        }

        if (ProjectCombo.SelectedItem is not LocalProject project)
        {
            ShowError("Выберите проект.");
            return;
        }

        var priorityTag = (PriorityCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Medium";
        if (!Enum.TryParse<TaskPriority>(priorityTag, out var priority))
            priority = TaskPriority.Medium;

        var assignedUser = AssigneeCombo.SelectedItem as LocalUser;
        DateOnly? dueDate = DueDatePicker.SelectedDate.HasValue
            ? DateOnly.FromDateTime(DueDatePicker.SelectedDate.Value) : null;

        if (_isEditMode)
        {
            var statusTag = (StatusCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Planned";
            Enum.TryParse<TaskStatus>(statusTag, out var status);

            UpdateResult = new UpdateTaskRequest(
                NameBox.Text.Trim(),
                string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim(),
                assignedUser?.Id,
                priority, dueDate, status);
        }
        else
        {
            Result = new CreateTaskRequest(
                project.Id,
                NameBox.Text.Trim(),
                string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim(),
                assignedUser?.Id,
                priority, dueDate);
        }

        DialogResult = true;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorText.Visibility = Visibility.Visible;
    }
}
