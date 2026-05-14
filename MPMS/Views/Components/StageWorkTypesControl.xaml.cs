using System.Windows;
using System.Windows.Controls;
using MPMS.Models;
using MPMS.ViewModels;

namespace MPMS.Views.Components;

public partial class StageWorkTypesControl : UserControl
{
    public StageWorkTypesControl()
    {
        InitializeComponent();
    }

    private void AddWorkTypeTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: LocalWorkTypeTemplate tpl }) return;
        if (DataContext is StageDetailViewModel vm)
            vm.AddWorkTypeTemplateCommand.Execute(tpl);
    }

    private void DecWorkTypeQty_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: StageWorkTypeLineVm line }) return;
        if (DataContext is StageDetailViewModel vm)
            vm.AdjustWorkTypeQuantity(line, -1);
    }

    private void IncWorkTypeQty_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: StageWorkTypeLineVm line }) return;
        if (DataContext is StageDetailViewModel vm)
            vm.AdjustWorkTypeQuantity(line, 1);
    }
}
