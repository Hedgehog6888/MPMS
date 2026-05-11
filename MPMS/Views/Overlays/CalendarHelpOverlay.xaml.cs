using System.Windows;
using System.Windows.Controls;

namespace MPMS.Views.Overlays;

public partial class CalendarHelpOverlay : UserControl
{
    public CalendarHelpOverlay()
    {
        InitializeComponent();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.Instance?.HideDrawer();
    }
}
