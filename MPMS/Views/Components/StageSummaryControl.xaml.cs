using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MPMS.ViewModels;

namespace MPMS.Views.Components;

public partial class StageSummaryControl : UserControl
{
    public StageSummaryControl()
    {
        InitializeComponent();
    }

    private void ReceiptRow_Click(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not StageDetailViewModel vm || !vm.CanEditStageSummary) return;
        if (sender is not FrameworkElement { Tag: ReceiptRowVm row }) return;
        vm.OpenReceiptLinePricing(row);
    }

    private void ReceiptName_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is not TextBlock textBlock) return;

        textBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var isTrimmed = textBlock.DesiredSize.Width > textBlock.ActualWidth + 0.5;

        if (isTrimmed)
        {
            var tooltip = new ToolTip
            {
                Content = textBlock.Text,
                Style = (Style)FindResource("ProgramToolTip")
            };
            ToolTipService.SetToolTip(textBlock, tooltip);
            ToolTipService.SetShowDuration(textBlock, 60000);
        }
        else
        {
            ToolTipService.SetToolTip(textBlock, null);
        }
    }

    private void ServiceHeader_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not StageDetailViewModel vm) return;
        if (sender is not Button { Tag: string column }) return;
        vm.SortServices(column);
    }

    private void MaterialHeader_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not StageDetailViewModel vm) return;
        if (sender is not Button { Tag: string column }) return;
        vm.SortMaterials(column);
    }
}
