using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MPMS.Models;
using MPMS.ViewModels;

namespace MPMS.Views.Components;

public partial class StageMaterialsControl : UserControl
{
    private static readonly Brush FocusBrush = new SolidColorBrush(Colors.Black);
    private static readonly Brush ClearBrush = new SolidColorBrush(Colors.Transparent);

    public StageMaterialsControl()
    {
        InitializeComponent();
    }

    private void ReadOnlySearch_GotFocus(object sender, RoutedEventArgs e)
    {
        ReadOnlySearchBorder.BorderBrush = FocusBrush;
    }

    private void ReadOnlySearch_LostFocus(object sender, RoutedEventArgs e)
    {
        ReadOnlySearchBorder.BorderBrush = ClearBrush;
    }

    private void ReadOnlyClearSearch_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is StageDetailViewModel vm)
            vm.MaterialSearchText = string.Empty;
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
