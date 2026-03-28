using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
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
    private Action? _onAfterSave;

    private List<AssigneePickerItem> _allAssigneeItems = [];
    private List<AssigneePickerItem> _foremanItems = [];
    private List<AssigneePickerItem> _workerItems = [];
    private readonly HashSet<Guid> _selectedAssigneeIds = [];
    private TaskPriority _selectedPriority = TaskPriority.Medium;

    public CreateTaskOverlay()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyPrioritySelection(_selectedPriority);
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

    public void SetEditMode(LocalTask task, Func<System.Threading.Tasks.Task>? onSaved = null, Action? onAfterSave = null)
    {
        _editTask = task;
        _onSaved = onSaved;
        _onAfterSave = onAfterSave;
        TitleLabel.Text = "Редактировать задачу";
        SaveButton.Content = "Сохранить изменения";
        StatusRow.Visibility = Visibility.Collapsed; // Status is auto from stages

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

        // Preselect project
        if (_fixedProjectId.HasValue)
        {
            ProjectCombo.SelectedValue = _fixedProjectId.Value;
            ProjectCombo.IsEnabled = false;
            await LoadAssigneesForProjectAsync(_fixedProjectId.Value);
        }
        else if (preselectedProjectId.HasValue)
        {
            ProjectCombo.SelectedValue = preselectedProjectId.Value;
            await LoadAssigneesForProjectAsync(preselectedProjectId.Value);
        }
        if (editTaskId.HasValue)
            ProjectCombo.IsEnabled = false; // при редактировании задачи проект менять нельзя

        if (editTaskId.HasValue && _editTask is not null)
        {
            _selectedPriority = _editTask.Priority;
            ApplyPrioritySelection(_selectedPriority);

            foreach (ComboBoxItem item in StatusCombo.Items)
                if (item.Tag?.ToString() == _editTask.Status.ToString())
                { StatusCombo.SelectedItem = item; break; }

            if (_editTask.DueDate.HasValue)
                DueDatePicker.SelectedDate = _editTask.DueDate.Value.ToDateTime(TimeOnly.MinValue);

            // Load existing task assignees (exclude blocked users)
            var blockedIds = await db.Users.Where(u => u.IsBlocked).Select(u => u.Id).ToListAsync();
            var taskAssignees = await db.TaskAssignees
                .Where(ta => ta.TaskId == _editTask.Id && !blockedIds.Contains(ta.UserId))
                .ToListAsync();
            foreach (var ta in taskAssignees)
                _selectedAssigneeIds.Add(ta.UserId);

            // Also include legacy single assignee (if not blocked)
            if (_editTask.AssignedUserId.HasValue && !blockedIds.Contains(_editTask.AssignedUserId.Value)
                && !_selectedAssigneeIds.Contains(_editTask.AssignedUserId.Value))
                _selectedAssigneeIds.Add(_editTask.AssignedUserId.Value);

            RefreshAssigneeItems();
            RefreshAssigneeChips();
        }
    }

    private async System.Threading.Tasks.Task LoadAssigneesForProjectAsync(Guid projectId)
    {
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        // Only project members can be assigned to tasks (foremen + workers), exclude blocked users
        var blockedUserIds = await db.Users.Where(u => u.IsBlocked).Select(u => u.Id).ToListAsync();
        var members = await db.ProjectMembers
            .Where(m => m.ProjectId == projectId && !blockedUserIds.Contains(m.UserId))
            .OrderBy(m => m.UserName)
            .ToListAsync();

        var userIds = members.Select(m => m.UserId).Distinct().ToList();
        var userAvatars = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .Select(u => new { u.Id, u.AvatarPath, u.AvatarData })
            .ToDictionaryAsync(u => u.Id);
        foreach (var m in members)
        {
            if (userAvatars.TryGetValue(m.UserId, out var av))
            {
                m.AvatarPath = av.AvatarPath;
                m.AvatarData = av.AvatarData;
            }
        }

        _foremanItems = members
            .Where(m => m.UserRole is "Foreman" or "Прораб")
            .Select(m => new AssigneePickerItem(
                m.UserId, m.UserName, m.UserRole, _selectedAssigneeIds, m.AvatarPath, m.AvatarData))
            .ToList();
        _workerItems = members
            .Where(m => m.UserRole is "Worker" or "Работник")
            .Select(m => new AssigneePickerItem(
                m.UserId, m.UserName, m.UserRole, _selectedAssigneeIds, m.AvatarPath, m.AvatarData))
            .ToList();
        _allAssigneeItems = [.. _foremanItems, .. _workerItems];

        await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (_allAssigneeItems.Count == 0)
            {
                NoProjHint.Visibility = Visibility.Visible;
                NoProjHintText.Text = "В проекте нет назначенных прорабов или работников. Добавьте команду в проект.";
                ForemanPickerBorder.Visibility = Visibility.Collapsed;
                WorkerPickerBorder.Visibility = Visibility.Collapsed;
            }
            else
            {
                NoProjHint.Visibility = Visibility.Collapsed;
                ForemanPickerBorder.Visibility = _foremanItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                WorkerPickerBorder.Visibility = _workerItems.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            }
            RefreshAssigneeItems();
        });
    }

    private void RefreshAssigneeItems()
    {
        foreach (var item in _foremanItems)
            item.RefreshSelected(_selectedAssigneeIds);
        foreach (var item in _workerItems)
            item.RefreshSelected(_selectedAssigneeIds);

        ForemanPickerList.ItemsSource = null;
        ForemanPickerList.ItemsSource = _foremanItems;
        WorkerPickerList.ItemsSource = null;
        WorkerPickerList.ItemsSource = _workerItems;

        NoForemenHint.Visibility = _foremanItems.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;
        NoWorkersHint.Visibility = _workerItems.Count == 0
            ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshAssigneeChips()
    {
        SelectedAssigneesPanel.Children.Clear();
        var selected = _allAssigneeItems.Where(i => _selectedAssigneeIds.Contains(i.UserId)).ToList();
        SelectedAssigneesPanel.Visibility = selected.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var item in selected)
        {
            var chip = BuildChip(item);
            SelectedAssigneesPanel.Children.Add(chip);
        }
    }

    private Border BuildChip(AssigneePickerItem item)
    {
        var chip = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 2, 6, 2),
            Background = new SolidColorBrush(Color.FromRgb(0xEF, 0xF6, 0xFF)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xBF, 0xDB, 0xFE)),
            BorderThickness = new Thickness(1)
        };
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        sp.Children.Add(new TextBlock
        {
            Text = item.Name, FontSize = 11,
            Foreground = new SolidColorBrush(Color.FromRgb(0x1D, 0x4E, 0xD8)),
            VerticalAlignment = VerticalAlignment.Center
        });
        sp.Children.Add(new TextBlock
        {
            Text = $"  {item.RoleDisplay}",
            FontSize = 11,
            Foreground = item.RoleColorBrush,
            VerticalAlignment = VerticalAlignment.Center
        });
        var removeBtn = new Button
        {
            Style = (Style)Application.Current.FindResource("ChipRemoveButton"),
            Content = new TextBlock { Text = "✕", FontSize = 9, Foreground = Brushes.Gray },
            Margin = new Thickness(4, 0, 0, 0),
            Tag = item.UserId
        };
        removeBtn.Click += (s, _) =>
        {
            if (s is Button b && b.Tag is Guid uid)
            {
                _selectedAssigneeIds.Remove(uid);
                RefreshAssigneeItems();
                RefreshAssigneeChips();
            }
        };
        sp.Children.Add(removeBtn);
        chip.Child = sp;
        return chip;
    }

    private void ForemanItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border b || b.Tag is not AssigneePickerItem item) return;
        if (_selectedAssigneeIds.Contains(item.UserId))
            _selectedAssigneeIds.Remove(item.UserId);
        else
            _selectedAssigneeIds.Add(item.UserId);
        RefreshAssigneeItems();
        RefreshAssigneeChips();
    }

    private void WorkerItem_Click(object sender, MouseButtonEventArgs e)
        => ForemanItem_Click(sender, e);

    private async void ProjectCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProjectCombo.SelectedValue is Guid projectId)
        {
            _selectedAssigneeIds.Clear();
            SelectedAssigneesPanel.Visibility = Visibility.Collapsed;
            await LoadAssigneesForProjectAsync(projectId);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        if (_onAfterSave is not null)
            _onAfterSave();
        else
            MainWindow.Instance?.HideDrawer();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorPanel.Visibility = Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(NameBox.Text))
        { ShowError("Введите название задачи."); return; }
        if (ProjectCombo.SelectedValue is not Guid projectId)
        { ShowError("Выберите проект."); return; }
        if (DueDatePicker.SelectedDate is null)
        { ShowError("Выберите срок выполнения."); return; }

        if (_selectedAssigneeIds.Count == 0)
        { ShowError("Назначьте хотя бы одного исполнителя на задачу."); return; }

        var priority = GetPriority();
        var dueDate  = DateOnly.FromDateTime(DueDatePicker.SelectedDate.Value);
        var primaryAssigneeId = _selectedAssigneeIds.Count > 0
            ? _selectedAssigneeIds.First()
            : (Guid?)null;

        SaveButton.IsEnabled = false;
        try
        {
            Guid taskId;
            var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();

            if (_editTask is null)
            {
                var tasksVm = _tasksVm ?? App.Services.GetRequiredService<TasksViewModel>();
                var localId = Guid.NewGuid();
                var req = new CreateTaskRequest(
                    projectId,
                    NameBox.Text.Trim(),
                    string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim(),
                    primaryAssigneeId, priority, dueDate);
                await tasksVm.SaveNewTaskAsync(req, localId);
                if (_onSaved is not null) await _onSaved();
                taskId = localId;
            }
            else
            {
                var status = GetStatus();
                var taskDetailVm = App.Services.GetRequiredService<MPMS.ViewModels.TaskDetailViewModel>();
                var req = new UpdateTaskRequest(
                    NameBox.Text.Trim(),
                    string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim(),
                    primaryAssigneeId, priority, dueDate, status);
                await taskDetailVm.EditTaskAsync(_editTask.Id, req);
                if (_onSaved is not null) await _onSaved();
                taskId = _editTask.Id;
            }

            // Save multi-assignees
            await SaveTaskAssigneesAsync(taskId, dbFactory);

            if (_onAfterSave is not null)
                _onAfterSave();
            else
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

    private async System.Threading.Tasks.Task SaveTaskAssigneesAsync(
        Guid taskId, IDbContextFactory<LocalDbContext> dbFactory)
    {
        await using var db = await dbFactory.CreateDbContextAsync();

        // Remove all existing
        var existing = await db.TaskAssignees.Where(a => a.TaskId == taskId).ToListAsync();
        db.TaskAssignees.RemoveRange(existing);

        // Add selected
        foreach (var uid in _selectedAssigneeIds)
        {
            var item = _allAssigneeItems.FirstOrDefault(i => i.UserId == uid);
            if (item is null) continue;
            db.TaskAssignees.Add(new LocalTaskAssignee
            {
                Id = Guid.NewGuid(),
                TaskId = taskId,
                UserId = uid,
                UserName = item.Name
            });
        }
        await db.SaveChangesAsync();
    }

    private void PriorityLow_Click(object sender, RoutedEventArgs e)
    {
        _selectedPriority = TaskPriority.Low;
        ApplyPrioritySelection(_selectedPriority);
    }

    private void PriorityMedium_Click(object sender, RoutedEventArgs e)
    {
        _selectedPriority = TaskPriority.Medium;
        ApplyPrioritySelection(_selectedPriority);
    }

    private void PriorityHigh_Click(object sender, RoutedEventArgs e)
    {
        _selectedPriority = TaskPriority.High;
        ApplyPrioritySelection(_selectedPriority);
    }

    private void PriorityCritical_Click(object sender, RoutedEventArgs e)
    {
        _selectedPriority = TaskPriority.Critical;
        ApplyPrioritySelection(_selectedPriority);
    }

    private void ApplyPrioritySelection(TaskPriority priority)
    {
        var neutral = new SolidColorBrush(Color.FromRgb(0xDF, 0xE1, 0xE6));
        var neutralBg = new SolidColorBrush(Colors.White);
        var neutralFg = new SolidColorBrush(Color.FromRgb(0x6B, 0x77, 0x8C));

        BtnPriorityLow.BorderBrush = priority == TaskPriority.Low
            ? new SolidColorBrush(Color.FromRgb(0x00, 0x87, 0x5A)) : neutral;
        BtnPriorityLow.Background = priority == TaskPriority.Low
            ? new SolidColorBrush(Color.FromRgb(0xE8, 0xF5, 0xE9)) : neutralBg;
        BtnPriorityLow.Foreground = priority == TaskPriority.Low
            ? new SolidColorBrush(Color.FromRgb(0x00, 0x87, 0x5A)) : neutralFg;
        BtnPriorityLow.FontWeight = priority == TaskPriority.Low ? FontWeights.SemiBold : FontWeights.Normal;

        BtnPriorityMedium.BorderBrush = priority == TaskPriority.Medium
            ? new SolidColorBrush(Color.FromRgb(0x00, 0x82, 0xFF)) : neutral;
        BtnPriorityMedium.Background = priority == TaskPriority.Medium
            ? new SolidColorBrush(Color.FromRgb(0xEB, 0xF2, 0xFF)) : neutralBg;
        BtnPriorityMedium.Foreground = priority == TaskPriority.Medium
            ? new SolidColorBrush(Color.FromRgb(0x1B, 0x6E, 0xC2)) : neutralFg;
        BtnPriorityMedium.FontWeight = priority == TaskPriority.Medium ? FontWeights.SemiBold : FontWeights.Normal;

        BtnPriorityHigh.BorderBrush = priority == TaskPriority.High
            ? new SolidColorBrush(Color.FromRgb(0xFF, 0x8B, 0x00)) : neutral;
        BtnPriorityHigh.Background = priority == TaskPriority.High
            ? new SolidColorBrush(Color.FromRgb(0xFF, 0xF4, 0xE6)) : neutralBg;
        BtnPriorityHigh.Foreground = priority == TaskPriority.High
            ? new SolidColorBrush(Color.FromRgb(0xE6, 0x51, 0x00)) : neutralFg;
        BtnPriorityHigh.FontWeight = priority == TaskPriority.High ? FontWeights.SemiBold : FontWeights.Normal;

        BtnPriorityCritical.BorderBrush = priority == TaskPriority.Critical
            ? new SolidColorBrush(Color.FromRgb(0xDE, 0x35, 0x0B)) : neutral;
        BtnPriorityCritical.Background = priority == TaskPriority.Critical
            ? new SolidColorBrush(Color.FromRgb(0xFF, 0xEB, 0xE6)) : neutralBg;
        BtnPriorityCritical.Foreground = priority == TaskPriority.Critical
            ? new SolidColorBrush(Color.FromRgb(0xDE, 0x35, 0x0B)) : neutralFg;
        BtnPriorityCritical.FontWeight = priority == TaskPriority.Critical ? FontWeights.SemiBold : FontWeights.Normal;
    }

    private TaskPriority GetPriority() => _selectedPriority;

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

/// <summary>Item in the assignee picker list with reactive selection state.</summary>
public sealed class AssigneePickerItem : INotifyPropertyChanged
{
    public Guid UserId { get; }
    public string Name { get; }
    public string? AvatarPath { get; }
    public byte[]? AvatarData { get; }
    public string RoleDisplay { get; }
    public string Initials { get; }
    public SolidColorBrush AvatarBrush { get; }
    public SolidColorBrush RoleColorBrush { get; }
    public string RoleColor => RoleColorBrush.Color.ToString();

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelectedVis)));
        }
    }
    public Visibility IsSelectedVis => _isSelected ? Visibility.Visible : Visibility.Collapsed;

    public event PropertyChangedEventHandler? PropertyChanged;

    public AssigneePickerItem(Guid userId, string name, string role, HashSet<Guid> selectedIds, string? avatarPath = null, byte[]? avatarData = null)
    {
        UserId = userId;
        Name = name;
        AvatarPath = avatarPath;
        AvatarData = avatarData;
        _isSelected = selectedIds.Contains(userId);

        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        Initials = parts.Length >= 2
            ? $"{parts[0][0]}{parts[1][0]}".ToUpper()
            : name.Length > 0 ? name[0].ToString().ToUpper() : "?";

        (RoleDisplay, AvatarBrush, RoleColorBrush) = role switch
        {
            "Foreman" or "Прораб" => (
                "Прораб",
                new SolidColorBrush(Color.FromRgb(0x16, 0x65, 0x34)),
                new SolidColorBrush(Color.FromRgb(0x16, 0x65, 0x34))),
            "Worker" or "Работник" => (
                "Работник",
                new SolidColorBrush(Color.FromRgb(0x92, 0x40, 0x0E)),
                new SolidColorBrush(Color.FromRgb(0x92, 0x40, 0x0E))),
            _ => (
                role,
                new SolidColorBrush(Color.FromRgb(0x17, 0x2B, 0x4D)),
                new SolidColorBrush(Color.FromRgb(0x6B, 0x77, 0x8C)))
        };
    }

    public void RefreshSelected(HashSet<Guid> selectedIds)
    {
        IsSelected = selectedIds.Contains(UserId);
    }
}
