using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MPMS.Models;
using MPMS.ViewModels;

namespace MPMS.Views.Components;

public partial class StageEquipmentControl : UserControl
{
    private static readonly Brush FocusBrush = new SolidColorBrush(Colors.Black);
    private static readonly Brush ClearBrush = new SolidColorBrush(Colors.Transparent);

    public StageEquipmentControl()
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
            vm.EquipmentSearchText = string.Empty;
    }

    private void AddEquipmentTemplate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: LocalEquipment equipment }) return;
        if (DataContext is StageDetailViewModel vm)
            vm.AddEquipmentTemplateCommand.Execute(equipment);
    }

    private void RemoveEquipmentRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: StageEquipmentLineVm line }) return;
        if (DataContext is StageDetailViewModel vm)
            vm.RemoveEquipmentLineCommand.Execute(line);
    }
}
