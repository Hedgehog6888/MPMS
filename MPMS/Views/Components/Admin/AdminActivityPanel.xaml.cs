using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MPMS.ViewModels;

namespace MPMS.Views.Components.Admin
{
    public partial class AdminActivityPanel : UserControl
    {
        private static readonly SolidColorBrush FocusBrush = new(Color.FromRgb(0x11, 0x11, 0x11));
        private static readonly SolidColorBrush ClearBrush = new(Colors.Transparent);

        public AdminActivityPanel()
        {
            InitializeComponent();
        }

        private void ActivitySearch_GotFocus(object s, RoutedEventArgs e) => ActivitySearchBorder.BorderBrush = FocusBrush;
        private void ActivitySearch_LostFocus(object s, RoutedEventArgs e) => ActivitySearchBorder.BorderBrush = ClearBrush;

        private void ClearActivitySearch_Click(object s, RoutedEventArgs e)
        {
            if (DataContext is AdminViewModel vm)
                vm.ActivitySearchText = string.Empty;
        }
    }
}
