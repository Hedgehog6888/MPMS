using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MPMS.Models;
using MPMS.ViewModels;

namespace MPMS.Views.Components.Catalogs;

public partial class WorkTypesPanel : UserControl
{
    private static readonly SolidColorBrush FocusBrush = new(Color.FromRgb(0x11, 0x11, 0x11));
    private static readonly SolidColorBrush ClearBrush = new(Colors.Transparent);

    public WorkTypesPanel()
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
        if (DataContext is CatalogsViewModel vm)
            vm.WorkTypeSearchText = string.Empty;
    }

    private void AddWorkType_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is CatalogsViewModel vm)
            vm.AddWorkTypeCommand.Execute(null);
    }

    private void WorkTypeRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Border border && border.DataContext is LocalWorkTypeTemplate wt)
        {
            if (DataContext is CatalogsViewModel vm)
                vm.ViewWorkTypeCommand.Execute(wt);
        }
    }
}
