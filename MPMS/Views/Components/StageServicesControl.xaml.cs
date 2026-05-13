using System.Windows;
using System.Windows.Controls;
using MPMS.Models;
using MPMS.ViewModels;

namespace MPMS.Views.Components;

public partial class StageServicesControl : UserControl
{
    public StageServicesControl()
    {
        InitializeComponent();
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
}
