using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPMS.Data;
using MPMS.Infrastructure;
using MPMS.Models;
using MPMS.Services;
using MPMS.ViewModels;
using TaskStatus = MPMS.Models.TaskStatus;

namespace MPMS.Views.Overlays;

public partial class UserPeekOverlay : UserControl
{
    private const int MaxAdditionalSpecialtyBadges = 7;

    private Guid _userId;
    private Guid _contextProjectId;
    private HashSet<Guid> _accessibleProjectIds = [];
    private string? _targetUserRole;

    // Для навигации из деталей задачи
    private Guid _currentDetailTaskId;
    private Guid _currentDetailTaskProjectId;

    // Для навигации из деталей этапа
    private Guid _currentDetailStageId;
    private Guid _currentDetailStageTaskId;
    private Guid _currentDetailStageProjectId;

    public UserPeekOverlay()
    {
        InitializeComponent();
    }

    public void SetUser(Guid userId, Guid projectId)
    {
        _userId = userId;
        _contextProjectId = projectId;
        _ = LoadAsync();
    }

    // ── Загрузка данных 

    private async System.Threading.Tasks.Task LoadAsync()
    {
        var auth = App.Services.GetRequiredService<IAuthService>();
        var viewerId = auth.UserId;

        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        if (viewerId is null)
        {
            await ShowErrorAsync("Не удалось определить текущего пользователя.");
            return;
        }

        if (!UserPeekAccess.CanViewerPeekTargetUser(auth, db, _userId))
        {
            await ShowErrorAsync("Нет доступа.");
            return;
        }

        _accessibleProjectIds = await UserPeekAccess.GetViewerAccessibleProjectIdsAsync(db, viewerId.Value, auth);
        if (_accessibleProjectIds.Count == 0)
        {
            await ShowErrorAsync("Нет доступных проектов.");
            return;
        }

        var user = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == _userId);
        if (user is null)
        {
            await ShowErrorAsync("Пользователь не найден.");
            return;
        }

        _targetUserRole = user.RoleName;
        bool isManager = UserPeekAccess.IsManager(_targetUserRole);

        var displayName = !string.IsNullOrWhiteSpace(user.Name)
            ? user.Name
            : $"{user.FirstName} {user.LastName}".Trim();
        if (string.IsNullOrWhiteSpace(displayName))
            displayName = user.Username;

        List<UserPeekProjectRowVm> projectRows;
        List<UserPeekTaskRowVm> taskRows = [];
        List<UserPeekStageRowVm> stageRows = [];
        List<UserPeekImageRowVm> imageRows = [];

        if (isManager)
        {
            var projects = await db.Projects.AsNoTracking()
                .Where(p => p.ManagerId == _userId && !p.IsArchived && !p.IsClosed && !p.IsMarkedForDeletion)
                .Select(p => new { p.Id, p.Name, p.Status, p.EndDate, p.StartDate, p.UpdatedAt })
                .ToListAsync();

            projectRows = projects
                .OrderBy(p => GetProjectStatusOrder(p.Status))
                .ThenBy(p => p.EndDate ?? DateOnly.MaxValue)
                .ThenBy(p => p.StartDate ?? DateOnly.MaxValue)
                .ThenByDescending(p => p.UpdatedAt)
                .ThenBy(p => p.Name)
                .Select(p => new UserPeekProjectRowVm { ProjectId = p.Id, Name = p.Name, Status = p.Status })
                .ToList();
        }
        else
        {
            var targetProjectIds = await GetTargetUserProjectIdsAsync(db, _userId);
            var scope = _accessibleProjectIds.Intersect(targetProjectIds).ToHashSet();

            var projectsInScopeRaw = await db.Projects.AsNoTracking()
                .Where(p => scope.Contains(p.Id) && !p.IsArchived && !p.IsClosed)
                .Select(p => new { p.Id, p.Name, p.Status, p.IsMarkedForDeletion, p.EndDate, p.StartDate, p.UpdatedAt })
                .ToListAsync();

            var projectsInScope = projectsInScopeRaw
                .OrderBy(p => p.IsMarkedForDeletion ? 1 : 0)
                .ThenBy(p => GetProjectStatusOrder(p.Status))
                .ThenBy(p => p.EndDate ?? DateOnly.MaxValue)
                .ThenBy(p => p.StartDate ?? DateOnly.MaxValue)
                .ThenByDescending(p => p.UpdatedAt)
                .ThenBy(p => p.Name)
                .ToList();

            var projById = projectsInScope.ToDictionary(x => x.Id);

            projectRows = projectsInScope
                .Select(p => new UserPeekProjectRowVm { ProjectId = p.Id, Name = p.Name, Status = p.Status })
                .ToList();

            var taskEntitiesRaw = await db.Tasks.AsNoTracking()
                .Where(t => scope.Contains(t.ProjectId) && !t.IsArchived
                    && (t.AssignedUserId == _userId
                        || db.TaskAssignees.Any(ta => ta.TaskId == t.Id && ta.UserId == _userId)))
                .ToListAsync();

            var taskEntities = taskEntitiesRaw
                .OrderBy(t =>
                    t.IsMarkedForDeletion || (projById.TryGetValue(t.ProjectId, out var tp) && tp.IsMarkedForDeletion)
                        ? 1 : 0)
                .ThenBy(t => GetTaskStatusOrder(t.Status))
                .ThenBy(t => t.DueDate ?? DateOnly.MaxValue)
                .ThenByDescending(t => t.UpdatedAt)
                .ThenBy(t => t.ProjectName)
                .ThenBy(t => t.Name)
                .ToList();

            taskRows = taskEntities.Select(t =>
            {
                var projMarked = projById.TryGetValue(t.ProjectId, out var pr) && pr.IsMarkedForDeletion;
                return new UserPeekTaskRowVm
                {
                    TaskId = t.Id,
                    ProjectId = t.ProjectId,
                    Name = t.Name,
                    ProjectName = t.ProjectName,
                    Status = t.Status,
                    EffectiveTaskMarkedForDeletion = t.IsMarkedForDeletion || projMarked,
                    DueDateLine = FormatDueDateLine(t.DueDate),
                };
            }).ToList();

            bool isForeman = UserPeekAccess.IsForeman(_targetUserRole);

            if (!isForeman)
            {
                var stageEntitiesRaw = await (
                    from s in db.TaskStages.AsNoTracking()
                    join t in db.Tasks.AsNoTracking() on s.TaskId equals t.Id
                    where scope.Contains(t.ProjectId) && !t.IsArchived && !s.IsArchived
                          && (s.AssignedUserId == _userId
                              || db.StageAssignees.Any(sa => sa.StageId == s.Id && sa.UserId == _userId))
                    select new { Stage = s, Task = t }).ToListAsync();

                var stageEntities = stageEntitiesRaw
                    .OrderBy(x =>
                        x.Stage.IsMarkedForDeletion || x.Task.IsMarkedForDeletion
                        || (projById.TryGetValue(x.Task.ProjectId, out var sp) && sp.IsMarkedForDeletion)
                            ? 1 : 0)
                    .ThenBy(x => GetStageStatusOrder(x.Stage.Status))
                    .ThenBy(x => x.Stage.DueDate ?? DateOnly.MaxValue)
                    .ThenByDescending(x => x.Stage.UpdatedAt)
                    .ThenBy(x => x.Task.ProjectName)
                    .ThenBy(x => x.Task.Name)
                    .ThenBy(x => x.Stage.Name)
                    .ToList();

                foreach (var x in stageEntities)
                {
                    if (!projById.TryGetValue(x.Task.ProjectId, out var pr)) continue;
                    stageRows.Add(new UserPeekStageRowVm
                    {
                        StageId = x.Stage.Id,
                        TaskId = x.Task.Id,
                        ProjectId = x.Task.ProjectId,
                        StageName = x.Stage.Name,
                        TaskSubtitle = "Задача: " + x.Task.Name,
                        ProjectName = x.Task.ProjectName,
                        Status = x.Stage.Status,
                        EffectiveMarkedForDeletion = x.Stage.IsMarkedForDeletion || x.Task.IsMarkedForDeletion || pr.IsMarkedForDeletion,
                        DueDateLine = FormatDueDateLine(x.Stage.DueDate),
                    });
                }
            }

            var imageFilesRaw = await db.Files.AsNoTracking()
                .Where(f => f.UploadedById == _userId)
                .ToListAsync();

            var imageFileEntities = imageFilesRaw.Where(f => IsImage(f.FileName)).ToList();

            var taskIdsForImages = imageFileEntities.Where(f => f.TaskId.HasValue).Select(f => f.TaskId!.Value).ToHashSet();
            var stageIdsForImages = imageFileEntities.Where(f => f.StageId.HasValue).Select(f => f.StageId!.Value).ToHashSet();
            var projIdsForImages = imageFileEntities.Where(f => f.ProjectId.HasValue).Select(f => f.ProjectId!.Value).ToHashSet();

            var tasksDictForImages = await db.Tasks.AsNoTracking()
                .Where(t => taskIdsForImages.Contains(t.Id))
                .ToDictionaryAsync(t => t.Id);

            var stagesDictForImages = await db.TaskStages.AsNoTracking()
                .Where(s => stageIdsForImages.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id);

            var projsDictForImages = await db.Projects.AsNoTracking()
                .Where(p => projIdsForImages.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id);

            foreach (var f in imageFileEntities)
            {
                bool ok = false;
                string? ctxName = null;

                if (f.StageId.HasValue && stagesDictForImages.TryGetValue(f.StageId.Value, out var stg))
                {
                    if (tasksDictForImages.TryGetValue(stg.TaskId, out var stTask)
                        && !stg.IsArchived && !stg.IsMarkedForDeletion
                        && !stTask.IsArchived && !stTask.IsMarkedForDeletion
                        && scope.Contains(stTask.ProjectId)
                        && projById.TryGetValue(stTask.ProjectId, out var stPrj)
                        && !stPrj.IsMarkedForDeletion)
                    {
                        ok = true;
                        ctxName = stPrj.Name;
                    }
                }
                else if (f.TaskId.HasValue && tasksDictForImages.TryGetValue(f.TaskId.Value, out var tsk))
                {
                    if (!tsk.IsArchived && !tsk.IsMarkedForDeletion
                        && scope.Contains(tsk.ProjectId)
                        && projById.TryGetValue(tsk.ProjectId, out var tPrj)
                        && !tPrj.IsMarkedForDeletion)
                    {
                        ok = true;
                        ctxName = tPrj.Name;
                    }
                }
                else if (f.ProjectId.HasValue && projsDictForImages.TryGetValue(f.ProjectId.Value, out var prj))
                {
                    if (scope.Contains(prj.Id) && !prj.IsArchived && !prj.IsClosed && !prj.IsMarkedForDeletion)
                    {
                        ok = true;
                        ctxName = prj.Name;
                    }
                }

                if (ok)
                {
                    imageRows.Add(new UserPeekImageRowVm
                    {
                        FileId = f.Id,
                        FileName = f.FileName,
                        FileData = f.FileData,
                        FilePath = f.FilePath,
                        FileSize = f.FileSize,
                        Description = f.Description,
                        ProjectId = f.ProjectId ?? (f.TaskId.HasValue && tasksDictForImages.TryGetValue(f.TaskId.Value, out var t) ? t.ProjectId : Guid.Empty),
                        ProjectName = ctxName,
                        UploadedByName = f.UploadedByName,
                        UploadedById = f.UploadedById,
                        CreatedAt = f.OriginalCreatedAt ?? f.CreatedAt,
                    });
                }
            }
        }

        var bmp = AvatarHelper.GetImageSource(user.AvatarData, user.AvatarPath, displayName);
        var roleBadgeColor = GetRoleBadgeColor(user.RoleName);
        var roleBadgeForegroundColor = GetRoleBadgeForegroundColor(user.RoleName);
        var joinedStr = user.CreatedAt != default
            ? $"В системе с {user.CreatedAt:dd.MM.yyyy}"
            : null;

        await Dispatcher.InvokeAsync(() =>
        {
            ErrorPanel.Visibility = Visibility.Collapsed;
            ShowHubMode();

            // Шапка: роль-бейдж
            HubRoleBadge.Background = new SolidColorBrush(roleBadgeColor);
            HubRoleBadge.BorderBrush = new SolidColorBrush(roleBadgeForegroundColor);
            HubRoleBadgeText.Foreground = new SolidColorBrush(roleBadgeForegroundColor);
            HubRoleBadgeText.Text = user.RoleDisplayName;

            // Аватар
            if (bmp is not null)
            {
                AvatarImage.Source = bmp;
                AvatarImage.Visibility = Visibility.Visible;
                AvatarInitialsText.Visibility = Visibility.Collapsed;
            }
            else
            {
                AvatarImage.Source = null;
                AvatarImage.Visibility = Visibility.Collapsed;
                AvatarInitialsText.Text = user.Initials;
                AvatarInitialsText.Visibility = Visibility.Visible;
                AvatarBorder.Background = new SolidColorBrush(roleBadgeColor);
            }

            // Имя + инфо
            UserNameText.Text = displayName;
            RoleLineText.Text = user.RoleDisplayName;

            SpecialtiesWrap.Children.Clear();
            if (user.RoleName is "Worker" or "Работник")
            {
                var main = user.SubRole?.Trim();
                var extras = WorkerSpecialtiesJson.Deserialize(user.AdditionalSubRoles)
                    .Where(s => !string.IsNullOrWhiteSpace(s) &&
                                !string.Equals(s.Trim(), main, StringComparison.OrdinalIgnoreCase))
                    .Select(s => s.Trim())
                    .ToList();
                void AddNamedBadge(string label)
                {
                    var bg = WorkerSpecialtiesJson.BadgeBackgroundRgbForSpecName(label);
                    var fg = WorkerSpecialtiesJson.BadgeForegroundRgbForSpecName(label);
                    SpecialtiesWrap.Children.Add(CreateWorkerSpecBadge(label,
                        new SolidColorBrush(Color.FromRgb(bg.R, bg.G, bg.B)),
                        new SolidColorBrush(Color.FromRgb(fg.R, fg.G, fg.B))));
                }

                if (!string.IsNullOrWhiteSpace(main))
                    AddNamedBadge(main);
                else
                    AddNamedBadge("Работник");

                foreach (var ex in extras.Take(MaxAdditionalSpecialtyBadges))
                    AddNamedBadge(ex);

                SpecialtiesWrap.Visibility = Visibility.Visible;
            }
            else
                SpecialtiesWrap.Visibility = Visibility.Collapsed;

            if (!string.IsNullOrWhiteSpace(user.Email))
            {
                EmailText.Text = user.Email;
                EmailRow.Visibility = Visibility.Visible;
            }
            else
                EmailRow.Visibility = Visibility.Collapsed;

            if (joinedStr is not null)
            {
                JoinedText.Text = joinedStr;
                JoinedText.Visibility = Visibility.Visible;
            }
            else
                JoinedText.Visibility = Visibility.Collapsed;

            // Вкладки с счётчиками
            if (isManager)
            {
                TabsPanel.Visibility = Visibility.Collapsed;
                HubContextChipText.Text = "Проекты менеджера";
            }
            else
            {
                TabsPanel.Visibility = Visibility.Visible;
                TabProjectsLabel.Text = $"Проекты ({projectRows.Count})";
                TabTasksLabel.Text = $"Задачи ({taskRows.Count})";
                TabStagesLabel.Text = $"Этапы ({stageRows.Count})";
                TabImagesLabel.Text = $"Изображения ({imageRows.Count})";
                TabStages.Visibility = UserPeekAccess.IsForeman(_targetUserRole) ? Visibility.Collapsed : Visibility.Visible;
            }

            // Данные списков
            ProjectsList.ItemsSource = projectRows;
            TasksList.ItemsSource = taskRows;
            StagesList.ItemsSource = stageRows;
            ImagesList.ItemsSource = imageRows;

            NoProjectsText.Visibility = projectRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            NoTasksText.Visibility = taskRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            NoStagesText.Visibility = stageRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            NoImagesText.Visibility = imageRows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

            TabProjects.IsChecked = true;
            ShowProjectsTab();

            BodyScroll.ScrollToVerticalOffset(0);
        });
    }

    // ── Переключение режимов хаб/деталь

    private void ShowHubMode()
    {
        HubFixedSection.Visibility = Visibility.Visible;
        HubRoleBadge.Visibility = Visibility.Visible;
        HubContextChip.Visibility = Visibility.Visible;
        BackButton.Visibility = Visibility.Collapsed;
        HubPanel.Visibility = Visibility.Visible;
        TaskDetailPanel.Visibility = Visibility.Collapsed;
        StageDetailPanel.Visibility = Visibility.Collapsed;
    }

    private void ShowTaskDetailMode()
    {
        HubFixedSection.Visibility = Visibility.Collapsed;
        HubRoleBadge.Visibility = Visibility.Collapsed;
        HubContextChip.Visibility = Visibility.Collapsed;
        BackButton.Visibility = Visibility.Visible;
        HubPanel.Visibility = Visibility.Collapsed;
        StageDetailPanel.Visibility = Visibility.Collapsed;
        TaskDetailPanel.Visibility = Visibility.Visible;
    }

    private void ShowStageDetailMode()
    {
        HubFixedSection.Visibility = Visibility.Collapsed;
        HubRoleBadge.Visibility = Visibility.Collapsed;
        HubContextChip.Visibility = Visibility.Collapsed;
        BackButton.Visibility = Visibility.Visible;
        HubPanel.Visibility = Visibility.Collapsed;
        TaskDetailPanel.Visibility = Visibility.Collapsed;
        StageDetailPanel.Visibility = Visibility.Visible;
    }

    // ── Табы

    private void ShowProjectsTab()
    {
        ProjectsTabPanel.Visibility = Visibility.Visible;
        TasksTabPanel.Visibility = Visibility.Collapsed;
        StagesTabPanel.Visibility = Visibility.Collapsed;
        ImagesTabPanel.Visibility = Visibility.Collapsed;
    }

    private void ShowTasksTab()
    {
        ProjectsTabPanel.Visibility = Visibility.Collapsed;
        TasksTabPanel.Visibility = Visibility.Visible;
        StagesTabPanel.Visibility = Visibility.Collapsed;
        ImagesTabPanel.Visibility = Visibility.Collapsed;
    }

    private void ShowStagesTab()
    {
        ProjectsTabPanel.Visibility = Visibility.Collapsed;
        TasksTabPanel.Visibility = Visibility.Collapsed;
        StagesTabPanel.Visibility = Visibility.Visible;
        ImagesTabPanel.Visibility = Visibility.Collapsed;
    }

    private void ShowImagesTab()
    {
        ProjectsTabPanel.Visibility = Visibility.Collapsed;
        TasksTabPanel.Visibility = Visibility.Collapsed;
        StagesTabPanel.Visibility = Visibility.Collapsed;
        ImagesTabPanel.Visibility = Visibility.Visible;
    }

    private void TabProjects_Click(object sender, RoutedEventArgs e) => ShowProjectsTab();
    private void TabTasks_Click(object sender, RoutedEventArgs e) => ShowTasksTab();
    private void TabStages_Click(object sender, RoutedEventArgs e) => ShowStagesTab();
    private void TabImages_Click(object sender, RoutedEventArgs e) => ShowImagesTab();

    // ── Клики по строкам

    private void ProjectRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not UserPeekProjectRowVm vm) return;
        e.Handled = true;
        _ = NavigateToProjectAsync(vm.ProjectId);
    }

    private async void TaskRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not UserPeekTaskRowVm vm) return;
        e.Handled = true;
        await OpenTaskDetailAsync(vm.TaskId);
    }

    private async void StageRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not UserPeekStageRowVm vm) return;
        e.Handled = true;
        await OpenStageDetailAsync(vm.StageId);
    }

    private async void ImageRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not UserPeekImageRowVm vm) return;
        e.Handled = true;
        await OpenPhotoViewerAsync(vm);
    }

    private async System.Threading.Tasks.Task OpenPhotoViewerAsync(UserPeekImageRowVm vm)
    {
        string filePath = string.Empty;

        if (!string.IsNullOrEmpty(vm.FilePath) && File.Exists(vm.FilePath))
        {
            filePath = vm.FilePath;
        }
        else if (vm.FileData != null && vm.FileData.Length > 0)
        {
            var tempDir = Path.Combine(Path.GetTempPath(), $"mpms_photo_{vm.FileId}");
            Directory.CreateDirectory(tempDir);
            filePath = Path.Combine(tempDir, vm.FileName);
            await File.WriteAllBytesAsync(filePath, vm.FileData);
        }

        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            MessageBox.Show("Данные файла отсутствуют локально.", "Ошибка",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        byte[]? uploaderAvatarData = null;
        string? uploaderAvatarPath = null;
        if (vm.UploadedById != Guid.Empty)
        {
            try
            {
                await using var db = await App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>().CreateDbContextAsync();
                var uploader = await db.Users.Where(u => u.Id == vm.UploadedById)
                    .Select(u => new { u.AvatarData, u.AvatarPath })
                    .FirstOrDefaultAsync();
                if (uploader != null)
                {
                    uploaderAvatarData = uploader.AvatarData;
                    uploaderAvatarPath = uploader.AvatarPath;
                }
            }
            catch { }
        }

        string activeTab;
        if (ImagesTabPanel.Visibility == Visibility.Visible) activeTab = "Images";
        else if (StagesTabPanel.Visibility == Visibility.Visible) activeTab = "Stages";
        else if (TasksTabPanel.Visibility == Visibility.Visible) activeTab = "Tasks";
        else activeTab = "Projects";

        await MainWindow.Instance!.HideOverlayLayerAnimatedAsync();
        MainWindow.Instance!.PhotoViewerClosed += OnPhotoViewerClosed;

        MainWindow.Instance?.ShowPhotoViewer(filePath, vm.FileName, vm.Description, vm.UploadedByName, vm.UploadedById, uploaderAvatarData, uploaderAvatarPath, vm.ProjectId,
            (savedPath, savedFileName, savedDescription) => SaveEditedPhotoAsync(vm.FileId, savedPath, savedFileName, savedDescription, filePath));

        return;

        void OnPhotoViewerClosed()
        {
            MainWindow.Instance!.PhotoViewerClosed -= OnPhotoViewerClosed;
            MainWindow.Instance?.ShowOverlayLayerAnimated();
            Dispatcher.BeginInvoke(() =>
            {
                switch (activeTab)
                {
                    case "Images": ShowImagesTab(); break;
                    case "Stages": ShowStagesTab(); break;
                    case "Tasks": ShowTasksTab(); break;
                    default: ShowProjectsTab(); break;
                }
            });
        }
    }

    private async System.Threading.Tasks.Task SaveEditedPhotoAsync(Guid fileId, string savedPath, string savedFileName, string? savedDescription, string originalTempPath)
    {
        try
        {
            await using var db = await App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>().CreateDbContextAsync();
            var file = await db.Files.FindAsync(fileId);
            if (file is null) return;

            var ext = Path.GetExtension(savedFileName);
            var fileType = !string.IsNullOrEmpty(ext) ? ext.TrimStart('.').ToLowerInvariant() : "jpg";

            byte[] data;
            if (File.Exists(savedPath))
                data = await File.ReadAllBytesAsync(savedPath);
            else
                data = [];

            file.FileName = savedFileName;
            file.FileType = fileType;
            file.FileSize = data.Length;
            file.FileData = data;
            file.Description = savedDescription;
            file.LastModifiedLocally = DateTime.UtcNow;
            file.IsSynced = false;

            await db.SaveChangesAsync();

            if (!string.Equals(savedPath, originalTempPath, StringComparison.OrdinalIgnoreCase) && File.Exists(savedPath))
            {
                try { File.Move(savedPath, originalTempPath, true); } catch { }
            }

            try
            {
                var info = new FileInfo(originalTempPath);
                info.CreationTimeUtc = file.OriginalCreatedAt ?? file.CreatedAt;
                info.LastWriteTimeUtc = DateTime.UtcNow;
            }
            catch { }

            await LoadAsync();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка сохранения: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    // ── Детали задачи

    private async System.Threading.Tasks.Task OpenTaskDetailAsync(Guid taskId)
    {
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var task = await db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == taskId);
        if (task is null || !_accessibleProjectIds.Contains(task.ProjectId)) return;

        await ProgressCalculator.ApplyTaskMetricsForTaskAsync(db, task);

        _currentDetailTaskId = taskId;
        _currentDetailTaskProjectId = task.ProjectId;

        var dueStr = task.DueDate.HasValue
            ? (DateOnlyToStringConverter.Instance.Convert(task.DueDate, typeof(string), "long", CultureInfo.InvariantCulture) as string ?? "—")
            : "Не указан";
        var dueDayStr = task.DueDate.HasValue
            ? (DateOnlyToStringConverter.Instance.Convert(task.DueDate, typeof(string), "dayname", CultureInfo.InvariantCulture) as string ?? "")
            : "";

        var prioText = PriorityToStringConverter.Instance.Convert(task.Priority, typeof(string), null!, CultureInfo.InvariantCulture) as string ?? "—";
        var prioBrush = (PriorityToBrushConverter.Instance.Convert(task.Priority, typeof(Brush), null!, CultureInfo.InvariantCulture) as SolidColorBrush)
            ?? new SolidColorBrush(Color.FromRgb(0x6B, 0x77, 0x8C));

        var titleBandBrush = HeaderBandBrushForTask(task);
        var progressBrush = ProgressPercentToBrushConverter.Instance.Convert(
            task.ProgressPercent, typeof(Brush), null!, CultureInfo.InvariantCulture) as Brush
            ?? new SolidColorBrush(Color.FromRgb(0x00, 0x82, 0xFF));

        await Dispatcher.InvokeAsync(() =>
        {
            ShowTaskDetailMode();

            TaskDetailTitleBand.Background = titleBandBrush;
            TaskDetailTitle.Text = task.Name;

            TaskDetailProgressBar.Value = task.ProgressPercent;
            TaskDetailProgressBar.Foreground = progressBrush;
            TaskDetailProgressPct.Text = $"{task.ProgressPercent}%";
            TaskDetailStagesInfo.Text = $"{task.CompletedStages} из {task.TotalStages} этапов завершено";

            TaskDetailDue.Text = dueStr;
            TaskDetailDayName.Text = dueDayStr;

            TaskDetailPriority.Text = prioText;
            TaskDetailPriority.Foreground = prioBrush;
            TaskPriorityBodyBadge.BorderBrush = prioBrush;
            if (prioBrush is SolidColorBrush psb)
                TaskPriorityBodyBadge.Background = new SolidColorBrush(Color.FromArgb(26, psb.Color.R, psb.Color.G, psb.Color.B));
            else
                TaskPriorityBodyBadge.Background = new SolidColorBrush(Color.FromArgb(26, 0x6B, 0x77, 0x8C));

            TaskDetailDescription.Text = string.IsNullOrWhiteSpace(task.Description)
                ? "Описание не указано"
                : task.Description!;

            BodyScroll.ScrollToVerticalOffset(0);
        });
    }

    // ── Детали этапа

    private async System.Threading.Tasks.Task OpenStageDetailAsync(Guid stageId)
    {
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var stage = await db.TaskStages.AsNoTracking().FirstOrDefaultAsync(s => s.Id == stageId);
        if (stage is null) return;

        var task = await db.Tasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == stage.TaskId);
        if (task is null || !_accessibleProjectIds.Contains(task.ProjectId)) return;

        var proj = await db.Projects.AsNoTracking().FirstOrDefaultAsync(p => p.Id == task.ProjectId);
        stage.TaskIsMarkedForDeletion = task.IsMarkedForDeletion;
        stage.ProjectIsMarkedForDeletion = proj?.IsMarkedForDeletion ?? false;

        _currentDetailStageId = stageId;
        _currentDetailStageTaskId = task.Id;
        _currentDetailStageProjectId = task.ProjectId;

        var dueStr = stage.DueDate.HasValue
            ? (DateOnlyToStringConverter.Instance.Convert(stage.DueDate, typeof(string), "long", CultureInfo.InvariantCulture) as string ?? "—")
            : "Не указан";
        var dueDayStr = stage.DueDate.HasValue
            ? (DateOnlyToStringConverter.Instance.Convert(stage.DueDate, typeof(string), "dayname", CultureInfo.InvariantCulture) as string ?? "")
            : "";

        var stageTitleBandBrush = HeaderBandBrushForStage(stage);

        await Dispatcher.InvokeAsync(() =>
        {
            ShowStageDetailMode();

            StageDetailTitleBand.Background = stageTitleBandBrush;
            StageDetailTitle.Text = stage.Name;

            StageDetailDue.Text = dueStr;
            StageDetailDayName.Text = dueDayStr;

            StageDetailDescription.Text = string.IsNullOrWhiteSpace(stage.Description)
                ? "Описание не указано"
                : stage.Description!;

            StageDetailTaskLine.Text = task.Name;
            StageDetailProjectLine.Text = task.ProjectName;

            BodyScroll.ScrollToVerticalOffset(0);
        });
    }

    // ── Быстрые действия: навигация

    private void GoToTaskProject_Click(object sender, RoutedEventArgs e)
    {
        if (_currentDetailTaskProjectId != Guid.Empty)
            _ = NavigateToProjectAsync(_currentDetailTaskProjectId);
    }

    private void GoToStageProject_Click(object sender, RoutedEventArgs e)
    {
        if (_currentDetailStageProjectId != Guid.Empty)
            _ = NavigateToProjectAsync(_currentDetailStageProjectId);
    }

    private void OpenFullTask_Click(object sender, RoutedEventArgs e)
    {
        if (_currentDetailTaskId != Guid.Empty)
            _ = OpenFullTaskOverlayAsync(_currentDetailTaskId);
    }

    private void OpenFullTaskFromStage_Click(object sender, RoutedEventArgs e)
    {
        if (_currentDetailStageTaskId != Guid.Empty)
            _ = OpenFullTaskOverlayAsync(_currentDetailStageTaskId);
    }

    private void OpenFullStage_Click(object sender, RoutedEventArgs e)
    {
        if (_currentDetailStageId != Guid.Empty)
            _ = OpenFullStageOverlayAsync(_currentDetailStageId);
    }

    private async System.Threading.Tasks.Task NavigateToProjectAsync(Guid projectId)
    {
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var project = await db.Projects.FindAsync(projectId);
        if (project is null) return;

        await Dispatcher.InvokeAsync(() =>
        {
            MainWindow.Instance?.HideAllOverlays();
            if (MainWindow.Instance?.DataContext is MainViewModel vm)
                vm.NavigateToProject(project);
        });
    }

    private async System.Threading.Tasks.Task OpenFullTaskOverlayAsync(Guid taskId)
    {
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var taskEntity = await db.Tasks.FindAsync(taskId);
        if (taskEntity is null) return;

        var projInfo = await db.Projects
            .Where(p => p.Id == taskEntity.ProjectId)
            .Select(p => new { p.Name, p.IsMarkedForDeletion })
            .FirstOrDefaultAsync();
        taskEntity.ProjectName = projInfo?.Name ?? taskEntity.ProjectName;
        taskEntity.ProjectIsMarkedForDeletion = projInfo?.IsMarkedForDeletion ?? false;

        var stages = await db.TaskStages
            .Where(s => s.TaskId == taskId && !s.IsArchived)
            .ToListAsync();
        foreach (var s in stages)
        {
            s.TaskIsMarkedForDeletion = taskEntity.IsMarkedForDeletion;
            s.ProjectIsMarkedForDeletion = taskEntity.ProjectIsMarkedForDeletion;
        }
        ProgressCalculator.ApplyTaskMetrics(taskEntity, stages);

        var tasksVm = App.Services.GetRequiredService<TasksViewModel>();
        var project = await tasksVm.GetProjectForTaskAsync(taskEntity.ProjectId);

        await Dispatcher.InvokeAsync(() =>
        {
            UIElement? leftPanel = null;
            if (project is not null)
            {
                var projectPanel = new ProjectSummaryPanel();
                projectPanel.SetProject(project);
                leftPanel = projectPanel;
            }
            var overlay = new TaskDetailOverlay();
            overlay.SetTask(taskEntity);
            MainWindow.Instance?.HideAllOverlays();
            MainWindow.Instance?.ShowDrawer(leftPanel, overlay, MainWindow.TaskOrStageDetailWithLeftTotalWidth);
        });
    }

    private async System.Threading.Tasks.Task OpenFullStageOverlayAsync(Guid stageId)
    {
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();
        var stageEntity = await db.TaskStages.FindAsync(stageId);
        if (stageEntity is null) return;

        var task = await db.Tasks.FindAsync(stageEntity.TaskId);
        if (task is null) return;

        var projInfo = await db.Projects
            .Where(p => p.Id == task.ProjectId)
            .Select(p => new { p.Name, p.IsMarkedForDeletion })
            .FirstOrDefaultAsync();
        task.ProjectName = projInfo?.Name ?? task.ProjectName;
        task.ProjectIsMarkedForDeletion = projInfo?.IsMarkedForDeletion ?? false;

        var taskStages = await db.TaskStages
            .Where(s => s.TaskId == task.Id && !s.IsArchived)
            .ToListAsync();
        foreach (var s in taskStages)
        {
            s.TaskIsMarkedForDeletion = task.IsMarkedForDeletion;
            s.ProjectIsMarkedForDeletion = task.ProjectIsMarkedForDeletion;
        }
        ProgressCalculator.ApplyTaskMetrics(task, taskStages);
        stageEntity.TaskName = task.Name;
        stageEntity.TaskIsMarkedForDeletion = task.IsMarkedForDeletion;
        stageEntity.ProjectIsMarkedForDeletion = task.ProjectIsMarkedForDeletion;

        var main = App.Services.GetRequiredService<MainViewModel>();
        var stageEditor = App.Services.GetRequiredService<StageDetailViewModel>();
        stageEditor.SetEditMode(stageEntity, task,
            goBack: () => main.GoBackCommand.Execute(null),
            onSavedAsync: null);
        await Dispatcher.InvokeAsync(() =>
        {
            MainWindow.Instance?.HideAllOverlays();
            main.NavigateToStageEditor(stageEditor);
        });
    }

    // ── Назад / Закрыть

    private void Back_Click(object sender, RoutedEventArgs e)
    {
        ShowHubMode();
        BodyScroll.ScrollToVerticalOffset(0);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
        => MainWindow.Instance?.HideDrawer();

    // ── Вспомогательные методы

    private static Color GetRoleBadgeColor(string? roleName) => roleName switch
    {
        "Administrator" or "Admin" => Color.FromRgb(0xFE, 0xE2, 0xE2),
        "Project Manager" or "ProjectManager" or "Manager" => Color.FromRgb(0xDB, 0xE8, 0xFE),
        "Foreman" => Color.FromRgb(0xD1, 0xFA, 0xE5),
        "Worker" => Color.FromRgb(0xED, 0xE9, 0xFE),
        _ => Color.FromRgb(0xF1, 0xF3, 0xF5),
    };

    private static Color GetRoleBadgeForegroundColor(string? roleName) => roleName switch
    {
        "Administrator" or "Admin" => Color.FromRgb(0x99, 0x1B, 0x1B),
        "Project Manager" or "ProjectManager" or "Manager" => Color.FromRgb(0x1D, 0x4E, 0xD8),
        "Foreman" => Color.FromRgb(0x16, 0x65, 0x34),
        "Worker" => Color.FromRgb(0x6D, 0x28, 0xD9),
        _ => Color.FromRgb(0x4B, 0x55, 0x63),
    };

    /// <summary>Фон шапки по статусу задачи (как в сводке задачи слева).</summary>
    private static SolidColorBrush HeaderBandBrushForTask(LocalTask task)
    {
        if (task.EffectiveTaskMarkedForDeletion)
            return new SolidColorBrush(Color.FromRgb(0xFF, 0xF5, 0xF5));
        var displayStatus = task.TotalStages > 0 && task.CompletedStages >= task.TotalStages
            ? TaskStatus.Completed
            : task.Status;
        return displayStatus switch
        {
            TaskStatus.InProgress => new SolidColorBrush(Color.FromRgb(0xEF, 0xF6, 0xFF)),
            TaskStatus.Completed => new SolidColorBrush(Color.FromRgb(0xF0, 0xFD, 0xF4)),
            TaskStatus.Paused => new SolidColorBrush(Color.FromRgb(0xFF, 0xFB, 0xEB)),
            _ => new SolidColorBrush(Color.FromRgb(0xF8, 0xF9, 0xFA)),
        };
    }

    /// <summary>Та же палитра, что для задачи в сводке — по статусу этапа.</summary>
    private static SolidColorBrush HeaderBandBrushForStage(LocalTaskStage stage)
    {
        if (stage.EffectiveMarkedForDeletion)
            return new SolidColorBrush(Color.FromRgb(0xFF, 0xF5, 0xF5));
        return stage.Status switch
        {
            StageStatus.InProgress => new SolidColorBrush(Color.FromRgb(0xEF, 0xF6, 0xFF)),
            StageStatus.Completed => new SolidColorBrush(Color.FromRgb(0xF0, 0xFD, 0xF4)),
            _ => new SolidColorBrush(Color.FromRgb(0xF8, 0xF9, 0xFA)),
        };
    }

    private static int GetProjectStatusOrder(ProjectStatus status) => status switch
    {
        ProjectStatus.Planning => 0,
        ProjectStatus.InProgress => 1,
        ProjectStatus.Completed => 2,
        ProjectStatus.Cancelled => 3,
        _ => 9
    };

    private static int GetTaskStatusOrder(TaskStatus status) => status switch
    {
        TaskStatus.Planned => 0,
        TaskStatus.InProgress => 1,
        TaskStatus.Paused => 2,
        TaskStatus.Completed => 3,
        _ => 9
    };

    private static int GetStageStatusOrder(StageStatus status) => status switch
    {
        StageStatus.Planned => 0,
        StageStatus.InProgress => 1,
        StageStatus.Completed => 2,
        _ => 9
    };

    private static bool IsImage(string fileName)
    {
        var ext = Path.GetExtension(fileName)?.ToLowerInvariant();
        return ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp";
    }

    private static string FormatDueDateLine(DateOnly? dueDate) =>
        dueDate.HasValue
            ? $"Срок: {dueDate.Value.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture)}"
            : "Срок не указан";

    private static Border CreateWorkerSpecBadge(string text, Brush background, Brush foreground) =>
        new()
        {
            Background = background,
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(0, 0, 6, 4),
            Child = new TextBlock
            {
                Text = text,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = foreground,
            },
        };

    private async System.Threading.Tasks.Task ShowErrorAsync(string message)
    {
        await Dispatcher.InvokeAsync(() =>
        {
            ErrorText.Text = message;
            ErrorPanel.Visibility = Visibility.Visible;
            HubFixedSection.Visibility = Visibility.Collapsed;
            HubPanel.Visibility = Visibility.Visible;
            TaskDetailPanel.Visibility = Visibility.Collapsed;
            StageDetailPanel.Visibility = Visibility.Collapsed;
            HubRoleBadge.Visibility = Visibility.Collapsed;
            HubContextChip.Visibility = Visibility.Collapsed;
            BackButton.Visibility = Visibility.Collapsed;
            ProjectsTabPanel.Visibility = Visibility.Collapsed;
            TasksTabPanel.Visibility = Visibility.Collapsed;
            StagesTabPanel.Visibility = Visibility.Collapsed;
            ImagesTabPanel.Visibility = Visibility.Collapsed;
        });
    }

    private static async System.Threading.Tasks.Task<HashSet<Guid>> GetTargetUserProjectIdsAsync(
        LocalDbContext db, Guid targetUserId)
    {
        var openProjectIds = await db.Projects.AsNoTracking()
            .Where(p => !p.IsArchived && !p.IsClosed)
            .Select(p => p.Id)
            .ToListAsync();
        var openSet = openProjectIds.ToHashSet();

        var fromMembers = await db.ProjectMembers.AsNoTracking()
            .Where(m => m.UserId == targetUserId && openSet.Contains(m.ProjectId))
            .Select(m => m.ProjectId)
            .ToListAsync();

        var fromTasks = await db.Tasks.AsNoTracking()
            .Where(t => !t.IsArchived && openSet.Contains(t.ProjectId)
                && (t.AssignedUserId == targetUserId
                    || db.TaskAssignees.Any(ta => ta.TaskId == t.Id && ta.UserId == targetUserId)))
            .Select(t => t.ProjectId)
            .ToListAsync();

        var fromStages = await (
            from s in db.TaskStages.AsNoTracking()
            join t in db.Tasks.AsNoTracking() on s.TaskId equals t.Id
            where !s.IsArchived && !t.IsArchived && openSet.Contains(t.ProjectId)
                  && (s.AssignedUserId == targetUserId
                      || db.StageAssignees.Any(sa => sa.StageId == s.Id && sa.UserId == targetUserId))
            select t.ProjectId).ToListAsync();

        return fromMembers.Concat(fromTasks).Concat(fromStages).ToHashSet();
    }

    // ── Модели представления

    public sealed class UserPeekProjectRowVm
    {
        public Guid ProjectId { get; init; }
        public string Name { get; init; } = "";
        public ProjectStatus Status { get; init; }
    }

    public sealed class UserPeekTaskRowVm
    {
        public Guid TaskId { get; init; }
        public Guid ProjectId { get; init; }
        public string Name { get; init; } = "";
        public string ProjectName { get; init; } = "";
        public TaskStatus Status { get; init; }
        public bool EffectiveTaskMarkedForDeletion { get; init; }
        public string DueDateLine { get; init; } = "";
    }

    public sealed class UserPeekStageRowVm
    {
        public Guid StageId { get; init; }
        public Guid TaskId { get; init; }
        public Guid ProjectId { get; init; }
        public string StageName { get; init; } = "";
        public string TaskSubtitle { get; init; } = "";
        public string ProjectName { get; init; } = "";
        public StageStatus Status { get; init; }
        public bool EffectiveMarkedForDeletion { get; init; }
        public string DueDateLine { get; init; } = "";
    }

    public sealed class UserPeekImageRowVm
    {
        public Guid FileId { get; init; }
        public string FileName { get; init; } = "";
        public byte[]? FileData { get; init; }
        public string? FilePath { get; init; }
        public long FileSize { get; init; }
        public string? Description { get; init; }
        public Guid ProjectId { get; init; }
        public string? ProjectName { get; init; }
        public string UploadedByName { get; init; } = "";
        public Guid UploadedById { get; init; }
        public DateTime CreatedAt { get; init; }
    }
}
