using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MPMS.ViewModels;
using MPMS.Views.Overlays;

namespace MPMS.Views.Components.Admin
{
    public partial class AdminArchivePanel : UserControl
    {
        private static readonly SolidColorBrush FocusBrush = new(Color.FromRgb(0x11, 0x11, 0x11));
        private static readonly SolidColorBrush ClearBrush = new(Colors.Transparent);

        public AdminArchivePanel()
        {
            InitializeComponent();
        }

        private void ArchiveSearch_GotFocus(object s, RoutedEventArgs e) => ArchiveSearchBorder.BorderBrush = FocusBrush;
        private void ArchiveSearch_LostFocus(object s, RoutedEventArgs e) => ArchiveSearchBorder.BorderBrush = ClearBrush;

        private void ClearArchiveSearch_Click(object s, RoutedEventArgs e)
        {
            if (DataContext is AdminViewModel vm)
                vm.ArchiveSearchText = string.Empty;
        }

        private void ArchiveTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not RadioButton rb) return;
            var tag = rb.Tag?.ToString() ?? "Projects";

            ArchProjPanel.Visibility      = tag == "Projects" ? Visibility.Visible : Visibility.Collapsed;
            ArchTaskPanel.Visibility      = tag == "Tasks"    ? Visibility.Visible : Visibility.Collapsed;
            ArchStagePanel.Visibility     = tag == "Stages"   ? Visibility.Visible : Visibility.Collapsed;
            ArchMaterialPanel.Visibility  = tag == "Materials" ? Visibility.Visible : Visibility.Collapsed;
            ArchEquipmentPanel.Visibility = tag == "Equipment" ? Visibility.Visible : Visibility.Collapsed;

            if (DataContext is AdminViewModel vm)
                _ = vm.RefreshArchiveAsync();
        }

        private void ArchiveRow_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is ArchiveRow row)
            {
                if (DataContext is AdminViewModel vm)
                {
                    var overlay = new ArchiveItemInfoOverlay(row, vm);
                    MainWindow.Instance?.ShowDrawer(overlay);
                }
            }
        }
    }
}
