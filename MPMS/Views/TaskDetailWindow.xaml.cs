using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using MPMS.Models;
using MPMS.ViewModels;
using MPMS.Views.Dialogs;

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

        var dialog = App.Services.GetRequiredService<CreateStageDialog>();
        dialog.Owner = this;
        dialog.SetEditMode(stage);

        if (dialog.ShowDialog() == true && dialog.UpdateResult is not null)
            await _vm.SaveUpdatedStageAsync(stage.Id, dialog.UpdateResult);
    }

    private async void DeleteStage_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not LocalTaskStage stage) return;

        var result = MessageBox.Show($"Удалить этап «{stage.Name}»?",
            "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning);

        if (result == MessageBoxResult.Yes)
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
}
