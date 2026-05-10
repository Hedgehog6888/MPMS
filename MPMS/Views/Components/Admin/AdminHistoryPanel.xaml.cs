using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MPMS.ViewModels;
using MPMS.Models;

namespace MPMS.Views.Components.Admin
{
    public partial class AdminHistoryPanel : UserControl
    {
        private static readonly SolidColorBrush FocusBrush = new(Color.FromRgb(0x11, 0x11, 0x11));
        private static readonly SolidColorBrush ClearBrush = new(Colors.Transparent);

        public AdminHistoryPanel()
        {
            InitializeComponent();
        }

        private void HistorySearch_GotFocus(object s, RoutedEventArgs e) => HistorySearchBorder.BorderBrush = FocusBrush;
        private void HistorySearch_LostFocus(object s, RoutedEventArgs e) => HistorySearchBorder.BorderBrush = ClearBrush;

        private void ClearHistorySearch_Click(object s, RoutedEventArgs e)
        {
            if (DataContext is AdminViewModel vm)
                vm.HistorySearchText = string.Empty;
        }

        private void HistoryRow_Click(object sender, MouseButtonEventArgs e)
        {
            if (sender is Border border && border.DataContext is LocalActivityLog log && DataContext is AdminViewModel vm)
            {
                vm.ViewActivityDetailCommand.Execute(log);
            }
        }
    }
}
