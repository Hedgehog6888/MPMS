using System.Windows;
using System.Windows.Controls;
using MPMS.Models;
using MPMS.ViewModels;

namespace MPMS.Views.Components;

public partial class StageEquipmentControl : UserControl
{
    public StageEquipmentControl()
    {
        InitializeComponent();
    }

    private void AddEquipmentTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: LocalEquipment equipment }) return;
        if (DataContext is StageEditViewModel vm)
            vm.AddEquipmentTemplateCommand.Execute(equipment);
    }

    private void RemoveEquipmentRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: StageEquipmentLineVm line }) return;
        if (DataContext is StageEditViewModel vm)
            vm.RemoveEquipmentLineCommand.Execute(line);
    }
}
