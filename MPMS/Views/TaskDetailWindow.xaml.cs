using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using MPMS.Models;
using MPMS.ViewModels;
using MPMS.Views.Dialogs;
using TaskStatus = MPMS.Models.TaskStatus;

namespace MPMS.Views;

public partial class TaskDetailWindow : Window
{
    private readonly TaskDetailViewModel _vm;

    public TaskDetailWindow(TaskDetailViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    public void SetTask(LocalTask task)
    {
        _vm.SetTask(task);
        _ = _vm.LoadAsync();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void InnerTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton rb || rb.Tag is not string tab) return;

        StagesTabPanel.Visibility    = tab == "Stages"    ? Visibility.Visible : Visibility.Collapsed;
        MaterialsTabPanel.Visibility = tab == "Materials" ? Visibility.Visible : Visibility.Collapsed;
        FilesTabPanel.Visibility     = tab == "Files"     ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void CreateStage_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Task is null) return;

        var dialog = App.Services.GetRequiredService<CreateStageDialog>();
        dialog.Owner = this;
        dialog.SetTask(_vm.Task.Id);

        if (dialog.ShowDialog() == true && dialog.Result is not null)
        {
            var id = Guid.NewGuid();
            await _vm.SaveNewStageAsync(dialog.Result, id);
        }
    }

    private async void EditStage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not LocalTaskStage stage) return;
        if (stage.Status == StageStatus.Completed) return;

        var dialog = App.Services.GetRequiredService<CreateStageDialog>();
        dialog.Owner = this;
        dialog.SetEditMode(stage);

        if (dialog.ShowDialog() == true && dialog.UpdateResult is not null)
            await _vm.SaveUpdatedStageAsync(stage.Id, dialog.UpdateResult);
    }

    private async void DeleteStage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not LocalTaskStage stage) return;

        if (MPMS.Views.Dialogs.ConfirmDeleteDialog.Show(this, "Этап", stage.Name))
            await _vm.DeleteStageCommand.ExecuteAsync(stage);
    }

    private async void StageStatus_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox cb || cb.Tag is not LocalTaskStage stage
            || cb.SelectedItem is not ComboBoxItem item || item.Tag is not string statusStr)
            return;

        if (!Enum.TryParse<StageStatus>(statusStr, out var newStatus)
            || newStatus == stage.Status) return;

        await _vm.ChangeStageStatusCommand.ExecuteAsync((stage, newStatus));
    }

    private void EditTask_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Task is null) return;

        var dialog = App.Services.GetRequiredService<CreateTaskDialog>();
        dialog.Owner = this;
        dialog.SetEditMode(_vm.Task);

        if (dialog.ShowDialog() == true && dialog.UpdateResult is not null)
        {
            _ = _vm.EditTaskAsync(_vm.Task.Id, dialog.UpdateResult);
        }
    }

    private void ChangeStatus_Click(object sender, RoutedEventArgs e)
    {
        if (_vm.Task is null) return;

        // Show a simple status picker
        var menu = new ContextMenu();
        var statuses = new[]
        {
            (TaskStatus.Planned,    "Запланирована"),
            (TaskStatus.InProgress, "В процессе"),
            (TaskStatus.Paused,     "Приостановлена"),
            (TaskStatus.Completed,  "Завершена"),
        };

        foreach (var (status, label) in statuses)
        {
            var item = new MenuItem { Header = label, Tag = status };
            item.Click += async (s, _) =>
            {
                if (s is MenuItem mi && mi.Tag is TaskStatus newStatus)
                    await _vm.ChangeTaskStatusAsync(newStatus);
            };
            menu.Items.Add(item);
        }

        if (sender is Button btn)
        {
            menu.PlacementTarget = btn;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
            menu.IsOpen = true;
        }
    }

    private void UploadFiles_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Выберите файлы для загрузки",
            Multiselect = true,
            Filter = "Все файлы (*.*)|*.*|Изображения|*.png;*.jpg;*.jpeg;*.gif;*.bmp|Документы|*.pdf;*.docx;*.xlsx"
        };

        if (dialog.ShowDialog() == true)
        {
            MessageBox.Show($"Выбрано файлов: {dialog.FileNames.Length}\nФункция загрузки файлов будет доступна при подключении к серверу.",
                "Загрузка файлов", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
