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
using MPMS.Services;
using MPMS.ViewModels;

namespace MPMS.Views.Overlays;

public partial class CreateProjectOverlay : UserControl
{
    private ProjectsViewModel? _vm;
    private LocalProject? _editProject;
    private Func<System.Threading.Tasks.Task>? _onSaved;
    private Action? _onAfterSave;

    // User data
    private List<LocalUser> _foremanUsers = [];
    private List<LocalUser> _workerUsers = [];
    private LocalUser? _selectedForeman;
    private readonly HashSet<Guid> _selectedWorkerIds = [];

    public CreateProjectOverlay()
    {
        InitializeComponent();
    }

    public void SetCreateMode(ProjectsViewModel vm)
    {
        _vm = vm;
        TitleLabel.Text = "Создать проект";
        SaveButton.Content = "Создать проект";
        _ = LoadUsersAsync();
    }

    public void SetEditMode(ProjectsViewModel vm, LocalProject project,
        Func<System.Threading.Tasks.Task>? onSaved = null, Action? onAfterSave = null)
    {
        _vm = vm;
        _editProject = project;
        _onSaved = onSaved;
        _onAfterSave = onAfterSave;
        TitleLabel.Text = "Редактировать проект";
        SaveButton.Content = "Сохранить изменения";

        NameBox.Text = project.Name;
        DescriptionBox.Text = project.Description ?? "";
        ClientBox.Text = project.Client ?? "";
        AddressBox.Text = project.Address ?? "";

        if (project.StartDate.HasValue)
            StartDatePicker.SelectedDate = project.StartDate.Value.ToDateTime(TimeOnly.MinValue);
        if (project.EndDate.HasValue)
            EndDatePicker.SelectedDate = project.EndDate.Value.ToDateTime(TimeOnly.MinValue);

        _ = LoadUsersAsync(project.ManagerId, project.Id);
    }

    private static bool IsManagerRole(string role) =>
        role is "ProjectManager" or "Manager" or "Project Manager";

    private static bool IsAdminRole(string role) =>
        role is "Admin" or "Administrator";

    private async System.Threading.Tasks.Task LoadUsersAsync(Guid? selectedManagerId = null, Guid? projectId = null)
    {
        var auth = App.Services.GetRequiredService<IAuthService>();
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        string role = auth.UserRole ?? "";

        if (IsManagerRole(role))
        {
            var self = await db.Users.FindAsync(auth.UserId);
            if (self is not null)
            {
                ManagerCombo.ItemsSource = new[] { self };
                ManagerCombo.SelectedIndex = 0;
                ManagerCombo.IsEnabled = false;
            }
        }
        else
        {
            var managers = await db.Users
                .Where(u => u.RoleName == "ProjectManager" || u.RoleName == "Manager"
                         || u.RoleName == "Project Manager")
                .OrderBy(u => u.Name)
                .ToListAsync();

            if (managers.Count == 0)
                managers = await db.Users.OrderBy(u => u.Name).ToListAsync();

            ManagerCombo.ItemsSource = managers;
            if (selectedManagerId.HasValue)
                ManagerCombo.SelectedValue = selectedManagerId.Value;
            else if (managers.Count > 0)
                ManagerCombo.SelectedIndex = 0;
        }

        // Load foremans
        _foremanUsers = await db.Users
            .Where(u => u.RoleName == "Foreman" || u.RoleName == "Прораб")
            .OrderBy(u => u.Name)
            .ToListAsync();
        ForemanPickerList.ItemsSource = _foremanUsers.Select(u => new UserPickerItem(u)).ToList();

        // Load workers
        _workerUsers = await db.Users
            .Where(u => u.RoleName == "Worker" || u.RoleName == "Работник")
            .OrderBy(u => u.Name)
            .ToListAsync();
        WorkerPickerList.ItemsSource = _workerUsers.Select(u => new UserPickerItem(u)).ToList();

        // If editing, load existing project members
        if (projectId.HasValue)
        {
            var members = await db.ProjectMembers
                .Where(m => m.ProjectId == projectId.Value)
                .ToListAsync();

            var foremanMember = members.FirstOrDefault(m => m.UserRole is "Foreman" or "Прораб");
            if (foremanMember is not null)
            {
                _selectedForeman = _foremanUsers.FirstOrDefault(u => u.Id == foremanMember.UserId);
                if (_selectedForeman is not null)
                    ShowSelectedForeman(_selectedForeman);
            }

            var workerMembers = members.Where(m => m.UserRole is "Worker" or "Работник").ToList();
            foreach (var wm in workerMembers)
            {
                _selectedWorkerIds.Add(wm.UserId);
            }
            RefreshWorkerChips(workerMembers.Select(m => new WorkerChipInfo(m.UserId, m.UserName, GetInitials(m.UserName))).ToList());
        }
    }

    private void ForemanItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is Border b && b.Tag is UserPickerItem item)
        {
            _selectedForeman = item.User;
            ShowSelectedForeman(item.User);
        }
    }

    private void ShowSelectedForeman(LocalUser user)
    {
        SelectedForemanName.Text = user.Name;
        ForemanAvatarText.Text = GetInitials(user.Name);
        SelectedForemanPanel.Visibility = Visibility.Visible;
    }

    private void RemoveForeman_Click(object sender, RoutedEventArgs e)
    {
        _selectedForeman = null;
        SelectedForemanPanel.Visibility = Visibility.Collapsed;
    }

    private async System.Threading.Tasks.Task RemoveWorkerAsync(Guid userId, string workerName)
    {
        if (_selectedWorkerIds.Count <= 1)
        {
            ShowError("В проекте должен остаться хотя бы один работник.");
            return;
        }
        if (_editProject is not null)
        {
            var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
            await using var db = await dbFactory.CreateDbContextAsync();
            var taskIds = await db.Tasks.Where(t => t.ProjectId == _editProject.Id).Select(t => t.Id).ToListAsync();
            var hasTaskAssign = await db.TaskAssignees.AnyAsync(ta => taskIds.Contains(ta.TaskId) && ta.UserId == userId);
            var stageIds = await db.TaskStages.Where(s => taskIds.Contains(s.TaskId)).Select(s => s.Id).ToListAsync();
            var hasStageAssign = await db.StageAssignees.AnyAsync(sa => stageIds.Contains(sa.StageId) && sa.UserId == userId);
            if (hasTaskAssign || hasStageAssign)
            {
                var owner = Window.GetWindow(this) ?? Application.Current.MainWindow;
                var ok = Dialogs.ConfirmDeleteDialog.Show(owner,
                    "работника из проекта",
                    workerName,
                    "С этим работником связаны назначения на задачи и этапы. Они будут удалены. Продолжить?");
                if (!ok) return;
            }
        }
        _selectedWorkerIds.Remove(userId);
        var updated = _workerUsers
            .Where(u => _selectedWorkerIds.Contains(u.Id))
            .Select(u => new WorkerChipInfo(u.Id, u.Name, GetInitials(u.Name)))
            .ToList();
        RefreshWorkerChips(updated);
    }

    private void WorkerItem_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border b || b.Tag is not UserPickerItem item) return;

        var userId = item.User.Id;
        if (_selectedWorkerIds.Contains(userId))
            _selectedWorkerIds.Remove(userId);
        else
            _selectedWorkerIds.Add(userId);

        var selectedWorkers = _workerUsers
            .Where(u => _selectedWorkerIds.Contains(u.Id))
            .Select(u => new WorkerChipInfo(u.Id, u.Name, GetInitials(u.Name)))
            .ToList();
        RefreshWorkerChips(selectedWorkers);
    }

    private void RefreshWorkerChips(List<WorkerChipInfo> workers)
    {
        SelectedWorkersPanel.Children.Clear();
        if (workers.Count == 0)
        {
            SelectedWorkersPanel.Visibility = Visibility.Collapsed;
            return;
        }

        SelectedWorkersPanel.Visibility = Visibility.Visible;
        foreach (var w in workers)
        {
            var chip = BuildWorkerChip(w);
            SelectedWorkersPanel.Children.Add(chip);
        }
    }

    private Border BuildWorkerChip(WorkerChipInfo info)
    {
        var chip = new Border
        {
            CornerRadius = new CornerRadius(20),
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 3, 6, 3),
            Background = new SolidColorBrush(Color.FromRgb(0xFE, 0xF3, 0xC7)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xFB, 0xBF, 0x24)),
            BorderThickness = new Thickness(1)
        };
        var sp = new StackPanel { Orientation = Orientation.Horizontal };
        var avatar = new Border
        {
            Width = 20, Height = 20,
            CornerRadius = new CornerRadius(10),
            Background = new SolidColorBrush(Color.FromRgb(0x92, 0x40, 0x0E)),
            Margin = new Thickness(0, 0, 6, 0)
        };
        avatar.Child = new TextBlock
        {
            Text = info.Initials,
            FontSize = 8, FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        sp.Children.Add(avatar);
        sp.Children.Add(new TextBlock
        {
            Text = info.Name,
            FontSize = 12,
            Foreground = new SolidColorBrush(Color.FromRgb(0x92, 0x40, 0x0E)),
            VerticalAlignment = VerticalAlignment.Center
        });
        var removeBtn = new Button
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            Margin = new Thickness(6, 0, 0, 0),
            Tag = (info.UserId, info.Name)
        };
        removeBtn.Template = CreateRemoveBtnTemplate();
        removeBtn.Click += async (s, _) =>
        {
            if (s is Button btn && btn.Tag is (Guid uid, string name))
                await RemoveWorkerAsync(uid, name);
        };
        sp.Children.Add(removeBtn);
        chip.Child = sp;
        return chip;
    }

    private static ControlTemplate CreateRemoveBtnTemplate()
    {
        var tpl = new ControlTemplate(typeof(Button));
        var factory = new FrameworkElementFactory(typeof(Border));
        factory.SetValue(Border.BackgroundProperty, Brushes.Transparent);
        var txtFactory = new FrameworkElementFactory(typeof(TextBlock));
        txtFactory.SetValue(TextBlock.TextProperty, "✕");
        txtFactory.SetValue(TextBlock.FontSizeProperty, 10.0);
        txtFactory.SetValue(TextBlock.ForegroundProperty, new SolidColorBrush(Colors.Gray));
        factory.AppendChild(txtFactory);
        tpl.VisualTree = factory;
        return tpl;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => MainWindow.Instance?.HideDrawer();

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        ErrorPanel.Visibility = Visibility.Collapsed;

        if (string.IsNullOrWhiteSpace(NameBox.Text))
        { ShowError("Введите название проекта."); return; }
        if (string.IsNullOrWhiteSpace(ClientBox.Text))
        { ShowError("Введите название заказчика."); return; }
        if (StartDatePicker.SelectedDate is null || EndDatePicker.SelectedDate is null)
        { ShowError("Выберите даты начала и завершения."); return; }
        if (ManagerCombo.SelectedValue is not Guid managerId)
        { ShowError("Выберите ответственного менеджера."); return; }

        if (_editProject is null && _selectedForeman is null && _selectedWorkerIds.Count == 0)
        { ShowError("Назначьте прораба или хотя бы одного работника на проект."); return; }
        if (_editProject is not null && _selectedWorkerIds.Count == 0)
        { ShowError("В проекте должен остаться хотя бы один работник."); return; }

        var startDate = DateOnly.FromDateTime(StartDatePicker.SelectedDate.Value);
        var endDate   = DateOnly.FromDateTime(EndDatePicker.SelectedDate.Value);
        if (endDate < startDate)
        { ShowError("Дата завершения не может быть раньше даты начала."); return; }

        SaveButton.IsEnabled = false;
        try
        {
            Guid projectId;

            if (_editProject is null)
            {
                var localId = Guid.NewGuid();
                var req = new CreateProjectRequest(
                    NameBox.Text.Trim(),
                    string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim(),
                    ClientBox.Text.Trim(),
                    string.IsNullOrWhiteSpace(AddressBox.Text) ? null : AddressBox.Text.Trim(),
                    startDate, endDate, managerId);
                await _vm!.SaveNewProjectAsync(req, localId);
                projectId = localId;
            }
            else
            {
                var req = new UpdateProjectRequest(
                    NameBox.Text.Trim(),
                    string.IsNullOrWhiteSpace(DescriptionBox.Text) ? null : DescriptionBox.Text.Trim(),
                    ClientBox.Text.Trim(),
                    string.IsNullOrWhiteSpace(AddressBox.Text) ? null : AddressBox.Text.Trim(),
                    startDate, endDate, _editProject!.Status, managerId);
                await _vm!.SaveUpdatedProjectAsync(_editProject.Id, req);
                if (_onSaved is not null) await _onSaved();
                projectId = _editProject.Id;
            }

            // Save project members
            await SaveProjectMembersAsync(projectId);

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

    private async System.Threading.Tasks.Task SaveProjectMembersAsync(Guid projectId)
    {
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        var newMemberIds = new HashSet<Guid>();
        if (_selectedForeman is not null) newMemberIds.Add(_selectedForeman.Id);
        foreach (var uid in _selectedWorkerIds) newMemberIds.Add(uid);

        var existing = await db.ProjectMembers
            .Where(m => m.ProjectId == projectId)
            .ToListAsync();
        var removedIds = existing.Where(m => !newMemberIds.Contains(m.UserId)).Select(m => m.UserId).ToList();

        db.ProjectMembers.RemoveRange(existing);

        // Add foreman (можно убрать — при редактировании разрешено сохранять без прораба)
        if (_selectedForeman is not null)
        {
            db.ProjectMembers.Add(new LocalProjectMember
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                UserId = _selectedForeman.Id,
                UserName = _selectedForeman.Name,
                UserRole = "Foreman"
            });
        }

        // Add workers
        foreach (var workerId in _selectedWorkerIds)
        {
            var worker = _workerUsers.FirstOrDefault(u => u.Id == workerId);
            if (worker is null) continue;
            db.ProjectMembers.Add(new LocalProjectMember
            {
                Id = Guid.NewGuid(),
                ProjectId = projectId,
                UserId = workerId,
                UserName = worker.Name,
                UserRole = "Worker"
            });
        }

        // Снять назначения с задач/этапов для удалённых из проекта
        if (removedIds.Count > 0)
        {
            var taskIds = await db.Tasks.Where(t => t.ProjectId == projectId).Select(t => t.Id).ToListAsync();
            var toRemoveTask = await db.TaskAssignees
                .Where(ta => taskIds.Contains(ta.TaskId) && removedIds.Contains(ta.UserId))
                .ToListAsync();
            db.TaskAssignees.RemoveRange(toRemoveTask);
            var stageIds = await db.TaskStages.Where(s => taskIds.Contains(s.TaskId)).Select(s => s.Id).ToListAsync();
            var toRemoveStage = await db.StageAssignees
                .Where(sa => stageIds.Contains(sa.StageId) && removedIds.Contains(sa.UserId))
                .ToListAsync();
            db.StageAssignees.RemoveRange(toRemoveStage);
        }

        await db.SaveChangesAsync();
    }

    private static string GetInitials(string name)
    {
        var parts = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2
            ? $"{parts[0][0]}{parts[1][0]}".ToUpper()
            : name.Length > 0 ? name[0].ToString().ToUpper() : "?";
    }

    private void ShowError(string message)
    {
        ErrorText.Text = message;
        ErrorPanel.Visibility = Visibility.Visible;
    }

    private record UserPickerItem(LocalUser User)
    {
        public string Name => User.Name;
        public string Initials => GetInitials(User.Name);
    }

    private record WorkerChipInfo(Guid UserId, string Name, string Initials);
}
