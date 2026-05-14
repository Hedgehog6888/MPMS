using System.Windows;
using System.Windows.Controls;
using MPMS.Models;
using MPMS.ViewModels;

namespace MPMS.Views.Components;

public partial class StageMaterialsControl : UserControl
{
    public StageMaterialsControl()
    {
        InitializeComponent();
    }

    private void AddMaterialTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: LocalMaterial material }) return;
        if (DataContext is StageDetailViewModel vm)
            vm.AddMaterialTemplateCommand.Execute(material);
    }

    private void DecMatQty_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: StageMaterialLineVm line }) return;
        if (DataContext is StageDetailViewModel vm)
            vm.AdjustMaterialQuantity(line, -1);
    }

    private void IncMatQty_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: StageMaterialLineVm line }) return;
        if (DataContext is StageDetailViewModel vm)
            vm.AdjustMaterialQuantity(line, 1);
    }

    private void RemoveMaterialRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: StageMaterialLineVm line }) return;
        if (DataContext is StageDetailViewModel vm)
            vm.RemoveMaterialLineCommand.Execute(line);
    }
}
