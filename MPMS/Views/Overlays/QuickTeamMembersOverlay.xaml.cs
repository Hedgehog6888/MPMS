using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPMS.Data;
using MPMS.Models;

namespace MPMS.Views.Overlays;

public partial class QuickTeamMembersOverlay : UserControl
{
    private Guid _projectId;
    private Func<System.Threading.Tasks.Task>? _onSaved;

    private List<AssigneePickerItem> _foremanItems = [];
    private List<AssigneePickerItem> _workerItems = [];
    private List<LocalUser> _foremanUsers = [];
    private List<LocalUser> _workerUsers = [];
    private readonly HashSet<Guid> _selectedForemanIds = [];
    private readonly HashSet<Guid> _selectedWorkerIds = [];

    public QuickTeamMembersOverlay()
    {
        InitializeComponent();
    }

    public void SetProject(Guid projectId, Func<System.Threading.Tasks.Task>? onSaved = null)
    {
        _projectId = projectId;
        _onSaved = onSaved;
        _ = LoadDataAsync();
    }

    private async System.Threading.Tasks.Task LoadDataAsync()
    {
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        _foremanUsers = await db.Users
            .Where(u => !u.IsBlocked && (u.RoleName == "Foreman" || u.RoleName == "Прораб"))
            .OrderBy(u => u.Name)
            .ToListAsync();

        _workerUsers = await db.Users
            .Where(u => !u.IsBlocked && (u.RoleName == "Worker" || u.RoleName == "Работник"))
            .OrderBy(u => u.Name)
            .ToListAsync();

        var members = await db.ProjectMembers
            .Where(m => m.ProjectId == _projectId)
            .ToListAsync();

        _selectedForemanIds.Clear();
        _selectedWorkerIds.Clear();

        foreach (var fm in members.Where(m => m.UserRole is "Foreman" or "Прораб"))
        {
            if (_foremanUsers.Any(u => u.Id == fm.UserId))
                _selectedForemanIds.Add(fm.UserId);
        }

        foreach (var wm in members.Where(m => m.UserRole is "Worker" or "Работник"))
        {
            if (_workerUsers.Any(u => u.Id == wm.UserId))
                _selectedWorkerIds.Add(wm.UserId);
        }

        _foremanItems = _foremanUsers
            .Select(u => new AssigneePickerItem(u.Id, u.Name, "Foreman", _selectedForemanIds, u.AvatarPath, u.AvatarData))
            .ToList();

        _workerItems = _workerUsers
            .Select(u => new AssigneePickerItem(
                u.Id, u.Name, "Worker", _selectedWorkerIds, u.AvatarPath, u.AvatarData, u.SubRole, u.AdditionalSubRoles))
            .ToList();

        ForemanPickerList.ItemsSource = _foremanItems;
        WorkerPickerList.ItemsSource = _workerItems;
        NoForemanHint.Visibility = _foremanItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoWorkersHint.Visibility = _workerItems.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        RefreshForemanItemsAndChips();
        RefreshWorkerItemsAndChips();
    }

    private void ForemanItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border b || b.Tag is not AssigneePickerItem item) return;

        if (_selectedForemanIds.Contains(item.UserId))
        {
            if (_selectedForemanIds.Count <= 1)
            {
                ShowError("В проекте должен остаться хотя бы один прораб.");
                return;
            }
            _selectedForemanIds.Remove(item.UserId);
        }
        else
        {
            _selectedForemanIds.Add(item.UserId);
            ErrorPanel.Visibility = Visibility.Collapsed;
        }

        RefreshForemanItemsAndChips();
    }

    private void WorkerItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border b || b.Tag is not AssigneePickerItem item) return;

        if (_selectedWorkerIds.Contains(item.UserId))
        {
            if (_selectedWorkerIds.Count <= 1)
            {
                ShowError("В проекте должен остаться хотя бы один работник.");
                return;
            }
            _selectedWorkerIds.Remove(item.UserId);
        }
        else
        {
            _selectedWorkerIds.Add(item.UserId);
            ErrorPanel.Visibility = Visibility.Collapsed;
        }

        RefreshWorkerItemsAndChips();
    }

    private void RefreshForemanItemsAndChips()
    {
        foreach (var item in _foremanItems)
            item.RefreshSelected(_selectedForemanIds);
        ForemanPickerList.ItemsSource = null;
        ForemanPickerList.ItemsSource = _foremanItems;

        SelectedForemenPanel.Children.Clear();
        var selected = _foremanUsers.Where(u => _selectedForemanIds.Contains(u.Id)).ToList();
        SelectedForemenPanel.Visibility = selected.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var foreman in selected)
            SelectedForemenPanel.Children.Add(BuildChip(foreman.Id, foreman.Name, true));
    }

    private void RefreshWorkerItemsAndChips()
    {
        foreach (var item in _workerItems)
            item.RefreshSelected(_selectedWorkerIds);
        WorkerPickerList.ItemsSource = null;
        WorkerPickerList.ItemsSource = _workerItems;

        SelectedWorkersPanel.Children.Clear();
        var selected = _workerUsers.Where(u => _selectedWorkerIds.Contains(u.Id)).ToList();
        SelectedWorkersPanel.Visibility = selected.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        foreach (var worker in selected)
            SelectedWorkersPanel.Children.Add(BuildChip(worker.Id, worker.Name, false));
    }

    private Border BuildChip(Guid userId, string userName, bool isForeman)
    {
        var chip = new Border
        {
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 2, 6, 2),
            Background = isForeman
                ? new SolidColorBrush(Color.FromRgb(0xEC, 0xFD, 0xF5))
                : new SolidColorBrush(Color.FromRgb(0xEF, 0xF6, 0xFF)),
            BorderBrush = isForeman
                ? new SolidColorBrush(Color.FromRgb(0x6E, 0xE7, 0xB7))
                : new SolidColorBrush(Color.FromRgb(0xBF, 0xDB, 0xFE)),
            BorderThickness = new Thickness(1)
        };

        var stack = new StackPanel { Orientation = Orientation.Horizontal };
        stack.Children.Add(new TextBlock
        {
            Text = userName,
            FontSize = 11,
            Foreground = isForeman
                ? new SolidColorBrush(Color.FromRgb(0x16, 0x65, 0x34))
                : new SolidColorBrush(Color.FromRgb(0x1D, 0x4E, 0xD8)),
            VerticalAlignment = VerticalAlignment.Center
        });

        var removeButton = new Button
        {
            Style = (Style)Application.Current.FindResource("ChipRemoveButton"),
            Content = new TextBlock { Text = "✕", FontSize = 9, Foreground = Brushes.Gray },
            Margin = new Thickness(4, 0, 0, 0),
            Tag = (isForeman, userId)
        };

        removeButton.Click += (s, _) =>
        {
            if (s is not Button btn || btn.Tag is not ValueTuple<bool, Guid> tuple)
                return;

            if (tuple.Item1)
            {
                if (_selectedForemanIds.Count <= 1)
                {
                    ShowError("В проекте должен остаться хотя бы один прораб.");
                    return;
                }
                _selectedForemanIds.Remove(tuple.Item2);
                RefreshForemanItemsAndChips();
            }
            else
            {
                if (_selectedWorkerIds.Count <= 1)
                {
                    ShowError("В проекте должен остаться хотя бы один работник.");
                    return;
                }
                _selectedWorkerIds.Remove(tuple.Item2);
                RefreshWorkerItemsAndChips();
            }
        };

        stack.Children.Add(removeButton);
        chip.Child = stack;
        return chip;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorPanel.Visibility = Visibility.Collapsed;

        if (_selectedForemanIds.Count == 0)
        {
            ShowError("Добавьте хотя бы одного прораба.");
            return;
        }
        if (_selectedWorkerIds.Count == 0)
        {
            ShowError("Добавьте хотя бы одного работника.");
            return;
        }

        SaveButton.IsEnabled = false;
        try
        {
            await SaveProjectMembersAsync();
            if (_onSaved is not null)
                await _onSaved();
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

    private async System.Threading.Tasks.Task SaveProjectMembersAsync()
    {
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        var newMemberIds = new HashSet<Guid>();
        foreach (var foremanId in _selectedForemanIds) newMemberIds.Add(foremanId);
        foreach (var workerId in _selectedWorkerIds) newMemberIds.Add(workerId);

        var existing = await db.ProjectMembers
            .Where(m => m.ProjectId == _projectId)
            .ToListAsync();
        var removedIds = existing
            .Where(m => !newMemberIds.Contains(m.UserId))
            .Select(m => m.UserId)
            .Distinct()
            .ToList();

        db.ProjectMembers.RemoveRange(existing);

        foreach (var foremanId in _selectedForemanIds)
        {
            var foreman = _foremanUsers.FirstOrDefault(u => u.Id == foremanId);
            if (foreman is null) continue;
            db.ProjectMembers.Add(new LocalProjectMember
            {
                Id = Guid.NewGuid(),
                ProjectId = _projectId,
                UserId = foremanId,
                UserName = foreman.Name,
                UserRole = "Foreman"
            });
        }

        foreach (var workerId in _selectedWorkerIds)
        {
            var worker = _workerUsers.FirstOrDefault(u => u.Id == workerId);
            if (worker is null) continue;
            db.ProjectMembers.Add(new LocalProjectMember
            {
                Id = Guid.NewGuid(),
                ProjectId = _projectId,
                UserId = workerId,
                UserName = worker.Name,
                UserRole = "Worker"
            });
        }

        if (removedIds.Count > 0)
        {
            var taskIds = await db.Tasks.Where(t => t.ProjectId == _projectId).Select(t => t.Id).ToListAsync();
            var toRemoveTaskAssignees = await db.TaskAssignees
                .Where(ta => taskIds.Contains(ta.TaskId) && removedIds.Contains(ta.UserId))
                .ToListAsync();
            db.TaskAssignees.RemoveRange(toRemoveTaskAssignees);

            var stageIds = await db.TaskStages.Where(s => taskIds.Contains(s.TaskId)).Select(s => s.Id).ToListAsync();
            var toRemoveStageAssignees = await db.StageAssignees
                .Where(sa => stageIds.Contains(sa.StageId) && removedIds.Contains(sa.UserId))
                .ToListAsync();
            db.StageAssignees.RemoveRange(toRemoveStageAssignees);
        }

        await db.SaveChangesAsync();
    }

    private void NestedScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer nested)
            return;

        var atTop = nested.VerticalOffset <= 0;
        var atBottom = nested.VerticalOffset >= nested.ScrollableHeight;
        var scrollingUp = e.Delta > 0;
        var scrollingDown = e.Delta < 0;

        if ((atTop && scrollingUp) || (atBottom && scrollingDown))
        {
            MainScrollViewer.ScrollToVerticalOffset(MainScrollViewer.VerticalOffset - e.Delta);
            e.Handled = true;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => MainWindow.Instance?.HideDrawer();

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
    }
}
