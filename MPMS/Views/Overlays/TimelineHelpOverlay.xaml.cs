using System.Windows;
using System.Windows.Controls;

namespace MPMS.Views.Overlays;

public partial class TimelineHelpOverlay : UserControl
{
    public TimelineHelpOverlay()
    {
        InitializeComponent();
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        MainWindow.Instance?.HideDrawer();
    }
}
