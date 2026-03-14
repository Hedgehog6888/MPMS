using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using MPMS.Models;
using MPMS.Services;
using MPMS.ViewModels;
using TaskStatus = MPMS.Models.TaskStatus;

namespace MPMS.Views.Overlays;

public partial class TaskDetailOverlay : UserControl
{
    private TaskDetailViewModel? _vm;
    private Action? _onClosed;

    public TaskDetailOverlay()
    {
        InitializeComponent();
    }

    public void SetTask(LocalTask task, Action? onClosed = null)
    {
        _onClosed = onClosed;
        _vm = App.Services.GetRequiredService<TaskDetailViewModel>();
        _vm.SetTask(task);
        DataContext = _vm;
        _ = LoadDataAsync();
        ApplyRoleRestrictions();
    }

    private void ApplyRoleRestrictions()
    {
        var auth = App.Services.GetRequiredService<IAuthService>();
        string role = auth.UserRole ?? "";
        bool isWorker   = string.Equals(role, "Worker",   StringComparison.OrdinalIgnoreCase);
        bool isForeman  = string.Equals(role, "Foreman",  StringComparison.OrdinalIgnoreCase);
        bool isManager  = role is "Manager" or "ProjectManager" or "Project Manager";
        bool isAdmin    = role is "Admin" or "Administrator";

        if (isWorker)
        {
            EditTaskBtn.Visibility    = Visibility.Collapsed;
            ChangeStatusBtn.Visibility = Visibility.Collapsed;
            AddStageBtn.Visibility    = Visibility.Collapsed;
        }

    }

    private async System.Threading.Tasks.Task LoadDataAsync()
    {
        if (_vm is null) return;
        await _vm.LoadAsync();
        UpdateStagesTabLabel();
        UpdateEmptyStates();
    }

    private void UpdateStagesTabLabel()
    {
        if (_vm is null) return;
        StagesTab.Content = _vm.Stages.Count > 0
            ? $"Этапы ({_vm.Stages.Count})"
            : "Этапы";
    }

    private void UpdateEmptyStates()
    {
        if (_vm is null) return;
        _vm.HasNoStages = _vm.Stages.Count == 0;
        _vm.HasNoMaterials = _vm.AllMaterials.Count == 0;
        _vm.HasNoFiles = _vm.Files.Count == 0;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        _onClosed?.Invoke();
        MainWindow.Instance?.HideDrawer();
    }

    private void InnerTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb) return;
        string tag = rb.Tag as string ?? "";

        StagesPanel.Visibility   = tag == "Stages"    ? Visibility.Visible : Visibility.Collapsed;
        MaterialsPanel.Visibility = tag == "Materials" ? Visibility.Visible : Visibility.Collapsed;
        FilesPanel.Visibility     = tag == "Files"     ? Visibility.Visible : Visibility.Collapsed;
        MessagesPanel.Visibility = tag == "Messages"   ? Visibility.Visible : Visibility.Collapsed;
    }

    private void EditTask_Click(object sender, RoutedEventArgs e)
    {
        if (_vm?.Task is null) return;
        var overlay = new CreateTaskOverlay();
        var self = this;
        overlay.SetEditMode(
            _vm.Task,
            onSaved: async () =>
            {
                await _vm.LoadAsync();
                UpdateStagesTabLabel();
                UpdateEmptyStates();
                _onClosed?.Invoke();
            },
            onAfterSave: () => MainWindow.Instance?.ShowDrawer(self, 500));
        MainWindow.Instance?.ShowDrawer(overlay);
    }

    private void ChangeStatus_Click(object sender, RoutedEventArgs e)
    {
        if (_vm?.Task is null) return;

        var menuStyle = Application.Current.FindResource("StatusMenu") as Style;
        var itemStyle = Application.Current.FindResource("StatusMenuItem") as Style;
        var menu = new ContextMenu { Style = menuStyle };

        void AddItem(string label, TaskStatus status)
        {
            var item = new MenuItem { Header = label, Style = itemStyle };
            item.Click += async (s, _) =>
            {
                await _vm.ChangeTaskStatusAsync(status);
                // Notify project page to refresh and close drawer
                _onClosed?.Invoke();
                MainWindow.Instance?.HideDrawer();
            };
            menu.Items.Add(item);
        }

        AddItem("Запланирована", TaskStatus.Planned);
        AddItem("Выполняется", TaskStatus.InProgress);
        AddItem("Приостановлена", TaskStatus.Paused);
        AddItem("Завершена", TaskStatus.Completed);

        menu.PlacementTarget = sender as UIElement;
        menu.IsOpen = true;
    }

    private void AddStage_Click(object sender, RoutedEventArgs e)
    {
        if (_vm?.Task is null) return;
        var overlay = new CreateStageOverlay();
        overlay.SetTask(_vm.Task, async () =>
        {
            await _vm.LoadAsync();
            UpdateStagesTabLabel();
            UpdateEmptyStates();
        });
        MainWindow.Instance?.ShowDrawer(overlay);
    }

    private void UploadFiles_Click(object sender, RoutedEventArgs e)
    {
        MessageBox.Show("Загрузка файлов будет доступна в следующей версии.", "Информация",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private async void StartStage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not LocalTaskStage stage || _vm is null) return;
        await _vm.ChangeStageStatusCommand.ExecuteAsync((stage, Models.StageStatus.InProgress));
    }

    private async void CompleteStage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not LocalTaskStage stage || _vm is null) return;
        await _vm.ChangeStageStatusCommand.ExecuteAsync((stage, Models.StageStatus.Completed));
    }

    private async void RevertStage_Click(object sender, RoutedEventArgs e)
    {
        // InProgress → Planned (cancel/revert action)
        if (sender is not Button btn || btn.Tag is not LocalTaskStage stage || _vm is null) return;
        await _vm.ChangeStageStatusCommand.ExecuteAsync((stage, Models.StageStatus.Planned));
    }

    private async void ReopenStage_Click(object sender, RoutedEventArgs e)
    {
        // Completed → InProgress (reopen stage)
        if (sender is not Button btn || btn.Tag is not LocalTaskStage stage || _vm is null) return;
        await _vm.ChangeStageStatusCommand.ExecuteAsync((stage, Models.StageStatus.InProgress));
    }

    private async void SendMessage_Click(object sender, RoutedEventArgs e)
    {
        if (_vm is null || string.IsNullOrWhiteSpace(MessageInput.Text)) return;
        await _vm.SendMessageAsync(MessageInput.Text);
        MessageInput.Clear();
    }

    private async void MessageInput_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter && _vm is not null && !string.IsNullOrWhiteSpace(MessageInput.Text))
        {
            await _vm.SendMessageAsync(MessageInput.Text);
            MessageInput.Clear();
            e.Handled = true;
        }
    }

    private async void MarkStageForDeletion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not LocalTaskStage stage || _vm is null) return;
        await _vm.MarkStageForDeletionCommand.ExecuteAsync(stage);
    }

    private async void DeleteStage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not LocalTaskStage stage || _vm is null) return;
        var result = MessageBox.Show($"Удалить этап «{stage.Name}»?",
            "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
            await _vm.DeleteStageCommand.ExecuteAsync(stage);
    }

    private void EditStage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not LocalTaskStage stage || _vm?.Task is null) return;
        var overlay = new CreateStageOverlay();
        var self = this;
        overlay.SetEditMode(
            stage,
            _vm.Task,
            onSaved: async () =>
            {
                await _vm.LoadAsync();
                UpdateStagesTabLabel();
                UpdateEmptyStates();
            },
            onAfterSave: () => MainWindow.Instance?.ShowDrawer(self, 500));
        MainWindow.Instance?.ShowDrawer(overlay);
    }
}
