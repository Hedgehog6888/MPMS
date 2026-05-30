using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MPMS.Models;
using MPMS.ViewModels;

namespace MPMS.Views.Components;

public partial class StageWorkTypesControl : UserControl
{
    private static readonly Brush FocusBrush = new SolidColorBrush(Colors.Black);
    private static readonly Brush ClearBrush = new SolidColorBrush(Colors.Transparent);

    public StageWorkTypesControl()
    {
        InitializeComponent();
    }

    private void Search_GotFocus(object sender, RoutedEventArgs e)
    {
        SearchBorder.BorderBrush = FocusBrush;
    }

    private void Search_LostFocus(object sender, RoutedEventArgs e)
    {
        SearchBorder.BorderBrush = ClearBrush;
    }

    private void ClearSearch_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is StageDetailViewModel vm)
            vm.ServiceSearchText = string.Empty;
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
            vm.ServiceSearchText = string.Empty;
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
