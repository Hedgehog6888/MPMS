using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MPMS.Infrastructure;
using MPMS.Models;
using MPMS.ViewModels;
using MPMS.Views.Overlays;

namespace MPMS.Views.Pages;

public partial class StageEditPage
{
    public StageEditPage()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            if (FindName("DueDatePicker") is DatePicker dp)
                DueDatePickerRestrictions.AttachNoPastSelectableBlackout(dp);
        };
    }

    private void StageTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { Tag: string tag }) return;
        if (DataContext is StageEditViewModel vm)
            vm.ActiveTab = tag;
    }

    private void ProjectRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not PickerRowVm row) return;
        if (DataContext is StageEditViewModel vm)
            vm.SelectedProjectId = row.Id;
    }

    private void TaskRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not PickerRowVm row) return;
        if (DataContext is StageEditViewModel vm)
            vm.SelectedTaskId = row.Id;
    }

    private void AddServiceTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: LocalServiceTemplate tpl }) return;
        if (DataContext is StageEditViewModel vm)
            vm.AddServiceTemplateCommand.Execute(tpl);
    }

    private void DecServiceQty_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: StageServiceLineVm line }) return;
        if (DataContext is StageEditViewModel vm)
            vm.AdjustServiceQuantity(line, -1);
    }

    private void IncServiceQty_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: StageServiceLineVm line }) return;
        if (DataContext is StageEditViewModel vm)
            vm.AdjustServiceQuantity(line, 1);
    }

    private void WorkerRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.DataContext is not AssigneePickerItem item) return;
        if (DataContext is StageEditViewModel vm)
            vm.ToggleAssigneeCommand.Execute(item);
    }

    private void MaterialCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ComboBox cb || cb.Tag is not StageMaterialLineVm line) return;
        if (cb.SelectedItem is LocalMaterial m && DataContext is StageEditViewModel vm)
            vm.ApplyMaterialToLine(line, m);
    }

    private void DecMatQty_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: StageMaterialLineVm line }) return;
        if (DataContext is StageEditViewModel vm)
            vm.AdjustMaterialQuantity(line, -1);
    }

    private void IncMatQty_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: StageMaterialLineVm line }) return;
        if (DataContext is StageEditViewModel vm)
            vm.AdjustMaterialQuantity(line, 1);
    }

    private void RemoveMaterialRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: StageMaterialLineVm line }) return;
        if (DataContext is StageEditViewModel vm)
            vm.RemoveMaterialLineCommand.Execute(line);
    }
}
