using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MPMS.Data;
using MPMS.Infrastructure;
using MPMS.Models;
using MPMS.Services;
using MPMS.ViewModels;
using MPMS.Views.Overlays;

namespace MPMS.Views.Pages;

public partial class StageDetailPage
{
    private ObservableCollection<LocalMessage> _stageMessages = new();

    public StageDetailPage()
    {
        InitializeComponent();

        DataContextChanged += OnDataContextChanged;

        Loaded += async (_, _) =>
        {
            if (FindName("DueDatePicker") is DatePicker dp)
                DueDatePickerRestrictions.AttachNoPastSelectableBlackout(dp);

            if (DataContext is StageDetailViewModel vm)
            {
                StageManagementPanel.DataContext = DataContext;

                _ = Dispatcher.InvokeAsync(UpdatePanels, System.Windows.Threading.DispatcherPriority.Loaded);
                await vm.LoadAsync();

                // Load stage discussion messages and attach to control
                if (vm.EditStage is not null && vm.EditTask is not null)
                {
                    await using var db = await App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>().CreateDbContextAsync();
                    var messages = await db.Messages
                        .Where(m => m.StageId == vm.EditStage.Id)
                        .OrderBy(m => m.CreatedAt)
                        .ToListAsync();
                    var msgUserIds = messages.Select(m => m.UserId).Distinct().ToList();
                    if (msgUserIds.Count > 0)
                    {
                        var msgUserAvatars = await db.Users.Where(u => msgUserIds.Contains(u.Id))
                            .Select(u => new { u.Id, u.AvatarData, u.AvatarPath })
                            .ToListAsync();
                        var msgAvDict = msgUserAvatars.ToDictionary(u => u.Id);
                        foreach (var msg in messages)
                        {
                            if (msgAvDict.TryGetValue(msg.UserId, out var av))
                            {
                                msg.AvatarData = av.AvatarData;
                                msg.AvatarPath = av.AvatarPath;
                            }
                        }
                    }
                    _stageMessages = new ObservableCollection<LocalMessage>(messages);
                }
            }

            if (FindName("StageDiscussionControl") is FrameworkElement sdc && sdc is MPMS.Views.Components.DiscussionPanelControl dpc)
            {
                dpc.SendRequested -= OnStageDiscussionSendRequested;
                dpc.SendRequested += OnStageDiscussionSendRequested;
                dpc.UserPeekRequested -= OnUserPeekRequested;
                dpc.UserPeekRequested += OnUserPeekRequested;
                dpc.ItemsSource = _stageMessages;
            }
        };
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged oldVm)
            oldVm.PropertyChanged -= Vm_PropertyChanged;
        if (e.NewValue is INotifyPropertyChanged newVm)
            newVm.PropertyChanged += Vm_PropertyChanged;
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(StageDetailViewModel.IsStageMarkedForDeletion)
            or nameof(StageDetailViewModel.StageStatus)
            or nameof(StageDetailViewModel.ShowStageUploadButton)
            or nameof(StageDetailViewModel.ShowStageReportButton)
            or nameof(StageDetailViewModel.CanChangeStageStatus)
            or nameof(StageDetailViewModel.CanMarkStageForDeletion)
            or nameof(StageDetailViewModel.CanEditStageSummary)
            or nameof(StageDetailViewModel.IsStageCatalogEditable))
            Dispatcher.InvokeAsync(UpdatePanels);
    }

    private void OnOpenEditorRequested(LocalTaskStage stage, LocalTask task)
    {
        if (DataContext is not StageDetailViewModel vm) return;

        var currentGoBack = vm.GoBackAction;
        if (currentGoBack == null)
        {
            var main = App.Services.GetRequiredService<MainViewModel>();
            currentGoBack = () => main.GoBackCommand.Execute(null);
        }

        var overlay = new CreateStageOverlay();
        overlay.SetEditMode(
            stage,
            task,
            onSaved: async () =>
            {
                var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
                await using var db = await dbFactory.CreateDbContextAsync();
                var freshStage = await db.TaskStages.FindAsync(stage.Id);
                var freshTask = await db.Tasks.FindAsync(task.Id);

                if (freshStage is not null && freshTask is not null)
                {
                    vm.SetEditMode(freshStage, freshTask, goBack: currentGoBack);
                    await vm.ReloadAllAsync();
                }
            },
            onAfterSave: () => MainWindow.Instance?.HideDrawer());

        MainWindow.Instance?.ShowCenteredOverlay(overlay, MainWindow.WideFormOverlayWidth);
    }

    private void StageTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag }) return;

        if (DataContext is StageDetailViewModel vm)
            vm.ActiveTab = tag;
    }

    private void ProjectRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not PickerRowVm row) return;

        if (DataContext is StageDetailViewModel vm)
            vm.SelectedProjectId = row.Id;
    }

    private void TaskRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not PickerRowVm row) return;

        if (DataContext is StageDetailViewModel vm)
            vm.SelectedTaskId = row.Id;
    }

    private void WorkerPeek_Click(object sender, RoutedEventArgs e)
    {
        e.Handled = true;

        if (sender is not FrameworkElement fe || fe.DataContext is not AssigneePickerItem item) return;

        if (DataContext is not StageDetailViewModel vm) return;

        if (vm.PeekProjectId is not Guid projectId) return;

        MainWindow.Instance?.TryOpenUserPeek(item.UserId, projectId);
    }

    private void AddFile_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is StageDetailViewModel vm && vm.CanUploadStageFiles)
            vm.FilesControlVM.UploadFileCommand.Execute(null);
    }

    private void CreateReport_Click(object sender, RoutedEventArgs e)
    {
        ReportPopup.IsOpen = true;
    }

    private void StageReport_Click(object sender, RoutedEventArgs e)
    {
        ReportPopup.IsOpen = false;
        if (DataContext is not StageDetailViewModel vm) return;
        if (vm.EditStage is null || vm.EditTask is null) return;
        var overlay = new StageReportOverlay(vm.EditStage, vm.EditTask);
        MainWindow.Instance?.ShowCenteredOverlay(overlay, 460);
    }

    private void EditStage_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not StageDetailViewModel vm) return;

        if (vm.EditStage is null || vm.EditTask is null) return;

        OnOpenEditorRequested(vm.EditStage, vm.EditTask);
    }

    private void UpdatePanels()
    {
        StageManagementPanel?.UpdateButtons();
    }

    private async void OnStageDiscussionSendRequested(object? sender, string text)
    {
        if (DataContext is not StageDetailViewModel vm) return;
        if (vm.EditStage is null || vm.EditTask is null) return;
        if (string.IsNullOrWhiteSpace(text)) return;

        var auth = App.Services.GetRequiredService<IAuthService>();
        var dbFactory = App.Services.GetRequiredService<IDbContextFactory<LocalDbContext>>();
        await using var db = await dbFactory.CreateDbContextAsync();

        var userName = auth.UserName ?? "—";
        var initials = string.IsNullOrEmpty(userName) ? "?"
            : string.Concat(userName.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(w => w.Length > 0 ? w[0].ToString().ToUpperInvariant() : ""));
        if (string.IsNullOrEmpty(initials)) initials = "?";

        var msg = new LocalMessage
        {
            Id = Guid.NewGuid(),
            StageId = vm.EditStage.Id,
            TaskId = vm.EditTask.Id,
            ProjectId = vm.EditTask.ProjectId,
            UserId = auth.UserId ?? Guid.Empty,
            UserName = userName,
            UserInitials = initials,
            UserColor = "#0F2038",
            UserRole = ProjectDetailViewModel.RoleToRussian(auth.UserRole),
            Text = text.Trim(),
            CreatedAt = DateTime.UtcNow
        };

        if (auth.UserId.HasValue)
        {
            var avatar = await db.Users
                .Where(u => u.Id == auth.UserId.Value)
                .Select(u => new { u.AvatarData, u.AvatarPath })
                .FirstOrDefaultAsync();
            if (avatar is not null)
            {
                msg.AvatarData = avatar.AvatarData;
                msg.AvatarPath = avatar.AvatarPath;
            }
        }

        db.Messages.Add(msg);
        await db.SaveChangesAsync();

        var sync = App.Services.GetRequiredService<ISyncService>();
        await sync.QueueOperationAsync("DiscussionMessage", msg.Id, SyncOperation.Create,
            new CreateDiscussionMessageRequest(msg.Id, msg.TaskId, msg.ProjectId, msg.StageId, msg.Text, msg.CreatedAt));

        // Local activity log
        var detailsSvcAuth = App.Services.GetRequiredService<IAuthService>();
        var parts = (detailsSvcAuth.UserName ?? "Система").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var actInitials = parts.Length >= 2 ? $"{parts[0][0]}{parts[1][0]}" : (detailsSvcAuth.UserName?.Length > 0 ? $"{detailsSvcAuth.UserName[0]}" : "?");
        var log = new LocalActivityLog
        {
            Id = Guid.NewGuid(),
            UserId = detailsSvcAuth.UserId,
            ActorRole = detailsSvcAuth.UserRole,
            UserName = detailsSvcAuth.UserName ?? "Система",
            UserInitials = actInitials.ToUpper(),
            UserColor = "#0F2038",
            ActionType = ActivityActionKind.Message,
            ActionText = $"Сообщение в этапе «{vm.EditStage.Name}»",
            DetailsText = ActivityDetailsService.BuildGenericDetails($"Сообщение в этапе «{vm.EditStage.Name}»", "Message", ActivityActionKind.Message),
            EntityType = "Message",
            EntityId = msg.Id,
            CreatedAt = DateTime.UtcNow
        };
        db.ActivityLogs.Add(log);
        await db.SaveChangesAsync();
        await sync.QueueLocalActivityLogAsync(log);

        _stageMessages.Add(msg);
        if (sender is MPMS.Views.Components.DiscussionPanelControl dpc)
            dpc.ScrollToBottom();
    }

    private void OnUserPeekRequested(object? sender, Guid userId)
    {
        if (DataContext is not StageDetailViewModel vm) return;
        var projectId = vm.EditTask?.ProjectId ?? Guid.Empty;
        MainWindow.Instance?.TryOpenUserPeek(userId, projectId);
    }
}

